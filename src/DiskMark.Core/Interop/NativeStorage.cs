using System.Runtime.InteropServices;
using DiskMark.Core.Common;
using Microsoft.Win32.SafeHandles;

namespace DiskMark.Core.Interop;

/// <summary>Opens files for unbuffered direct I/O on each platform.</summary>
public static class NativeStorage
{
    /// <summary>FILE_FLAG_NO_BUFFERING. .NET passes this undocumented FileOptions value through to CreateFileW.</summary>
    public const FileOptions NoBuffering = (FileOptions)0x20000000;

    /// <summary>
    /// Options for <see cref="File.OpenHandle"/>. On Windows, the handle is overlapped (true async I/O through IOCP),
    /// unbuffered, and deleted by the OS when closed, even if the process is killed.
    /// </summary>
    public static FileOptions GetOpenOptions(bool writeThrough)
    {
        var options = FileOptions.None;
        if (OperatingSystem.IsWindows())
            options |= FileOptions.Asynchronous | NoBuffering | FileOptions.DeleteOnClose;
        if (writeThrough)
            options |= FileOptions.WriteThrough;
        return options;
    }

    /// <summary>
    /// Turns off OS caching for an open handle: O_DIRECT via F_SETFL on Linux, F_NOCACHE on macOS.
    /// Windows handles are already unbuffered from <see cref="GetOpenOptions"/>.
    /// </summary>
    public static void EnableDirectIo(SafeFileHandle handle)
    {
        if (OperatingSystem.IsWindows())
            return;

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Direct I/O is only implemented for Windows, Linux, and macOS.");

        bool added = false;
        try
        {
            handle.DangerousAddRef(ref added);
            int fd = (int)handle.DangerousGetHandle();

            if (OperatingSystem.IsLinux())
            {
                int flags = Libc.Fcntl(fd, Libc.F_GETFL, 0);
                if (flags < 0)
                    throw ErrnoException("F_GETFL");

                if (Libc.Fcntl(fd, Libc.F_SETFL, flags | LinuxODirect) < 0)
                {
                    int errno = Marshal.GetLastPInvokeError();
                    if (errno == Libc.EINVAL)
                    {
                        throw new BenchmarkException(
                            BenchmarkErrorKind.DirectIoUnsupported,
                            "This filesystem does not support direct I/O (O_DIRECT). Choose a directory on a local disk.");
                    }

                    throw ErrnoException("F_SETFL O_DIRECT", errno);
                }
            }
            else if (Libc.Fcntl(fd, Libc.F_NOCACHE, 1) < 0)
            {
                throw ErrnoException("F_NOCACHE");
            }
        }
        finally
        {
            if (added)
                handle.DangerousRelease();
        }
    }

    /// <summary>O_DIRECT differs by architecture. On arm64, 0x4000 is O_DIRECTORY.</summary>
    public static int LinuxODirect => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 or Architecture.X86 or Architecture.RiscV64 or Architecture.LoongArch64 or Architecture.S390x => 0x4000,
        Architecture.Arm64 or Architecture.Arm or Architecture.Armv6 => 0x10000,
        Architecture.Ppc64le => 0x20000,
        var arch => throw new PlatformNotSupportedException($"The O_DIRECT flag value is unknown for {arch}."),
    };

    private static IOException ErrnoException(string operation, int? errno = null)
    {
        int code = errno ?? Marshal.GetLastPInvokeError();
        return new IOException($"{operation} failed: {Marshal.GetPInvokeErrorMessage(code)} (errno {code})", code);
    }
}
