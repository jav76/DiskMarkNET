using System.Runtime.InteropServices;

namespace DiskMark.Core.Interop;

internal static unsafe partial class Libc
{
    // Linux fcntl commands.
    public const int F_GETFL = 3;
    public const int F_SETFL = 4;

    // macOS fcntl command.
    public const int F_NOCACHE = 48;

    public const int AT_EMPTY_PATH = 0x1000;
    public const uint STATX_DIOALIGN = 0x2000;

    public const int PROT_READ = 1;
    public const int MAP_SHARED = 1;
    public const int EINVAL = 22;

    static Libc() => NativeLibraryResolver.EnsureRegistered();

    [LibraryImport(NativeLibraryResolver.LibC, EntryPoint = "fcntl", SetLastError = true)]
    private static partial int FcntlCore(int fd, int cmd, nint arg);

    [LibraryImport(NativeLibraryResolver.LibC, EntryPoint = "fcntl", SetLastError = true)]
    private static partial int FcntlAppleArm64(int fd, int cmd, nint pad2, nint pad3, nint pad4, nint pad5, nint pad6, nint pad7, nint arg);

    /// <summary>
    /// Calls the variadic fcntl. Apple arm64 passes variadic arguments on the stack instead of in registers, so a
    /// normal three-argument P/Invoke would leave <paramref name="arg"/> unread. Six padding arguments fill x2-x7
    /// so the real argument lands in the first stack slot, where the callee's va_arg reads it.
    /// </summary>
    public static int Fcntl(int fd, int cmd, nint arg) =>
        OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? FcntlAppleArm64(fd, cmd, 0, 0, 0, 0, 0, 0, arg)
            : FcntlCore(fd, cmd, arg);

    [LibraryImport(NativeLibraryResolver.LibC, EntryPoint = "statx", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Statx(int dirfd, string path, int flags, uint mask, byte* buffer);

    [LibraryImport(NativeLibraryResolver.LibC, EntryPoint = "realpath", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint RealPathCore(string path, nint resolved);

    [LibraryImport(NativeLibraryResolver.LibC, EntryPoint = "free")]
    private static partial void Free(nint pointer);

    [LibraryImport(NativeLibraryResolver.LibC, EntryPoint = "mmap", SetLastError = true)]
    public static partial nint Mmap(nint address, nuint length, int protection, int flags, int fd, long offset);

    [LibraryImport(NativeLibraryResolver.LibC, EntryPoint = "munmap", SetLastError = true)]
    public static partial int Munmap(nint address, nuint length);

    [LibraryImport(NativeLibraryResolver.LibC, EntryPoint = "mincore", SetLastError = true)]
    public static partial int Mincore(nint address, nuint length, byte* vector);

    public static string? RealPath(string path)
    {
        nint resolved = RealPathCore(path, 0);
        if (resolved == 0)
            return null;

        try
        {
            return Marshal.PtrToStringUTF8(resolved);
        }
        finally
        {
            Free(resolved);
        }
    }
}
