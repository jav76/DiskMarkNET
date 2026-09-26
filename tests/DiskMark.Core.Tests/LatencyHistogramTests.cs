using DiskMark.Core.Telemetry;

namespace DiskMark.Core.Tests;

public class LatencyHistogramTests
{
    [Fact]
    public void Buckets_ContainTheirValues_WithinOnePercent()
    {
        var random = new Random(42);
        for (int i = 0; i < 100_000; i++)
        {
            long value = i < 1000 ? i : random.NextInt64(1, 1L << 34);
            var (lower, upper) = LatencyHistogram.BucketRange(LatencyHistogram.IndexOf(value));
            Assert.InRange(value, lower, upper);
            Assert.True((upper - lower) / (double)Math.Max(1, lower) <= 1 / 128d, $"Bucket for {value} is too wide: {lower}-{upper}");
        }
    }

    [Fact]
    public void BucketIndexes_AreContiguous()
    {
        for (int index = 0; index < LatencyHistogram.BucketCount - 1; index++)
        {
            var (_, upper) = LatencyHistogram.BucketRange(index);
            var (nextLower, _) = LatencyHistogram.BucketRange(index + 1);
            Assert.Equal(upper + 1, nextLower);
        }
    }

    [Fact]
    public void Percentiles_OfUniformDistribution_AreAccurate()
    {
        var histogram = new LatencyHistogram();
        for (long ns = 1; ns <= 100_000; ns++)
            histogram.Record(ns);

        Assert.Equal(100_000, histogram.Count);
        Assert.Equal(50_000, histogram.GetPercentileNanoseconds(50), 50_000 * 0.01);
        Assert.Equal(99_000, histogram.GetPercentileNanoseconds(99), 99_000 * 0.01);
        Assert.Equal(99_900, histogram.GetPercentileNanoseconds(99.9), 99_900 * 0.01);
        Assert.Equal(50_000.5, histogram.MeanNanoseconds, 0.001);
        Assert.Equal(1, histogram.MinNanoseconds);
        Assert.Equal(100_000, histogram.MaxNanoseconds);
    }

    [Fact]
    public void Add_MatchesRecordingEverythingInOne()
    {
        var a = new LatencyHistogram();
        var b = new LatencyHistogram();
        var combined = new LatencyHistogram();
        var random = new Random(7);
        for (int i = 0; i < 10_000; i++)
        {
            long value = random.NextInt64(1_000, 5_000_000);
            (i % 2 == 0 ? a : b).Record(value);
            combined.Record(value);
        }

        a.Add(b);

        Assert.Equal(combined.Summarize(), a.Summarize());
    }

    [Fact]
    public void Summarize_Empty_ReturnsEmpty()
    {
        Assert.Same(Common.LatencySummary.Empty, new LatencyHistogram().Summarize());
        Assert.Empty(new LatencyHistogram().ToDisplayBuckets());
    }

    [Fact]
    public void DisplayBuckets_PreserveCount_AndUseMicroseconds()
    {
        var histogram = new LatencyHistogram();
        for (int i = 0; i < 1000; i++)
            histogram.Record(20_000 + (i * 100));

        var buckets = histogram.ToDisplayBuckets();

        Assert.Equal(histogram.Count, buckets.Sum(b => b.Count));
        Assert.True(buckets[0].LowerUs <= 20);
        Assert.True(buckets[^1].UpperUs >= 119.9);
    }
}
