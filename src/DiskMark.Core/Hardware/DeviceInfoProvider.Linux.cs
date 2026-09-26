using System.Globalization;

namespace DiskMark.Core.Hardware;

public static partial class DeviceInfoProvider
{
    private static StorageDeviceInfo? GetLinux(FileSystemDetails fileSystem)
    {
        if (fileSystem.Source is null || !fileSystem.Source.StartsWith("/dev/", StringComparison.Ordinal))
            return null;

        string deviceName = Path.GetFileName(ResolveLink(fileSystem.Source));
        string classPath = $"/sys/class/block/{deviceName}";
        if (!Directory.Exists(classPath))
            return null;

        string disk = FindWholeDisk(ResolveLink(classPath), depth: 0);
        string diskName = Path.GetFileName(disk);

        string? model = ReadSys(disk, "device/model") ?? ReadSys(disk, "device/name");
        string? firmware = ReadSys(disk, "device/firmware_rev") ?? ReadSys(disk, "device/rev");
        string? rotational = ReadSys(disk, "queue/rotational");
        long? sectors = ParseLong(ReadSys(disk, "size"));

        return new StorageDeviceInfo(
            model,
            firmware,
            LinuxBusType(diskName, disk),
            rotational is null ? null : rotational == "0",
            (int?)ParseLong(ReadSys(disk, "queue/logical_block_size")),
            (int?)ParseLong(ReadSys(disk, "queue/physical_block_size")),
            sectors * 512,
            $"/dev/{diskName}");
    }

    /// <summary>Walks from a partition or device-mapper/md node down to the underlying whole disk.</summary>
    private static string FindWholeDisk(string sysPath, int depth)
    {
        if (depth > 4)
            return sysPath;

        if (File.Exists(Path.Combine(sysPath, "partition")))
            return Directory.GetParent(sysPath)?.FullName ?? sysPath;

        var slaves = Path.Combine(sysPath, "slaves");
        if (Directory.Exists(slaves))
        {
            var first = Directory.EnumerateFileSystemEntries(slaves).Order(StringComparer.Ordinal).FirstOrDefault();
            if (first is not null)
                return FindWholeDisk(ResolveLink(first), depth + 1);
        }

        return sysPath;
    }

    private static string? LinuxBusType(string diskName, string sysPath)
    {
        if (diskName.StartsWith("nvme", StringComparison.Ordinal))
            return "NVMe";
        if (sysPath.Contains("/usb", StringComparison.Ordinal))
            return "USB";
        if (diskName.StartsWith("mmcblk", StringComparison.Ordinal))
            return "SD/MMC";
        if (diskName.StartsWith("vd", StringComparison.Ordinal))
            return "VirtIO";
        if (diskName.StartsWith("xvd", StringComparison.Ordinal))
            return "Xen";
        if (diskName.StartsWith("md", StringComparison.Ordinal))
            return "MD RAID";
        if (diskName.StartsWith("sd", StringComparison.Ordinal))
            return "SATA/SCSI";
        return null;
    }

    private static string ResolveLink(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
        }
        catch (IOException)
        {
            return path;
        }
    }

    private static string? ReadSys(string directory, string relative)
    {
        try
        {
            var value = File.ReadAllText(Path.Combine(directory, relative)).Trim();
            return value.Length == 0 ? null : value;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long? ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
}
