using DiskMark.Core.Common;

namespace DiskMark.Core.Telemetry;

/// <summary>Point-in-time view of a running benchmark, meant to be polled (about 5-10 Hz) by UIs.</summary>
/// <param name="Bytes">Bytes completed in the current phase (preparation or measurement).</param>
/// <param name="OverallProgress">0 to 1 across preparation and all measurements.</param>
public readonly record struct BenchmarkSnapshot(
    BenchmarkPhase Phase,
    TestSpec? Spec,
    IoMode? Mode,
    int Pass,
    int PassCount,
    double PhaseElapsedSeconds,
    double PhaseDurationSeconds,
    long Bytes,
    long Operations,
    long PhaseTotalBytes,
    double OverallProgress);
