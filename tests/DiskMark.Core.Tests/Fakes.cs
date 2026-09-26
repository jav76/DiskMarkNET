using DiskMark.Core.Common;
using DiskMark.Core.Engine;
using DiskMark.Core.Hardware;
using DiskMark.Core.Memory;
using DiskMark.Core.Telemetry;
using Microsoft.Win32.SafeHandles;

namespace DiskMark.Core.Tests;

/// <summary>Deterministic engine: each call returns the next throughput from a list and records what was run.</summary>
internal sealed class FakeIoEngine(params double[] megabytesPerSecondSequence) : IIoEngine
{
    private int _calls;

    public string Name => "fake";

    public List<(TestSpec Spec, IoMode Mode)> Calls { get; } = [];

    public Action<int>? OnCall { get; init; }

    public MeasurementResult Run(IoRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add((request.Spec, request.Mode));
        OnCall?.Invoke(_calls);
        double mbps = megabytesPerSecondSequence.Length == 0 ? 100 : megabytesPerSecondSequence[_calls % megabytesPerSecondSequence.Length];
        _calls++;

        double seconds = request.Duration.TotalSeconds;
        long bytes = (long)(mbps * 1_000_000 * seconds);
        long operations = bytes / request.Spec.BlockSize;
        var histogram = new LatencyHistogram();
        for (int i = 0; i < 100; i++)
            histogram.Record(10_000 + (i * 1_000));
        request.Counters.Publish(0, bytes, operations);
        cancellationToken.ThrowIfCancellationRequested();
        return new MeasurementResult(bytes, operations, seconds, histogram);
    }
}

internal sealed class FakeTarget : IBenchmarkTarget
{
    public SafeFileHandle Handle { get; } = new();

    public long Length { get; init; } = 64 * ByteSize.MiB;

    public IoAlignment Alignment { get; init; } = new(4096, 4096);

    public bool Prepared { get; private set; }

    public bool Disposed { get; private set; }

    public double? CachedFraction { get; init; }

    public void Prepare(DataPattern pattern, Action<long> progress, CancellationToken cancellationToken)
    {
        progress(Length);
        Prepared = true;
    }

    public double? GetCachedFraction() => CachedFraction;

    public void Dispose() => Disposed = true;
}

internal static class FakeRunner
{
    public static FileSystemDetails LocalDisk(long available = 500 * ByteSize.GiB) =>
        new("ext4", "/", "/dev/fake", FileSystemSupport.Supported, false, 1024 * ByteSize.GiB, available, null);

    public static BenchmarkOptions Options(int passes = 2) => new()
    {
        TargetDirectory = ".",
        TestFileSize = 64 * ByteSize.MiB,
        Passes = passes,
        MeasureDuration = TimeSpan.FromSeconds(1),
        IntervalDuration = TimeSpan.Zero,
    };

    public static BenchmarkRunner Create(
        BenchmarkOptions options,
        IIoEngine engine,
        FakeTarget? target = null,
        FileSystemDetails? fileSystem = null) => new(
            options,
            engine,
            _ => target ?? new FakeTarget(),
            _ => fileSystem ?? LocalDisk(),
            (_, _) => new StorageDeviceInfo("Fake SSD", "1.0", "NVMe", true, 512, 4096, ByteSize.TiB, "/dev/fake"));
}
