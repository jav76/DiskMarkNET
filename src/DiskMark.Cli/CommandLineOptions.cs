using DiskMark.Core.Common;

namespace DiskMark.Cli;

internal enum CliCommand
{
    Run,
    Targets,
    Profiles,
    Help,
    Version,
}

internal sealed record CommandLineOptions
{
    public CliCommand Command { get; init; } = CliCommand.Help;
    public string? TargetDirectory { get; init; }
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
    public string Engine { get; init; } = "auto";

    /// <summary>Write JSON instead of the table: to stdout when <see cref="JsonPath"/> is null, otherwise to that file.</summary>
    public bool Json { get; init; }
    public string? JsonPath { get; init; }
    public bool NoColor { get; init; }

    public BenchmarkOptions ToBenchmarkOptions() => new()
    {
        TargetDirectory = TargetDirectory ?? ".",
        Profile = Profile,
        TestFileSize = TestFileSize,
        Passes = Passes,
        MeasureDuration = MeasureDuration,
        IntervalDuration = IntervalDuration,
        WarmupDuration = WarmupDuration,
        DataPattern = DataPattern,
        WriteThrough = WriteThrough,
        IncludeReads = IncludeReads,
        IncludeWrites = IncludeWrites,
    };
}
