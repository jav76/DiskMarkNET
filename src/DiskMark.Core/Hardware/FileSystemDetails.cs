namespace DiskMark.Core.Hardware;

public enum FileSystemSupport
{
    /// <summary>Direct I/O is expected to bypass the OS cache.</summary>
    Supported,

    /// <summary>The filesystem accepts direct I/O but may still serve data from memory (tmpfs, ZFS before 2.3, FUSE, overlayfs).</summary>
    Caching,

    /// <summary>Network filesystem: results measure the network and the remote server.</summary>
    Network,

    /// <summary>Pseudo or read-only filesystem that cannot hold a test file.</summary>
    Unsupported,
}

public sealed record FileSystemDetails(
    string Type,
    string? MountPoint,
    string? Source,
    FileSystemSupport Support,
    bool Compressed,
    long TotalBytes,
    long AvailableBytes,
    string? Note);

public sealed record StorageDeviceInfo(
    string? Model,
    string? Firmware,
    string? BusType,
    bool? IsSolidState,
    int? LogicalSectorSize,
    int? PhysicalSectorSize,
    long? CapacityBytes,
    string? DevicePath);

public sealed record StorageTarget(
    string DisplayName,
    string MountPoint,
    string DefaultDirectory,
    string FileSystemType,
    long TotalBytes,
    long AvailableBytes);
