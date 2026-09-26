namespace DiskMark.Core.Common;

/// <summary>Benchmark settings. Defaults match CrystalDiskMark: 1 GiB, 5 passes, 5 s per test, 5 s interval.</summary>
public sealed record BenchmarkOptions
{
    public required string TargetDirectory { get; init; }
    public BenchmarkProfile Profile { get; init; } = BenchmarkProfile.Default;
    public long TestFileSize { get; init; } = ByteSize.GiB;
    public int Passes { get; init; } = 5;
    public TimeSpan MeasureDuration { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan IntervalDuration { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan WarmupDuration { get; init; } = TimeSpan.Zero;
    public DataPattern DataPattern { get; init; } = DataPattern.Random;
    public bool WriteThrough { get; init; }
    public bool IncludeReads { get; init; } = true;
    public bool IncludeWrites { get; init; } = true;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TargetDirectory))
            throw Invalid("A target directory is required.");
        if (TestFileSize < ByteSize.MiB || TestFileSize % ByteSize.MiB != 0)
            throw Invalid("Test file size must be a whole number of MiB (minimum 1 MiB).");
        if (Passes is < 1 or > 100)
            throw Invalid("Passes must be between 1 and 100.");
        if (MeasureDuration <= TimeSpan.Zero || MeasureDuration > TimeSpan.FromHours(1))
            throw Invalid("Measure duration must be greater than 0 and at most 1 hour.");
        if (IntervalDuration < TimeSpan.Zero || WarmupDuration < TimeSpan.Zero)
            throw Invalid("Interval and warmup durations cannot be negative.");
        if (!IncludeReads && !IncludeWrites)
            throw Invalid("At least one of reads or writes must be enabled.");
        if (Profile.Tests.Count == 0)
            throw Invalid($"Profile '{Profile.Name}' has no tests.");

        foreach (var spec in Profile.Tests)
        {
            if (spec.BlockSize <= 0 || spec.QueueDepth < 1 || spec.Threads < 1)
                throw Invalid($"Test {spec.Name} has an invalid block size, queue depth, or thread count.");
            if (spec.BlockSize > TestFileSize)
                throw Invalid($"Test file size {ByteSize.Format(TestFileSize)} is smaller than the {spec.Name} block size.");
        }
    }

    private static BenchmarkException Invalid(string message) => new(BenchmarkErrorKind.InvalidOptions, message);
}
