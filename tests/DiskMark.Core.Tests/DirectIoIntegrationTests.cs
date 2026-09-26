using DiskMark.Core.Common;
using DiskMark.Core.Engine;
using DiskMark.Core.Memory;
using DiskMark.Core.Telemetry;

namespace DiskMark.Core.Tests;

/// <summary>Real direct I/O against DISKMARK_TEST_DIR (default: .testdata/ in the repository).</summary>
public class DirectIoIntegrationTests
{
    private const long FileSize = 32 * ByteSize.MiB;

    [Fact]
    public void TestFile_IsHiddenWhileOpen_AndRemovedAfterDispose()
    {
        string directory = TestDirectory.GetForDirectIo();
        using (var file = TestFile.Create(directory, FileSize, writeThrough: false))
        {
            Assert.False(file.Handle.IsInvalid);
            if (!OperatingSystem.IsWindows())
                Assert.Empty(TestDirectory.LeftoverTestFiles(directory));
        }

        Assert.Empty(TestDirectory.LeftoverTestFiles(directory));
    }

    /// <summary>Buffered I/O accepts any offset; unbuffered I/O (O_DIRECT, NO_BUFFERING) rejects unaligned ones.</summary>
    [Fact]
    public void DirectIo_IsActive_UnalignedOffsetIsRejected()
    {
        if (OperatingSystem.IsMacOS())
            Assert.Skip("macOS F_NOCACHE does not impose alignment rules.");

        string directory = TestDirectory.GetForDirectIo();
        using var file = TestFile.Create(directory, FileSize, writeThrough: false);
        file.Prepare(DataPattern.Random, _ => { }, TestContext.Current.CancellationToken);
        using var buffer = new AlignedBuffer(4096, file.Alignment.Memory);

        var error = Record.Exception(() => RandomAccess.Read(file.Handle, buffer.Span, 1));

        Assert.IsAssignableFrom<IOException>(error);
    }

    [Fact]
    public void Prepare_WritesWholeFile_WithoutFillingPageCache()
    {
        string directory = TestDirectory.GetForDirectIo();
        using var file = TestFile.Create(directory, FileSize, writeThrough: false);
        long reported = 0;

        file.Prepare(DataPattern.Random, bytes => reported = bytes, TestContext.Current.CancellationToken);

        Assert.Equal(FileSize, reported);
        Assert.Equal(FileSize, RandomAccess.GetLength(file.Handle));
        using var buffer = new AlignedBuffer(4096, file.Alignment.Memory);
        RandomAccess.Read(file.Handle, buffer.Span, FileSize - 4096);
        Assert.Contains(buffer.Span.ToArray(), b => b != 0);

        // On macOS this also proves F_NOCACHE took effect through the padded variadic fcntl call.
        if (file.GetCachedFraction() is { } cached)
            Assert.True(cached < 0.05, $"The OS cache holds {cached:P0} of the test file.");
    }

    [Theory]
    [InlineData("threaded", IoMode.Read)]
    [InlineData("threaded", IoMode.Write)]
    [InlineData("async", IoMode.Read)]
    [InlineData("async", IoMode.Write)]
    public void Engines_CompleteShortMeasurements(string engineName, IoMode mode)
    {
        string directory = TestDirectory.GetForDirectIo();
        using var file = TestFile.Create(directory, FileSize, writeThrough: false);
        file.Prepare(DataPattern.Random, _ => { }, TestContext.Current.CancellationToken);
        var spec = new TestSpec(AccessPattern.Random, 4096, 4, 1);
        var counters = new LiveCounters(spec.OutstandingIos);

        var result = IoEngineFactory.Create(engineName).Run(
            new IoRequest
            {
                Handle = file.Handle,
                FileLength = file.Length,
                Spec = spec,
                Mode = mode,
                Duration = TimeSpan.FromMilliseconds(300),
                Alignment = file.Alignment,
                Counters = counters,
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.Operations > 0);
        Assert.Equal(result.Operations * 4096, result.Bytes);
        Assert.Equal(result.Operations, result.Histogram.Count);
        Assert.Equal((result.Bytes, result.Operations), counters.Read());
    }

    [Fact]
    public void Runner_CompletesSmallRun_AndCleansUp()
    {
        string directory = TestDirectory.GetForDirectIo();
        var options = new BenchmarkOptions
        {
            TargetDirectory = directory,
            TestFileSize = 16 * ByteSize.MiB,
            Passes = 1,
            MeasureDuration = TimeSpan.FromMilliseconds(200),
            IntervalDuration = TimeSpan.Zero,
        };

        var result = new BenchmarkRunner(options).Run(TestContext.Current.CancellationToken);

        Assert.True(result.Completed, result.Error);
        Assert.Equal(8, result.Tests.Count);
        Assert.All(result.Tests, t => Assert.True(t.MaxMegabytesPerSecond > 0, $"{t.Name} measured nothing"));
        Assert.NotNull(result.Environment.FileSystem);
        Assert.Empty(TestDirectory.LeftoverTestFiles(directory));
    }

    [Fact]
    public void Runner_MissingDirectory_ThrowsTargetError()
    {
        var options = new BenchmarkOptions { TargetDirectory = Path.Combine(TestDirectory.Get(), "missing-" + Guid.NewGuid().ToString("N")) };
        var error = Assert.Throws<BenchmarkException>(() => new BenchmarkRunner(options).Run(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkErrorKind.Target, error.Kind);
    }
}
