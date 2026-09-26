namespace DiskMark.Core.Hardware;

/// <summary>Best-effort lookup of the physical device behind a filesystem. Never throws; returns null when unknown.</summary>
public static partial class DeviceInfoProvider
{
    public static StorageDeviceInfo? TryGet(FileSystemDetails fileSystem, string directory)
    {
        try
        {
            if (OperatingSystem.IsLinux())
                return GetLinux(fileSystem);
            if (OperatingSystem.IsWindows())
                return GetWindows(directory);
            if (OperatingSystem.IsMacOS())
                return GetMacOS(fileSystem.MountPoint ?? directory);
        }
        catch (Exception)
        {
            // Hardware details are informational; a failed lookup must not stop a benchmark.
        }

        return null;
    }
}
