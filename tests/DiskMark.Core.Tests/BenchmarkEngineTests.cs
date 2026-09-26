using DiskMark.Core.Common;
using DiskMark.Core.Engine;
using DiskMark.Core.Hardware;
using TestResult = DiskMark.Core.Common.TestResult;

namespace DiskMark.Core.Tests;

public class BenchmarkEngineTests
{
    [Fact]
    public void Run_MeasuresAllReadsThenAllWrites_ForEveryPass()
    {
        var engine = new FakeIoEngine();
        var target = new FakeTarget();
        var result = FakeRunner.Create(FakeRunner.Options(passes: 2), engine, target).Run(TestContext.Current.CancellationToken);

        var tests = BenchmarkProfile.Default.Tests;
        var expected = tests.SelectMany(t => new[] { (t, IoMode.Read), (t, IoMode.Read) })
            .Concat(tests.SelectMany(t => new[] { (t, IoMode.Write), (t, IoMode.Write) }));
        Assert.Equal(expected, engine.Calls);
        Assert.True(result.Completed);
        Assert.False(result.Cancelled);
        Assert.Equal(8, result.Tests.Count);
        Assert.True(target.Prepared);
        Assert.True(target.Disposed);
    }

    [Fact]
    public void Run_ReportsBestPass_AndMedian()
    {
        var options = FakeRunner.Options(passes: 3) with { IncludeWrites = false, Profile = OneTestProfile() };
        var result = FakeRunner.Create(options, new FakeIoEngine(100, 300, 200)).Run(TestContext.Current.CancellationToken);

        var test = Assert.Single(result.Tests);
        Assert.Equal(3, test.Passes.Count);
        Assert.Equal(300, test.MaxMegabytesPerSecond, 0.01);
        Assert.Equal(200, test.MedianMegabytesPerSecond, 0.01);
        Assert.Equal(Math.Floor(300_000_000d / test.Spec.BlockSize), test.MaxIops, 0.001);
        Assert.Equal(300, test.Latency.Samples);
    }

    [Fact]
    public void Run_RaisesResultUpdated_AfterEveryPass()
    {
        var runner = FakeRunner.Create(FakeRunner.Options(passes: 3), new FakeIoEngine());
        var updates = new List<TestResult>();
        runner.ResultUpdated += updates.Add;

        runner.Run(TestContext.Current.CancellationToken);

        Assert.Equal(8 * 3, updates.Count);
        Assert.Equal([1, 2, 3], updates.Take(3).Select(u => u.Passes.Count));
    }

    [Fact]
    public void Run_Cancelled_ReturnsPartialResults()
    {
        using var cts = new CancellationTokenSource();
        var engine = new FakeIoEngine { OnCall = call => { if (call == 2) cts.Cancel(); } };
        var target = new FakeTarget();

        var result = FakeRunner.Create(FakeRunner.Options(passes: 1), engine, target).Run(cts.Token);

        Assert.False(result.Completed);
        Assert.True(result.Cancelled);
        Assert.Equal(2, result.Tests.Count);
        Assert.True(target.Disposed);
    }

