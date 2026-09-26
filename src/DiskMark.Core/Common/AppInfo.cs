using System.Reflection;
using System.Runtime.InteropServices;

namespace DiskMark.Core.Common;

public static class AppInfo
{
    public static string Version { get; } = ResolveVersion();

    public static string Platform { get; } =
        $"{RuntimeInformation.OSDescription.Trim()} ({RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()})";

    private static string ResolveVersion()
    {
        var version = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        int plus = version.IndexOf('+');
        return plus >= 0 && version.Length > plus + 8 ? version[..(plus + 8)] : version;
    }
}
