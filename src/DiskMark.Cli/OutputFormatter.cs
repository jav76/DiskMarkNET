using System.Globalization;
using System.Text;
using System.Text.Json;
using DiskMark.Core.Common;

namespace DiskMark.Cli;

/// <summary>CrystalDiskMark-style tables for the terminal, plus JSON output.</summary>
internal static class OutputFormatter
{
    public static string ToJson(RunResult result) => JsonSerializer.Serialize(result, DiskMarkJsonContext.Default.RunResult);

    public static string FormatResults(RunResult result, Ansi ansi)
    {
        var sb = new StringBuilder();
        string us = ConsoleSupport.MicroSymbol;
        var env = result.Environment;
        var config = result.Configuration;

        sb.AppendLine(ansi.Bold($"DiskMarkNET {env.AppVersion}") + ansi.Dim($"  |  {env.OperatingSystem} ({env.Architecture})  |  engine: {config.Engine}"));
        sb.AppendLine($"Target  : {env.TargetDirectory}{FormatFileSystem(env)}");
        if (env.Device is { } device)
            sb.AppendLine($"Device  : {FormatDevice(device)}");
        sb.AppendLine(
            $"Profile : {config.Profile}  |  {ByteSize.Format(config.TestFileSize)}  |  {config.Passes} x {Seconds(config.MeasureSeconds)}" +
            $"  |  interval {Seconds(config.IntervalSeconds)}  |  {config.DataPattern.ToString().ToLowerInvariant()} data" +
            (config.WriteThrough ? "  |  write-through" : string.Empty));
        sb.AppendLine();

        var specs = result.Tests.Select(t => t.Spec).Distinct().ToList();
        string header = $"{"Test",-15}{"Read MB/s",12}{"Write MB/s",12}{"Read IOPS",13}{"Write IOPS",13}{"Read " + us,12}{"Write " + us,12}";
        sb.AppendLine(ansi.Bold(header));
        foreach (var spec in specs)
        {
            var read = result.Find(spec, IoMode.Read);
            var write = result.Find(spec, IoMode.Write);
            sb.Append(ansi.Cyan($"{spec.Name,-15}"));
            sb.Append(ansi.Green(Cell(read?.MaxMegabytesPerSecond, 12)));
            sb.Append(ansi.Yellow(Cell(write?.MaxMegabytesPerSecond, 12)));
            sb.Append(ansi.Green(Cell(read?.MaxIops, 13)));
            sb.Append(ansi.Yellow(Cell(write?.MaxIops, 13)));
            sb.Append(ansi.Green(Cell(read?.Latency.MeanUs, 12)));
            sb.AppendLine(ansi.Yellow(Cell(write?.Latency.MeanUs, 12)));
        }

        sb.AppendLine();
        sb.AppendLine(ansi.Bold($"{"Latency (" + us + ")",-21}{"p50",11}{"p90",11}{"p95",11}{"p99",11}{"p99.9",11}{"max",11}"));
        foreach (var test in result.Tests)
        {
            var l = test.Latency;
            var colored = test.Mode == IoMode.Read ? (Func<string, string>)ansi.Green : ansi.Yellow;
            sb.Append(ansi.Cyan($"{test.Spec.Name,-15}"));
            sb.Append(colored($"{test.Mode,-6}"));
            sb.AppendLine(Cell(l.P50Us, 11) + Cell(l.P90Us, 11) + Cell(l.P95Us, 11) + Cell(l.P99Us, 11) + Cell(l.P999Us, 11) + Cell(l.MaxUs, 11));
        }

        sb.AppendLine();
        sb.AppendLine(ansi.Dim($"MB/s = 1,000,000 bytes/s. Scores are the best of {config.Passes} pass(es); latency covers all passes."));

        if (result.Warnings.Count > 0)
        {
            sb.AppendLine();
            foreach (var warning in result.Warnings)
                sb.AppendLine(ansi.Yellow($"warning: {warning}"));
        }

        if (!result.Completed)
        {
            sb.AppendLine();
            sb.AppendLine(ansi.Red($"Incomplete run: {result.Error ?? "stopped early"}"));
        }

        return sb.ToString();
    }

    public static string FormatDevice(Core.Hardware.StorageDeviceInfo device)
    {
        var parts = new List<string>();
        if (device.Model is not null)
            parts.Add(device.Model);
        var details = new List<string>();
        if (device.BusType is not null)
            details.Add(device.BusType);
        if (device.IsSolidState is { } ssd)
            details.Add(ssd ? "SSD" : "HDD");
        if (device.CapacityBytes is { } capacity)
            details.Add(ByteSize.Format(capacity));
        if (device.DevicePath is not null)
            details.Add(device.DevicePath);
        if (details.Count > 0)
            parts.Add($"({string.Join(", ", details)})");
        return parts.Count == 0 ? "unknown" : string.Join(' ', parts);
    }

    private static string FormatFileSystem(RunEnvironment env) => env.FileSystem is { } fs
        ? $" ({fs.Type}{(fs.Source is null ? string.Empty : $" on {fs.Source}")}, {ByteSize.Format(fs.AvailableBytes)} free)"
        : string.Empty;

    private static string Cell(double? value, int width) =>
        (value is null ? "-" : value.Value.ToString("F2", CultureInfo.InvariantCulture)).PadLeft(width);

    private static string Seconds(double seconds) => seconds.ToString("0.###", CultureInfo.InvariantCulture) + " s";
}