    [Fact]
    public void Run_UnsupportedFileSystem_Throws()
    {
        var fileSystem = FakeRunner.LocalDisk() with { Type = "proc", Support = FileSystemSupport.Unsupported, Note = "pseudo" };
        var error = Assert.Throws<BenchmarkException>(() =>
            FakeRunner.Create(FakeRunner.Options(), new FakeIoEngine(), fileSystem: fileSystem).Run(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkErrorKind.FileSystem, error.Kind);
    }

    [Fact]
    public void Run_InsufficientSpace_Throws()
    {
        var error = Assert.Throws<BenchmarkException>(() =>
            FakeRunner.Create(FakeRunner.Options(), new FakeIoEngine(), fileSystem: FakeRunner.LocalDisk(available: 10 * ByteSize.MiB)).Run(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkErrorKind.InsufficientSpace, error.Kind);
    }

    [Fact]
    public void Run_InvalidOptions_Throws()
    {
        var error = Assert.Throws<BenchmarkException>(() =>
            FakeRunner.Create(FakeRunner.Options() with { Passes = 0 }, new FakeIoEngine()).Run(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkErrorKind.InvalidOptions, error.Kind);
    }

    [Fact]
    public void Run_BlockSizeNotAlignedToDevice_Throws()
    {
        var target = new FakeTarget { Alignment = new(8192, 8192) };
        var error = Assert.Throws<BenchmarkException>(() =>
            FakeRunner.Create(FakeRunner.Options(), new FakeIoEngine(), target).Run(TestContext.Current.CancellationToken));
        Assert.Equal(BenchmarkErrorKind.InvalidOptions, error.Kind);
        Assert.True(target.Disposed);
    }

    [Fact]
    public void Run_ImplausibleResults_AddCacheWarnings()
    {
        var target = new FakeTarget { CachedFraction = 0.9 };
        var result = FakeRunner.Create(FakeRunner.Options(passes: 1), new FakeIoEngine(50_000), target).Run(TestContext.Current.CancellationToken);

        Assert.Contains(result.Warnings, w => w.Contains("faster than any single-drive interface"));
        Assert.Contains(result.Warnings, w => w.Contains("page cache"));
    }

    [Fact]
    public void Run_CachingFileSystem_AddsNote()
    {
        var fileSystem = FakeRunner.LocalDisk() with { Type = "tmpfs", Support = FileSystemSupport.Caching, Note = "tmpfs note" };
        var result = FakeRunner.Create(FakeRunner.Options(passes: 1), new FakeIoEngine(), fileSystem: fileSystem).Run(TestContext.Current.CancellationToken);
        Assert.Contains("tmpfs note", result.Warnings);
    }

    [Fact]
    public void Snapshot_ReportsCompletion()
    {
        var runner = FakeRunner.Create(FakeRunner.Options(passes: 1), new FakeIoEngine());
        Assert.Equal(BenchmarkPhase.Idle, runner.GetSnapshot().Phase);

        runner.Run(TestContext.Current.CancellationToken);

        var snapshot = runner.GetSnapshot();
        Assert.Equal(BenchmarkPhase.Completed, snapshot.Phase);
        Assert.Equal(1, snapshot.OverallProgress);
    }

    [Theory]
    [InlineData(new double[] { 5 }, 5)]
    [InlineData(new double[] { 3, 1, 2 }, 2)]
    [InlineData(new double[] { 4, 1, 3, 2 }, 2.5)]
    [InlineData(new double[0], 0)]
    public void Median_HandlesOddEvenAndEmpty(double[] values, double expected)
    {
        Assert.Equal(expected, BenchmarkRunner.Median(values));
    }

    [Fact]
    public void RandomOffsets_AreBlockAligned_AndInsideFile()
    {
        var spec = new TestSpec(AccessPattern.Random, 4096, 32, 1);
        long length = 64 * ByteSize.MiB;
        var offsets = OffsetSource.Create(spec, length, null);
        var seen = new HashSet<long>();

        for (int i = 0; i < 50_000; i++)
        {
            long offset = offsets.Next();
            Assert.Equal(0, offset % spec.BlockSize);
            Assert.InRange(offset, 0, length - spec.BlockSize);
            seen.Add(offset);
        }

        Assert.True(seen.Count > 10_000, "Random offsets should spread across the file.");
    }

    [Fact]
    public void SequentialCursor_IsSharedAndWrapsAtEnd()
    {
        var spec = new TestSpec(AccessPattern.Sequential, (int)ByteSize.MiB, 8, 1);
        var cursor = new SequentialCursor(4 * ByteSize.MiB, spec.BlockSize);
        var first = OffsetSource.Create(spec, 4 * ByteSize.MiB, cursor);
        var second = OffsetSource.Create(spec, 4 * ByteSize.MiB, cursor);

        long[] offsets = [first.Next(), second.Next(), first.Next(), second.Next(), first.Next()];

        Assert.Equal([0, ByteSize.MiB, 2 * ByteSize.MiB, 3 * ByteSize.MiB, 0], offsets);
    }

    private static BenchmarkProfile OneTestProfile() =>
        new("one", "single test", [new TestSpec(AccessPattern.Random, 4096, 1, 1)]);
}
