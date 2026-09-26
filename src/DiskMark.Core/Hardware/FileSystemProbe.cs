using System.Text;
using DiskMark.Core.Common;
using DiskMark.Core.Interop;

namespace DiskMark.Core.Hardware;

/// <summary>Identifies the filesystem behind a directory and whether direct I/O results on it can be trusted.</summary>
public static class FileSystemProbe
{
    private static readonly HashSet<string> s_unsupported = new(StringComparer.OrdinalIgnoreCase)
    {
        "proc", "sysfs", "devtmpfs", "devpts", "devfs", "cgroup", "cgroup2", "securityfs", "debugfs", "tracefs",
        "configfs", "pstore", "bpf", "mqueue", "hugetlbfs", "autofs", "efivarfs", "binfmt_misc", "fusectl", "nsfs",
        "rpc_pipefs", "squashfs", "iso9660", "cd9660",
    };

    private static readonly HashSet<string> s_caching = new(StringComparer.OrdinalIgnoreCase)
    {
        "tmpfs", "ramfs", "zfs", "overlay", "overlayfs", "aufs", "fuseblk", "9p", "virtiofs",
    };

    private static readonly HashSet<string> s_network = new(StringComparer.OrdinalIgnoreCase)
    {
        "nfs", "nfs4", "cifs", "smb3", "smbfs", "afpfs", "webdav", "ceph", "fuse.sshfs", "fuse.glusterfs", "fuse.rclone",
    };

    public static FileSystemDetails Probe(string directory)
    {
        string full = Path.GetFullPath(directory);
        if (!Directory.Exists(full))
            throw new BenchmarkException(BenchmarkErrorKind.Target, $"Directory not found: {full}");

        string type;
        string? mountPoint = null;
        string? source = null;
        bool compressed = false;
        DriveType driveType = DriveType.Unknown;

        if (OperatingSystem.IsLinux() && TryFindLinuxMount(full, out var mount))
        {
            type = mount.FileSystemType;
            mountPoint = mount.MountPoint;
            source = mount.Source;
            compressed = mount.SuperOptions.Contains("compress", StringComparison.Ordinal);
        }
        else
        {
            var drive = new DriveInfo(OperatingSystem.IsWindows() ? Path.GetPathRoot(full)! : full);
            type = SafeGet(() => drive.DriveFormat) ?? "unknown";
            driveType = SafeGet(() => (DriveType?)drive.DriveType) ?? DriveType.Unknown;
            mountPoint = OperatingSystem.IsWindows() ? drive.Name : FindUnixMountPoint(full);
            if (OperatingSystem.IsWindows())
                compressed = (File.GetAttributes(full) & FileAttributes.Compressed) != 0;
        }

        var space = new DriveInfo(OperatingSystem.IsWindows() ? Path.GetPathRoot(full)! : full);
        long total = SafeGet(() => (long?)space.TotalSize) ?? 0;
        long available = SafeGet(() => (long?)space.AvailableFreeSpace) ?? 0;

        var support = Classify(type, driveType);
        return new FileSystemDetails(type, mountPoint, source, support, compressed, total, available, Describe(type, support, compressed));
    }

    public static FileSystemSupport Classify(string type, DriveType driveType = DriveType.Unknown)
    {
        if (driveType == DriveType.Network || s_network.Contains(type))
            return FileSystemSupport.Network;
        if (s_unsupported.Contains(type) || driveType == DriveType.CDRom)
            return FileSystemSupport.Unsupported;
        if (s_caching.Contains(type) || type.StartsWith("fuse", StringComparison.OrdinalIgnoreCase) || driveType == DriveType.Ram)
            return FileSystemSupport.Caching;
        return FileSystemSupport.Supported;
    }

    private static string? Describe(string type, FileSystemSupport support, bool compressed)
    {
        string? note = support switch
        {
            FileSystemSupport.Caching when type.Equals("zfs", StringComparison.OrdinalIgnoreCase) =>
                "ZFS before OpenZFS 2.3 ignores O_DIRECT and serves I/O through the ARC cache; results may reflect RAM speed.",
            FileSystemSupport.Caching =>
                $"{type} may serve direct I/O from memory; results may not reflect the storage device.",
            FileSystemSupport.Network =>
                $"{type} is a network filesystem; results measure the network and the remote server.",
            FileSystemSupport.Unsupported =>
                $"{type} is a pseudo or read-only filesystem and cannot hold a test file.",
            _ => null,
        };

        if (compressed)
        {
            const string compressionNote = "Filesystem compression is enabled; direct I/O may fall back to buffered I/O.";
            note = note is null ? compressionNote : $"{note} {compressionNote}";
        }

        return note;
    }

    internal readonly record struct LinuxMount(string MountPoint, string FileSystemType, string Source, string SuperOptions);

    private static bool TryFindLinuxMount(string path, out LinuxMount mount)
    {
        mount = default;
        string resolved;
        string[] lines;
        try
        {
            resolved = Libc.RealPath(path) ?? path;
            lines = File.ReadAllLines("/proc/self/mountinfo");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }

        int bestLength = -1;
        foreach (var line in lines)
        {
            if (!TryParseMountInfoLine(line, out var candidate) || !IsUnder(resolved, candidate.MountPoint))
                continue;

            // Later entries shadow earlier ones on the same mount point.
            if (candidate.MountPoint.Length >= bestLength)
            {
                bestLength = candidate.MountPoint.Length;
                mount = candidate;
            }
        }

        return bestLength >= 0;
    }

    internal static bool TryParseMountInfoLine(string line, out LinuxMount mount)
    {
        mount = default;
        var parts = line.Split(' ');
        if (parts.Length < 10)
            return false;

        int separator = Array.IndexOf(parts, "-", 6);
        if (separator < 0 || separator + 3 >= parts.Length)
            return false;

        mount = new LinuxMount(
            UnescapeMountField(parts[4]),
            parts[separator + 1],
            UnescapeMountField(parts[separator + 2]),
            parts[separator + 3]);
        return true;
    }

    /// <summary>mountinfo escapes space, tab, newline, and backslash as three-digit octal sequences.</summary>
    internal static string UnescapeMountField(string value)
    {
        if (!value.Contains('\\'))
            return value;

        var builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && IsOctal(value, i + 1))
            {
                builder.Append((char)Convert.ToInt32(value.Substring(i + 1, 3), 8));
                i += 3;
            }
            else
            {
                builder.Append(value[i]);
            }
        }

        return builder.ToString();
    }

    private static bool IsOctal(string value, int start)
    {
        if (start + 3 > value.Length)
            return false;
        for (int i = start; i < start + 3; i++)
        {
            if (value[i] is < '0' or > '7')
                return false;
        }

        return true;
    }

    private static bool IsUnder(string path, string mountPoint) =>
        mountPoint == "/" ||
        path.Equals(mountPoint, StringComparison.Ordinal) ||
        (path.StartsWith(mountPoint, StringComparison.Ordinal) && path.Length > mountPoint.Length && path[mountPoint.Length] == '/');

    private static string? FindUnixMountPoint(string path)
    {
        string resolved = Libc.RealPath(path) ?? path;
        string? best = null;
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (IsUnder(resolved, drive.Name) && (best is null || drive.Name.Length > best.Length))
                best = drive.Name;
        }

        return best;
    }

    private static T? SafeGet<T>(Func<T?> getter)
    {
        try
        {
            return getter();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DriveNotFoundException)
        {
            return default;
        }
    }
}
