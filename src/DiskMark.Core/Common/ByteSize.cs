using System.Globalization;

namespace DiskMark.Core.Common;

/// <summary>
/// Binary size parsing and formatting. Suffixes are binary (1 MB == 1 MiB), matching CrystalDiskMark test sizes.
/// Throughput is reported separately in decimal MB/s.
/// </summary>
public static class ByteSize
{
    public const long KiB = 1024;
    public const long MiB = KiB * 1024;
    public const long GiB = MiB * 1024;
    public const long TiB = GiB * 1024;

    public static bool TryParse(string? text, out long bytes)
    {
        bytes = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var span = text.Trim();
        int split = 0;
        while (split < span.Length && (char.IsDigit(span[split]) || span[split] == '.'))
            split++;

        if (split == 0 || !double.TryParse(span.AsSpan(0, split), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            return false;

        long multiplier = span[split..].Trim().ToUpperInvariant() switch
        {
            "" or "B" => 1,
            "K" or "KB" or "KIB" => KiB,
            "M" or "MB" or "MIB" => MiB,
            "G" or "GB" or "GIB" => GiB,
            "T" or "TB" or "TIB" => TiB,
            _ => -1,
        };
        if (multiplier < 0)
            return false;

        double result = value * multiplier;
        if (result is < 1 or > long.MaxValue || result != Math.Floor(result))
            return false;

        bytes = (long)result;
        return true;
    }

    public static string Format(long bytes)
    {
        (double value, string unit) = bytes switch
        {
            >= TiB => (bytes / (double)TiB, "TiB"),
            >= GiB => (bytes / (double)GiB, "GiB"),
            >= MiB => (bytes / (double)MiB, "MiB"),
            >= KiB => (bytes / (double)KiB, "KiB"),
            _ => (bytes, "B"),
        };
        return value == Math.Floor(value)
            ? string.Create(CultureInfo.InvariantCulture, $"{value:0} {unit}")
            : string.Create(CultureInfo.InvariantCulture, $"{value:0.##} {unit}");
    }

    /// <summary>Formats a block size the way CrystalDiskMark labels tests, e.g. 4K, 128K, 1M.</summary>
    public static string FormatCompact(long bytes) => bytes switch
    {
        _ when bytes >= MiB && bytes % MiB == 0 => string.Create(CultureInfo.InvariantCulture, $"{bytes / MiB}M"),
        _ when bytes >= KiB && bytes % KiB == 0 => string.Create(CultureInfo.InvariantCulture, $"{bytes / KiB}K"),
        _ => bytes.ToString(CultureInfo.InvariantCulture),
    };
}
