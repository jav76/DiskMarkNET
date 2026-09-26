using Microsoft.Win32.SafeHandles;

namespace DiskMark.Core.Interop;

/// <summary>
/// Reports how much of a file sits in the OS page cache (Linux) or unified buffer cache (macOS), to verify that
/// direct I/O bypassed it. Uses mmap + mincore, which only inspects residency and does not read the file.
/// </summary>
internal static unsafe class PageCache
{
    /// <returns>Fraction of pages resident (0 to 1), or null when it cannot be determined.</returns>
    public static double? GetResidentFraction(SafeFileHandle handle, long length)
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) || length <= 0)
            return null;

        bool added = false;
        nint address = -1;
        try
        {
            handle.DangerousAddRef(ref added);
            address = Libc.Mmap(0, (nuint)length, Libc.PROT_READ, Libc.MAP_SHARED, (int)handle.DangerousGetHandle(), 0);
            if (address == -1)
                return null;

            long pageSize = Environment.SystemPageSize;
            long pages = (length + pageSize - 1) / pageSize;
            var vector = new byte[pages];
            fixed (byte* v = vector)
            {
                if (Libc.Mincore(address, (nuint)length, v) != 0)
                    return null;
            }

            long resident = 0;
            foreach (byte b in vector)
                resident += b & 1;
            return resident / (double)pages;
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }
        finally
        {
            if (address != -1)
                Libc.Munmap(address, (nuint)length);
            if (added)
                handle.DangerousRelease();
        }
    }
}
