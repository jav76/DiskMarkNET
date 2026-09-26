using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using DiskMark.Core.Interop;
using Microsoft.Win32.SafeHandles;

namespace DiskMark.Core.Hardware;

public static partial class DeviceInfoProvider
{
    [SupportedOSPlatform("windows")]
    private static unsafe StorageDeviceInfo? GetWindows(string directory)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(directory));
        if (string.IsNullOrEmpty(root) || root.Length < 2 || root[1] != ':')
            return null;

        int? diskNumber;
        using (var volume = OpenDevice($@"\\.\{root[0]}:"))
        {
            if (volume is null)
                return null;

            // VOLUME_DISK_EXTENTS: DWORD count, 4 bytes padding, then DISK_EXTENT { DWORD DiskNumber; ... }.
            byte* extents = stackalloc byte[256];
            diskNumber = Kernel32.DeviceIoControl(volume, Kernel32.IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, null, 0, extents, 256, out _, 0)
                && BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(extents, 256)) > 0
                    ? (int)BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(extents + 8, 4))
                    : null;
        }

        if (diskNumber is null)
            return null;

        string devicePath = $@"\\.\PhysicalDrive{diskNumber}";
        using var disk = OpenDevice(devicePath);
        if (disk is null)
            return null;

        string? model = null;
        string? firmware = null;
        string? bus = null;
        var descriptor = QueryProperty(disk, Kernel32.StorageDeviceProperty, 1024);
        if (descriptor is not null && descriptor.Length >= 32)
        {
            string? vendor = ReadAnsi(descriptor, BinaryPrimitives.ReadInt32LittleEndian(descriptor.AsSpan(12)));
            string? product = ReadAnsi(descriptor, BinaryPrimitives.ReadInt32LittleEndian(descriptor.AsSpan(16)));
            firmware = ReadAnsi(descriptor, BinaryPrimitives.ReadInt32LittleEndian(descriptor.AsSpan(20)));
            model = string.Join(' ', new[] { vendor, product }.Where(s => !string.IsNullOrWhiteSpace(s)));
            bus = WindowsBusType(BinaryPrimitives.ReadInt32LittleEndian(descriptor.AsSpan(28)));
        }

        int? logical = null;
        int? physical = null;
        var alignment = QueryProperty(disk, Kernel32.StorageAccessAlignmentProperty, 64);
        if (alignment is not null && alignment.Length >= 24)
        {
            logical = BinaryPrimitives.ReadInt32LittleEndian(alignment.AsSpan(16));
            physical = BinaryPrimitives.ReadInt32LittleEndian(alignment.AsSpan(20));
        }

        bool? solidState = null;
        var seekPenalty = QueryProperty(disk, Kernel32.StorageDeviceSeekPenaltyProperty, 16);
        if (seekPenalty is not null && seekPenalty.Length >= 9)
            solidState = seekPenalty[8] == 0;

        long? capacity = null;
        try
        {
            capacity = new DriveInfo(root).TotalSize;
        }
        catch (IOException)
        {
        }

        return new StorageDeviceInfo(
            string.IsNullOrWhiteSpace(model) ? null : model,
            firmware,
            bus,
            solidState,
            logical,
            physical,
            capacity,
            devicePath);
    }

    [SupportedOSPlatform("windows")]
    private static SafeFileHandle? OpenDevice(string path)
    {
        // Zero desired access allows metadata IOCTLs without administrator rights.
        var handle = Kernel32.CreateFile(path, 0, Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE, 0, Kernel32.OPEN_EXISTING, 0, 0);
        if (!handle.IsInvalid)
            return handle;

        handle.Dispose();
        return null;
    }

    [SupportedOSPlatform("windows")]
    private static unsafe byte[]? QueryProperty(SafeFileHandle device, int propertyId, int outputSize)
    {
        // STORAGE_PROPERTY_QUERY { PropertyId; QueryType = PropertyStandardQuery; AdditionalParameters[1] }.
        byte* query = stackalloc byte[12];
        new Span<byte>(query, 12).Clear();
        BinaryPrimitives.WriteInt32LittleEndian(new Span<byte>(query, 4), propertyId);

        var output = new byte[outputSize];
        fixed (byte* outputPointer = output)
        {
            if (!Kernel32.DeviceIoControl(device, Kernel32.IOCTL_STORAGE_QUERY_PROPERTY, query, 12, outputPointer, (uint)outputSize, out uint returned, 0))
                return null;
            return output.AsSpan(0, (int)Math.Min(returned, (uint)outputSize)).ToArray();
        }
    }

    private static string? ReadAnsi(byte[] buffer, int offset)
    {
        if (offset <= 0 || offset >= buffer.Length)
            return null;
        int end = Array.IndexOf(buffer, (byte)0, offset);
        var value = Encoding.ASCII.GetString(buffer, offset, (end < 0 ? buffer.Length : end) - offset).Trim();
        return value.Length == 0 ? null : value;
    }

    private static string? WindowsBusType(int busType) => busType switch
    {
        0x01 => "SCSI",
        0x03 => "ATA",
        0x07 => "USB",
        0x08 => "RAID",
        0x09 => "iSCSI",
        0x0A => "SAS",
        0x0B => "SATA",
        0x0C => "SD",
        0x0D => "MMC",
        0x0E or 0x0F => "Virtual",
        0x10 => "Storage Spaces",
        0x11 => "NVMe",
        0x12 => "SCM",
        0x13 => "UFS",
        _ => null,
    };
}
