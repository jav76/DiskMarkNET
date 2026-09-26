using DiskMark.Core.Memory;

namespace DiskMark.Core.Tests;

public class AlignedBufferTests
{
    [Theory]
    [InlineData(4096, 4096)]
    [InlineData(1 << 20, 4096)]
    [InlineData(4096, 65536)]
    public void Address_IsAligned_AndMemoryCoversBuffer(int length, int alignment)
    {
        using var buffer = new AlignedBuffer(length, alignment);

        Assert.Equal(0, buffer.Address % alignment);
        Assert.Equal(length, buffer.Span.Length);
        Assert.Equal(length, buffer.Memory.Length);
        Assert.All(buffer.Span.ToArray(), b => Assert.Equal(0, b));
    }

    [Fact]
    public void FillRandom_WritesNonZeroData()
    {
        using var buffer = new AlignedBuffer(4096, 4096);
        buffer.FillRandom();
        Assert.Contains(buffer.Span.ToArray(), b => b != 0);
    }

    [Fact]
    public void Constructor_RejectsNonPowerOfTwoAlignment()
    {
        Assert.Throws<ArgumentException>(() => new AlignedBuffer(4096, 3000));
    }

    [Fact]
    public void Span_AfterDispose_Throws()
    {
        var buffer = new AlignedBuffer(4096, 4096);
        buffer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => buffer.GetSpan());
    }
}
