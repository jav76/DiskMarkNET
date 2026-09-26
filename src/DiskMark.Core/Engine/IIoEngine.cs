using DiskMark.Core.Common;
using DiskMark.Core.Memory;
using DiskMark.Core.Telemetry;
using Microsoft.Win32.SafeHandles;

namespace DiskMark.Core.Engine;

/// <summary>Runs one timed measurement of a test spec against an open direct I/O handle.</summary>
public interface IIoEngine
{
    string Name { get; }

    MeasurementResult Run(IoRequest request, CancellationToken cancellationToken);
}

public sealed class IoRequest
{
    public required SafeFileHandle Handle { get; init; }

    public required long FileLength { get; init; }

    public required TestSpec Spec { get; init; }

    public required IoMode Mode { get; init; }

    public required TimeSpan Duration { get; init; }

    public TimeSpan Warmup { get; init; }

    public required IoAlignment Alignment { get; init; }

    public DataPattern DataPattern { get; init; }

    /// <summary>One slot per outstanding I/O (<see cref="TestSpec.OutstandingIos"/>).</summary>
    public required LiveCounters Counters { get; init; }
}

/// <param name="ElapsedSeconds">Length of the counted window. Only I/Os that started and finished inside it count.</param>
public sealed record MeasurementResult(long Bytes, long Operations, double ElapsedSeconds, LatencyHistogram Histogram);
