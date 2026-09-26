using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DiskMark.Core.Common;
using DiskMark.Core.Hardware;

namespace DiskMark.Desktop.ViewModels;

public sealed record DriveRow(string Label, string Value);

/// <summary>Filesystem and device details for the selected target directory.</summary>
public sealed partial class DriveInfoViewModel : ObservableObject
{
    public ObservableCollection<DriveRow> Rows { get; } = [];

    /// <summary>Filesystem caveat (caching, network, compression) shown next to the target selector.</summary>
    [ObservableProperty]
    public partial string? Warning { get; set; }

    public void Show(string directory, FileSystemDetails fileSystem, StorageDeviceInfo? device, int? memoryAlignment = null, int? offsetAlignment = null)
    {
        Rows.Clear();
        Add("Directory", directory);
        Add("Filesystem", fileSystem.Type);
        Add("Mount point", fileSystem.MountPoint);
        Add("Source", fileSystem.Source);
        Add("Direct I/O", fileSystem.Support switch
        {
            FileSystemSupport.Supported => "Supported",
            FileSystemSupport.Caching => "Accepted, but may be cached",
            FileSystemSupport.Network => "Network filesystem",
            _ => "Not supported",
        });
        Add("Free / total", $"{ByteSize.Format(fileSystem.AvailableBytes)} / {ByteSize.Format(fileSystem.TotalBytes)}");

        if (device is not null)
        {
            Add("Device", device.Model);
            Add("Interface", device.BusType);
            Add("Media", device.IsSolidState switch { true => "Solid state", false => "Rotational", null => null });
            Add("Firmware", device.Firmware);
            Add("Sectors", device.LogicalSectorSize is { } logical
                ? $"{logical} B logical" + (device.PhysicalSectorSize is { } physical ? $", {physical} B physical" : string.Empty)
                : null);
            Add("Capacity", device.CapacityBytes is { } capacity ? ByteSize.Format(capacity) : null);
            Add("Device path", device.DevicePath);
        }
        else
        {
            Add("Device", "Unknown");
        }

        if (memoryAlignment is not null && offsetAlignment is not null)
            Add("I/O alignment", $"{memoryAlignment} B memory, {offsetAlignment} B offset");

        Warning = fileSystem.Note;
    }

    public void Show(RunEnvironment environment)
    {
        if (environment.FileSystem is { } fileSystem)
            Show(environment.TargetDirectory, fileSystem, environment.Device, environment.MemoryAlignment, environment.OffsetAlignment);
    }

    public void ShowError(string directory, string message)
    {
        Rows.Clear();
        Add("Directory", directory);
        Add("Error", message);
        Warning = message;
    }

    private void Add(string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            Rows.Add(new DriveRow(label, value));
    }
}
