using DiskMark.Core.Hardware;

namespace DiskMark.Core.Common;

/// <summary>One timed measurement. Throughput is decimal: 1 MB/s = 1,000,000 bytes/s.</summary>
public sealed record PassResult(long Bytes, long Operations, double ElapsedSeconds, double MegabytesPerSecond, double Iops)
{
    public static PassResult Create(long bytes, long operations, double elapsedSeconds) => elapsedSeconds > 0
        ? new(bytes, operations, elapsedSeconds, bytes / elapsedSeconds / 1_000_000d, operations / elapsedSeconds)
        : new(bytes, operations, 0, 0, 0);
}

public sealed record LatencySummary(
    long Samples,
    double MinUs,
    double MeanUs,
    double P50Us,
    double P90Us,
    double P95Us,
    double P99Us,
    double P999Us,
    double MaxUs)
{
    public static LatencySummary Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0);
}

public sealed record HistogramBucket(double LowerUs, double UpperUs, long Count);

/// <summary>
/// Aggregated result for one test and direction. The headline values are the best pass (CrystalDiskMark parity);
/// medians and every pass are kept for regression tracking. Latency covers all passes combined.
/// </summary>
public sealed record TestResult(
    TestSpec Spec,
    IoMode Mode,
    IReadOnlyList<PassResult> Passes,
    double MaxMegabytesPerSecond,
    double MedianMegabytesPerSecond,
    double MaxIops,
    double MedianIops,
    LatencySummary Latency,
    IReadOnlyList<HistogramBucket> Histogram)
{
    public string Name => $"{Spec.Name} {Mode}";
}

public sealed record RunConfiguration(
    string Profile,
    long TestFileSize,
    int Passes,
    double MeasureSeconds,
    double IntervalSeconds,
    double WarmupSeconds,
    DataPattern DataPattern,
    bool WriteThrough,
    string Engine);

public sealed record RunEnvironment(
    string AppVersion,
    string OperatingSystem,
    string Architecture,
    int ProcessorCount,
    string TargetDirectory,
    FileSystemDetails? FileSystem,
    StorageDeviceInfo? Device,
    int MemoryAlignment,
    int OffsetAlignment);

public sealed record RunResult(
    int SchemaVersion,
    DateTimeOffset StartedAt,
    double DurationSeconds,
    bool Completed,
    bool Cancelled,
    string? Error,
    RunConfiguration Configuration,
    RunEnvironment Environment,
    IReadOnlyList<TestResult> Tests,
    IReadOnlyList<string> Warnings)
{
    public const int CurrentSchemaVersion = 1;

    public TestResult? Find(TestSpec spec, IoMode mode) =>
        Tests.FirstOrDefault(t => t.Mode == mode && t.Spec == spec);
}
