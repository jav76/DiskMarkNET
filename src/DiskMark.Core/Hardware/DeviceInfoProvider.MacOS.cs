using System.Diagnostics;
using System.Globalization;
using System.Xml;

namespace DiskMark.Core.Hardware;

public static partial class DeviceInfoProvider
{
    private static StorageDeviceInfo? GetMacOS(string mountPoint)
    {
        var info = DiskUtilInfo(mountPoint);
        if (info is null)
            return null;

        // APFS volumes live in a synthesized container; follow the physical store to the whole disk.
        for (int hop = 0; hop < 4; hop++)
        {
            Dictionary<string, string>? next = null;
            if (info.TryGetValue("APFSPhysicalStore", out var store))
                next = DiskUtilInfo(store);
            else if (info.TryGetValue("ParentWholeDisk", out var parent) &&
                     info.TryGetValue("DeviceIdentifier", out var self) && parent != self)
                next = DiskUtilInfo(parent);

            if (next is null)
                break;
            info = next;
        }

        return new StorageDeviceInfo(
            Get(info, "MediaName"),
            null,
            Get(info, "BusProtocol"),
            info.TryGetValue("SolidState", out var ssd) ? ssd == "true" : null,
            (int?)GetLong(info, "DeviceBlockSize"),
            null,
            GetLong(info, "TotalSize") ?? GetLong(info, "Size"),
            Get(info, "DeviceNode"));
    }

    private static Dictionary<string, string>? DiskUtilInfo(string target)
    {
        var start = new ProcessStartInfo("/usr/sbin/diskutil")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("info");
        start.ArgumentList.Add("-plist");
        start.ArgumentList.Add(target);

        using var process = Process.Start(start);
        if (process is null)
            return null;

        var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(5000) || process.ExitCode != 0)
            return null;

        return ParsePlist(output.GetAwaiter().GetResult());
    }

    /// <summary>
    /// Flattens a plist into key/value strings. Nested values are included, so the first APFSPhysicalStore
    /// inside the APFSPhysicalStores array becomes available as a top-level key.
    /// </summary>
    internal static Dictionary<string, string> ParsePlist(string xml)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(xml), settings);

        string? key = null;
        reader.Read();
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                // ReadElementContentAsString already advances past the element.
                switch (reader.Name)
                {
                    case "key":
                        key = reader.ReadElementContentAsString();
                        continue;
                    case "string" or "integer" or "real":
                        var text = reader.ReadElementContentAsString();
                        if (key is not null)
                            values.TryAdd(key, text);
                        key = null;
                        continue;
                    case "true" or "false":
                        if (key is not null)
                            values.TryAdd(key, reader.Name);
                        key = null;
                        break;
                }
            }

            reader.Read();
        }

        return values;
    }

    private static string? Get(Dictionary<string, string> info, string key) =>
        info.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static long? GetLong(Dictionary<string, string> info, string key) =>
        info.TryGetValue(key, out var value) && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
}
