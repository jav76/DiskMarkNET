namespace DiskMark.Core.Hardware;

/// <summary>Lists storage locations that can hold a test file, hiding pseudo, RAM-backed, and container filesystems.</summary>
public static class TargetEnumerator
{
    private static readonly string[] s_hiddenUnixPrefixes =
        ["/proc", "/sys", "/dev", "/run", "/snap", "/var/lib/docker", "/var/snap", "/boot", "/private/var/vm"];

    public static IReadOnlyList<StorageTarget> GetTargets()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var targets = new List<StorageTarget>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || !IsCandidate(drive))
                    continue;

                string mount = drive.Name;
                string defaultDirectory = DefaultDirectoryFor(mount, home);
                string label = OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? $"{mount.TrimEnd('\\')} {drive.VolumeLabel}"
                    : DisplayNameFor(mount);

                targets.Add(new StorageTarget(label, mount, defaultDirectory, drive.DriveFormat, drive.TotalSize, drive.AvailableFreeSpace));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Mounts we cannot inspect (e.g. other users' container layers) are skipped.
            }
        }

        return targets
            .OrderByDescending(t => t.DefaultDirectory == home || IsUnder(home, t.DefaultDirectory))
            .ThenBy(t => t.MountPoint, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsCandidate(DriveInfo drive)
    {
        if (OperatingSystem.IsWindows())
            return drive.DriveType is DriveType.Fixed or DriveType.Removable or DriveType.Network;

        string mount = drive.Name;
        if (OperatingSystem.IsMacOS())
        {
            if (mount == "/")
                return false;
            if (mount.StartsWith("/System/Volumes/", StringComparison.Ordinal))
                return mount == "/System/Volumes/Data";
        }

        if (s_hiddenUnixPrefixes.Any(p => mount == p || mount.StartsWith(p + "/", StringComparison.Ordinal)))
            return false;

        var support = FileSystemProbe.Classify(drive.DriveFormat, drive.DriveType);
        return support is FileSystemSupport.Supported or FileSystemSupport.Network;
    }

    /// <summary>
    /// Mount roots such as "/" and "C:\" are usually not writable without elevation, so targets that hold the user's
    /// profile default to a writable directory on the same volume.
    /// </summary>
    private static string DefaultDirectoryFor(string mount, string home)
    {
        if (OperatingSystem.IsWindows())
        {
            string temp = Path.GetTempPath();
            return string.Equals(Path.GetPathRoot(temp), mount, StringComparison.OrdinalIgnoreCase) ? temp : mount;
        }

        if (OperatingSystem.IsMacOS() && mount == "/System/Volumes/Data")
            return home;

        return IsUnder(home, mount) && !IsCoveredByLongerMount(home, mount) ? home : mount;
    }

    private static string DisplayNameFor(string mount) => mount switch
    {
        "/" => "/ (root)",
        "/System/Volumes/Data" => "Macintosh HD (Data)",
        _ => mount,
    };

    private static bool IsCoveredByLongerMount(string path, string mount)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.Name.Length > mount.Length && IsUnder(path, drive.Name))
                return true;
        }

        return false;
    }

    private static bool IsUnder(string path, string root)
    {
        if (root == "/")
            return path.StartsWith('/');
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var trimmed = root.TrimEnd('/', '\\');
        return path.Equals(trimmed, comparison) ||
               (path.StartsWith(trimmed, comparison) && path.Length > trimmed.Length && path[trimmed.Length] is '/' or '\\');
    }
}
