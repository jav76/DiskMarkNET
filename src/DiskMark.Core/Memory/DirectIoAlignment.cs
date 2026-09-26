using System.Buffers.Binary;
using DiskMark.Core.Interop;
using Microsoft.Win32.SafeHandles;

namespace DiskMark.Core.Memory;

/// <param name="Memory">Required buffer address alignment.</param>
/// <param name="Offset">Required file offset and transfer size alignment.</param>
public readonly record struct IoAlignment(int Memory, int Offset);

public static class DirectIoAlignment
{
    /// <summary>Safe for both 512-byte and 4 KiB logical sector devices.</summary>
    public const int FallbackOffsetAlignment = 4096;

    public static IoAlignment Resolve(SafeFileHandle handle, string directory)
    {
        int page = Environment.SystemPageSize;

        if (OperatingSystem.IsLinux() && TryGetLinuxDioAlignment(handle, out int memAlign, out int offsetAlign))
            return new IoAlignment(Math.Max(page, memAlign), Math.Max(offsetAlign, 512));

        if (OperatingSystem.IsWindows() && TryGetWindowsSectorSize(directory, out int sector))
            return new IoAlignment(Math.Max(page, sector), Math.Max(sector, 512));

        return new IoAlignment(page, FallbackOffsetAlignment);
    }

    private static unsafe bool TryGetLinuxDioAlignment(SafeFileHandle handle, out int memoryAlignment, out int offsetAlignment)
    {
        memoryAlignment = offsetAlignment = 0;
        bool added = false;
        try
        {
            handle.DangerousAddRef(ref added);
            // struct statx is 256 bytes; stx_dio_mem_align is at offset 152 and stx_dio_offset_align at 156 (Linux 6.1+).
            byte* buffer = stackalloc byte[256];
            new Span<byte>(buffer, 256).Clear();
            if (Libc.Statx((int)handle.DangerousGetHandle(), "", Libc.AT_EMPTY_PATH, Libc.STATX_DIOALIGN, buffer) != 0)
                return false;

            var span = new ReadOnlySpan<byte>(buffer, 256);
            uint mask = BinaryPrimitives.ReadUInt32LittleEndian(span);
            if ((mask & Libc.STATX_DIOALIGN) == 0)
                return false;

            memoryAlignment = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[152..]);
            offsetAlignment = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[156..]);
            return offsetAlignment > 0;
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
        finally
        {
            if (added)
                handle.DangerousRelease();
        }
    }

    private static bool TryGetWindowsSectorSize(string directory, out int bytesPerSector)
    {
        bytesPerSector = 0;
        var root = Path.GetPathRoot(Path.GetFullPath(directory));
        if (string.IsNullOrEmpty(root) || !Kernel32.GetDiskFreeSpace(root, out _, out uint sector, out _, out _))
            return false;

        bytesPerSector = (int)sector;
        return bytesPerSector > 0;
    }
}
