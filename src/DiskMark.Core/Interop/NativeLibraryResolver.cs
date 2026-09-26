using System.Reflection;
using System.Runtime.InteropServices;

namespace DiskMark.Core.Interop;

/// <summary>
/// Maps the logical "libc" import to the platform's real C library. On glibc, "libc.so" is a linker script
/// rather than a loadable library, so probing by name alone is unreliable.
/// </summary>
internal static class NativeLibraryResolver
{
    public const string LibC = "libc";

    private static int s_registered;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref s_registered, 1) == 1)
            return;

        try
        {
            NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);
        }
        catch (InvalidOperationException)
        {
            // A resolver was already registered for this assembly.
        }
    }

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibC)
            return 0;

        string[] candidates = OperatingSystem.IsMacOS()
            ? ["/usr/lib/libSystem.B.dylib"]
            : ["libc.so.6", "libc.so"];

        foreach (var candidate in candidates)
        {
            if (NativeLibrary.TryLoad(candidate, out var handle))
                return handle;
        }

        return 0;
    }
}
