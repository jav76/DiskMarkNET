using System.Globalization;
using DiskMark.Core.Common;
using DiskMark.Core.Engine;

namespace DiskMark.Cli;

internal sealed record ParseResult(CommandLineOptions? Options, string? Error)
{
    public static ParseResult Ok(CommandLineOptions options) => new(options, null);

    public static ParseResult Fail(string error) => new(null, error);
}

/// <summary>Minimal handcrafted parser (no reflection, trim-safe). Accepts "--name value" and "--name=value".</summary>
internal static class CommandLineParser
{
    public static ParseResult Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return ParseResult.Ok(new CommandLineOptions { Command = CliCommand.Help });

        var options = new CommandLineOptions();
        int index = 0;
        string first = args[0];

        switch (first)
        {
            case "-h" or "--help" or "help":
                return ParseResult.Ok(options with { Command = CliCommand.Help });
            case "-v" or "--version" or "version":
                return ParseResult.Ok(options with { Command = CliCommand.Version });
            case "targets":
                options = options with { Command = CliCommand.Targets };
                index = 1;
                break;
            case "profiles":
                options = options with { Command = CliCommand.Profiles };
                index = 1;
                break;
            case "run":
                options = options with { Command = CliCommand.Run };
                index = 1;
                break;
            default:
                return ParseResult.Fail($"Unknown command '{first}'.");
        }

        for (; index < args.Count; index++)
        {
            string arg = args[index];
            if (!arg.StartsWith("-", StringComparison.Ordinal) || arg == "-")
            {
                if (options.Command != CliCommand.Run || options.TargetDirectory is not null)
                    return ParseResult.Fail($"Unexpected argument '{arg}'.");
                options = options with { TargetDirectory = arg };
                continue;
            }

            string name = arg;
            string? inlineValue = null;
            int equals = arg.IndexOf('=');
            if (equals > 0)
            {
                name = arg[..equals];
                inlineValue = arg[(equals + 1)..];
            }

            switch (name)
            {
                case "-h" or "--help":
                    return ParseResult.Ok(options with { Command = CliCommand.Help });
                case "--json":
                    options = options with { Json = true, JsonPath = string.IsNullOrEmpty(inlineValue) || inlineValue == "-" ? null : inlineValue };
                    continue;
                case "--no-color":
                    options = options with { NoColor = true };
                    continue;
                case "--write-through":
                    options = options with { WriteThrough = true };
                    continue;
            }

            if (!TryTakeValue(args, ref index, name, inlineValue, out var value, out var error))
                return ParseResult.Fail(error);

            switch (name)
            {
                case "-p" or "--profile":
                    var profile = BenchmarkProfile.Find(value);
                    if (profile is null)
                        return ParseResult.Fail($"Unknown profile '{value}'. Available: {string.Join(", ", BenchmarkProfile.All.Select(p => p.Name))}.");
                    options = options with { Profile = profile };
                    break;
                case "-s" or "--size":
                    if (!ByteSize.TryParse(value, out long size) || size < ByteSize.MiB || size % ByteSize.MiB != 0)
                        return ParseResult.Fail($"Invalid size '{value}'. Use a whole number of MiB or GiB, e.g. 64MiB or 1GiB.");
                    options = options with { TestFileSize = size };
                    break;
                case "-n" or "--passes":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int passes) || passes is < 1 or > 100)
                        return ParseResult.Fail($"Invalid pass count '{value}'. Use 1 to 100.");
                    options = options with { Passes = passes };
                    break;
                case "-d" or "--duration":
                    if (!TryParseDuration(value, out var duration) || duration <= TimeSpan.Zero)
                        return ParseResult.Fail($"Invalid duration '{value}'. Examples: 5s, 500ms, 1m.");
                    options = options with { MeasureDuration = duration };
                    break;
                case "-i" or "--interval":
                    if (!TryParseDuration(value, out var interval))
                        return ParseResult.Fail($"Invalid interval '{value}'. Examples: 5s, 0s.");
                    options = options with { IntervalDuration = interval };
                    break;
                case "--warmup":
                    if (!TryParseDuration(value, out var warmup))
                        return ParseResult.Fail($"Invalid warmup '{value}'. Examples: 1s, 0s.");
                    options = options with { WarmupDuration = warmup };
                    break;
                case "--data":
                    switch (value.ToLowerInvariant())
                    {
                        case "random":
                            options = options with { DataPattern = DataPattern.Random };
                            break;
                        case "zeros" or "zero":
                            options = options with { DataPattern = DataPattern.Zeros };
                            break;
                        default:
                            return ParseResult.Fail($"Invalid data pattern '{value}'. Use random or zeros.");
                    }

                    break;
                case "-m" or "--mode":
                    switch (value.ToLowerInvariant())
                    {
                        case "read":
                            options = options with { IncludeReads = true, IncludeWrites = false };
                            break;
                        case "write":
                            options = options with { IncludeReads = false, IncludeWrites = true };
                            break;
                        case "all" or "both":
                            options = options with { IncludeReads = true, IncludeWrites = true };
                            break;
                        default:
                            return ParseResult.Fail($"Invalid mode '{value}'. Use read, write, or all.");
                    }

                    break;
                case "--engine":
                    if (!IoEngineFactory.Names.Contains(value.ToLowerInvariant()))
                        return ParseResult.Fail($"Invalid engine '{value}'. Use {string.Join(", ", IoEngineFactory.Names)}.");
                    options = options with { Engine = value.ToLowerInvariant() };
                    break;
                default:
                    return ParseResult.Fail($"Unknown option '{name}'.");
            }
        }

        if (options.Command == CliCommand.Run && options.TargetDirectory is null)
            return ParseResult.Fail("Missing target directory. Example: diskmark run /path/to/dir");

        return ParseResult.Ok(options);
    }

    /// <summary>Parses "5s", "500ms", "1m", or a bare number of seconds.</summary>
    internal static bool TryParseDuration(string text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        text = text.Trim().ToLowerInvariant();
        (string number, double scale) = text switch
        {
            _ when text.EndsWith("ms", StringComparison.Ordinal) => (text[..^2], 0.001),
            _ when text.EndsWith('s') => (text[..^1], 1d),
            _ when text.EndsWith('m') => (text[..^1], 60d),
            _ => (text, 1d),
        };

        if (!double.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value) || value < 0)
            return false;

        double seconds = value * scale;
        if (seconds > TimeSpan.FromHours(1).TotalSeconds)
            return false;

        duration = TimeSpan.FromSeconds(seconds);
        return true;
    }

    private static bool TryTakeValue(IReadOnlyList<string> args, ref int index, string name, string? inlineValue, out string value, out string error)
    {
        error = string.Empty;
        if (inlineValue is not null)
        {
            value = inlineValue;
            return true;
        }

        if (index + 1 >= args.Count)
        {
            value = string.Empty;
            error = $"Option '{name}' requires a value.";
            return false;
        }

        value = args[++index];
        return true;
    }
}
