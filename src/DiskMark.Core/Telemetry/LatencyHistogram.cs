using System.Diagnostics;
using System.Numerics;
using DiskMark.Core.Common;

namespace DiskMark.Core.Telemetry;

/// <summary>
/// HdrHistogram-style log-linear latency histogram in nanoseconds. Values below 256 ns are exact; above that, each
/// power-of-two range is split into 128 sub-buckets, bounding the relative error at 1/128 (under 0.8%).
/// Not thread-safe: each I/O worker owns one and they are merged after a measurement.
/// </summary>
public sealed class LatencyHistogram
{
    private const int LinearLimit = 256;
    private const int SubBucketBits = 7;
    private const int SubBucketCount = 1 << SubBucketBits;

    /// <summary>Covers values up to 2^34 ns (about 17 s); larger values land in the last bucket.</summary>
    public const int BucketCount = LinearLimit + 26 * SubBucketCount;

    private static readonly long s_nanosecondsPerTick =
        1_000_000_000L % Stopwatch.Frequency == 0 ? 1_000_000_000L / Stopwatch.Frequency : 0;

    private static readonly double s_nanosecondsPerTickFraction = 1e9 / Stopwatch.Frequency;

    private readonly long[] _counts = new long[BucketCount];
    private long _min = long.MaxValue;
    private long _max;
    private double _sum;

    public long Count { get; private set; }

    public long MinNanoseconds => Count == 0 ? 0 : _min;

    public long MaxNanoseconds => _max;

    public double MeanNanoseconds => Count == 0 ? 0 : _sum / Count;

    public static long TicksToNanoseconds(long ticks) =>
        s_nanosecondsPerTick != 0 ? ticks * s_nanosecondsPerTick : (long)(ticks * s_nanosecondsPerTickFraction);

    public void RecordTicks(long elapsedTicks) => Record(TicksToNanoseconds(elapsedTicks));

    public void Record(long nanoseconds)
    {
        if (nanoseconds < 0)
            nanoseconds = 0;

        _counts[IndexOf(nanoseconds)]++;
        Count++;
        _sum += nanoseconds;
        if (nanoseconds < _min)
            _min = nanoseconds;
        if (nanoseconds > _max)
            _max = nanoseconds;
    }

    public void Add(LatencyHistogram other)
    {
        for (int i = 0; i < BucketCount; i++)
            _counts[i] += other._counts[i];

        if (other.Count == 0)
            return;

        Count += other.Count;
        _sum += other._sum;
        _min = Math.Min(_min, other._min);
        _max = Math.Max(_max, other._max);
    }

    /// <param name="percentile">0 to 100.</param>
    public double GetPercentileNanoseconds(double percentile)
    {
        if (Count == 0)
            return 0;

        long rank = Math.Clamp((long)Math.Ceiling(percentile / 100d * Count), 1, Count);
        long cumulative = 0;
        for (int i = 0; i < BucketCount; i++)
        {
            cumulative += _counts[i];
            if (cumulative >= rank)
            {
                var (lower, upper) = BucketRange(i);
                return Math.Clamp((lower + upper) / 2d, _min, _max);
            }
        }

        return _max;
    }

    public LatencySummary Summarize() => Count == 0
        ? LatencySummary.Empty
        : new LatencySummary(
            Count,
            MinNanoseconds / 1000d,
            MeanNanoseconds / 1000d,
            GetPercentileNanoseconds(50) / 1000d,
            GetPercentileNanoseconds(90) / 1000d,
            GetPercentileNanoseconds(95) / 1000d,
            GetPercentileNanoseconds(99) / 1000d,
            GetPercentileNanoseconds(99.9) / 1000d,
            MaxNanoseconds / 1000d);

    /// <summary>Coarse log-scale bins for charts, in microseconds.</summary>
    /// <param name="binsPerDecade">0 picks finer bins for narrow distributions.</param>
    public IReadOnlyList<HistogramBucket> ToDisplayBuckets(int binsPerDecade = 0)
    {
        if (Count == 0)
            return [];

        double lowDecade = Math.Floor(Math.Log10(Math.Max(MinNanoseconds, 1)));
        double highDecade = Math.Ceiling(Math.Log10(Math.Max(MaxNanoseconds, 1) + 1));
        if (binsPerDecade <= 0)
            binsPerDecade = highDecade - lowDecade <= 2 ? 25 : 12;
        int binCount = Math.Max(1, (int)((highDecade - lowDecade) * binsPerDecade));
        var bins = new long[binCount];

        for (int i = 0; i < BucketCount; i++)
        {
            if (_counts[i] == 0)
                continue;
            var (lower, upper) = BucketRange(i);
            double mid = Math.Max((lower + upper) / 2d, 1);
            int bin = Math.Clamp((int)((Math.Log10(mid) - lowDecade) * binsPerDecade), 0, binCount - 1);
            bins[bin] += _counts[i];
        }

        int first = Array.FindIndex(bins, c => c > 0);
        int last = Array.FindLastIndex(bins, c => c > 0);
        var result = new List<HistogramBucket>(last - first + 1);
        for (int b = first; b <= last; b++)
        {
            double lowerNs = Math.Pow(10, lowDecade + b / (double)binsPerDecade);
            double upperNs = Math.Pow(10, lowDecade + (b + 1) / (double)binsPerDecade);
            result.Add(new HistogramBucket(lowerNs / 1000d, upperNs / 1000d, bins[b]));
        }

        return result;
    }

    public void Reset()
    {
        Array.Clear(_counts);
        Count = 0;
        _sum = 0;
        _min = long.MaxValue;
        _max = 0;
    }

    internal static int IndexOf(long nanoseconds)
    {
        if (nanoseconds < LinearLimit)
            return (int)nanoseconds;

        int msb = 63 - BitOperations.LeadingZeroCount((ulong)nanoseconds);
        int shift = msb - SubBucketBits;
        int subBucket = (int)(nanoseconds >> shift) - SubBucketCount;
        int index = LinearLimit + (shift - 1) * SubBucketCount + subBucket;
        return Math.Min(index, BucketCount - 1);
    }

    /// <returns>Inclusive nanosecond range covered by a bucket.</returns>
    internal static (long Lower, long Upper) BucketRange(int index)
    {
        if (index < LinearLimit)
            return (index, index);

        int offset = index - LinearLimit;
        int shift = offset / SubBucketCount + 1;
        long subBucket = offset % SubBucketCount + SubBucketCount;
        return (subBucket << shift, ((subBucket + 1) << shift) - 1);
    }
}
