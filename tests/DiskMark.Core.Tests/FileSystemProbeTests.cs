using DiskMark.Core.Hardware;

namespace DiskMark.Core.Tests;

public class FileSystemProbeTests
{
    [Theory]
    [InlineData("ext4", FileSystemSupport.Supported)]
    [InlineData("xfs", FileSystemSupport.Supported)]
    [InlineData("NTFS", FileSystemSupport.Supported)]
    [InlineData("apfs", FileSystemSupport.Supported)]
    [InlineData("tmpfs", FileSystemSupport.Caching)]
    [InlineData("zfs", FileSystemSupport.Caching)]
    [InlineData("overlay", FileSystemSupport.Caching)]
    [InlineData("fuseblk", FileSystemSupport.Caching)]
    [InlineData("fuse.portal", FileSystemSupport.Caching)]
    [InlineData("nfs4", FileSystemSupport.Network)]
    [InlineData("fuse.sshfs", FileSystemSupport.Network)]
    [InlineData("proc", FileSystemSupport.Unsupported)]
    [InlineData("squashfs", FileSystemSupport.Unsupported)]
    public void Classify_MapsFileSystemTypes(string type, FileSystemSupport expected)
    {
        Assert.Equal(expected, FileSystemProbe.Classify(type));
    }

    [Fact]
    public void Classify_NetworkDriveType_IsNetwork()
    {
        Assert.Equal(FileSystemSupport.Network, FileSystemProbe.Classify("NTFS", DriveType.Network));
    }

    [Fact]
    public void ParseMountInfoLine_ReadsMountPointTypeAndSource()
    {
        const string line = "29 1 8:2 / /mnt/my\\040disk rw,relatime shared:1 - btrfs /dev/sda2 rw,compress=zstd:3,space_cache=v2";

        Assert.True(FileSystemProbe.TryParseMountInfoLine(line, out var mount));
        Assert.Equal("/mnt/my disk", mount.MountPoint);
        Assert.Equal("btrfs", mount.FileSystemType);
        Assert.Equal("/dev/sda2", mount.Source);
        Assert.Contains("compress", mount.SuperOptions);
    }

    [Fact]
    public void ParseMountInfoLine_RejectsMalformedLines()
    {
        Assert.False(FileSystemProbe.TryParseMountInfoLine("garbage", out _));
    }

    [Fact]
    public void Probe_MissingDirectory_ThrowsTargetError()
    {
        var error = Assert.Throws<Common.BenchmarkException>(() => FileSystemProbe.Probe(Path.Combine(TestDirectory.Get(), Guid.NewGuid().ToString("N"))));
        Assert.Equal(Common.BenchmarkErrorKind.Target, error.Kind);
    }
}
