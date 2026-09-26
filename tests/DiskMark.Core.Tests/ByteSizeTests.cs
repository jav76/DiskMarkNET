using DiskMark.Core.Common;

namespace DiskMark.Core.Tests;

public class ByteSizeTests
{
    [Theory]
    [InlineData("1GiB", ByteSize.GiB)]
    [InlineData("1G", ByteSize.GiB)]
    [InlineData("64MB", 64 * ByteSize.MiB)]
    [InlineData("64mib", 64 * ByteSize.MiB)]
    [InlineData("512K", 512 * ByteSize.KiB)]
    [InlineData("1.5GiB", 3 * ByteSize.GiB / 2)]
    [InlineData("4096", 4096)]
    public void TryParse_AcceptsBinaryUnits(string text, long expected)
    {
        Assert.True(ByteSize.TryParse(text, out long bytes));
        Assert.Equal(expected, bytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("10XB")]
    [InlineData("0")]
    [InlineData("-5M")]
    public void TryParse_RejectsInvalidInput(string text)
    {
        Assert.False(ByteSize.TryParse(text, out _));
    }

    [Theory]
    [InlineData(ByteSize.GiB, "1 GiB")]
    [InlineData(64 * ByteSize.MiB, "64 MiB")]
    [InlineData(3 * ByteSize.GiB / 2, "1.5 GiB")]
    public void Format_UsesBinaryUnits(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSize.Format(bytes));
    }

    [Fact]
    public void TestSpecNames_MatchCrystalDiskMark()
    {
        Assert.Equal(["SEQ1M Q8T1", "SEQ1M Q1T1", "RND4K Q32T1", "RND4K Q1T1"], BenchmarkProfile.Default.Tests.Select(t => t.Name));
        Assert.Equal(["SEQ1M Q8T1", "SEQ128K Q32T1", "RND4K Q32T16", "RND4K Q1T1"], BenchmarkProfile.Nvme.Tests.Select(t => t.Name));
    }
}
