using System.Globalization;
using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace DiskMark.Desktop.Controls;

internal static class ChartText
{
    public static FormattedText Create(AvaloniaObject owner, string text, IBrush brush, double size = 11) => new(
        text,
        CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        new Typeface(owner.GetValue(TextElement.FontFamilyProperty)),
        size,
        brush);

    public static string Microseconds(double us) => us switch
    {
        >= 1_000_000 => $"{us / 1_000_000:0.##} s",
        >= 1_000 => $"{us / 1_000:0.##} ms",
        >= 1 => $"{us:0.##} µs",
        _ => $"{us * 1000:0} ns",
    };
}
