using System.Runtime.InteropServices;
using DiskMark.Cli;
using DiskMark.Core.Common;
using DiskMark.Core.Engine;
using DiskMark.Core.Hardware;

var parsed = CommandLineParser.Parse(args);
if (parsed.Options is not { } options)
{
    Console.Error.WriteLine($"error: {parsed.Error}");
    Console.Error.WriteLine("Run 'diskmark --help' for usage.");
    return ExitCodes.Usage;
}

return options.Command switch
{
    CliCommand.Help => PrintHelp(),
    CliCommand.Version => PrintVersion(),
    CliCommand.Profiles => PrintProfiles(),
    CliCommand.Targets => PrintTargets(),
    _ => RunBenchmark(options),
};

static int PrintHelp()
{
    Console.Out.Write(HelpText.Text);
    return ExitCodes.Success;
}

static int PrintVersion()
{
    Console.Out.WriteLine($"diskmark {AppInfo.Version}");
    return ExitCodes.Success;
}

static int PrintProfiles()
{
    foreach (var profile in BenchmarkProfile.All)
    {
        Console.Out.WriteLine($"{profile.Name,-10}{profile.Description}");
        foreach (var test in profile.Tests)
            Console.Out.WriteLine($"{string.Empty,-10}  {test.Name}");
    }

    return ExitCodes.Success;
}

static int PrintTargets()
{
    var targets = TargetEnumerator.GetTargets();
    if (targets.Count == 0)
    {
        Console.Error.WriteLine("No suitable storage targets found.");
        return ExitCodes.Target;
    }

    Console.Out.WriteLine($"{"Name",-28}{"Type",-10}{"Free",12}{"Total",12}  Default directory");
    foreach (var t in targets)
        Console.Out.WriteLine($"{t.DisplayName,-28}{t.FileSystemType,-10}{ByteSize.Format(t.AvailableBytes),12}{ByteSize.Format(t.TotalBytes),12}  {t.DefaultDirectory}");
    return ExitCodes.Success;
}

static int RunBenchmark(CommandLineOptions options)
{
    var stdoutAnsi = new Ansi(ConsoleSupport.StdoutSupportsColor(options.NoColor));
    var stderrAnsi = new Ansi(ConsoleSupport.StderrSupportsColor(options.NoColor));

    BenchmarkRunner runner;
    try
    {
        var benchmarkOptions = options.ToBenchmarkOptions();
        benchmarkOptions.Validate();
        runner = new BenchmarkRunner(benchmarkOptions, IoEngineFactory.Create(options.Engine));
    }
    catch (BenchmarkException e)
    {
        Console.Error.WriteLine($"error: {e.Message}");
        return ExitCodes.Usage;
    }

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        // First Ctrl+C stops gracefully and prints partial results; a second one terminates immediately.
        if (!cts.IsCancellationRequested)
        {
            e.Cancel = true;
            cts.Cancel();
        }
    };

    using var sigterm = TryRegisterSigterm(cts);
    using var progress = new ProgressReporter(runner, stderrAnsi, interactive: !Console.IsErrorRedirected);
    runner.EnvironmentResolved += env =>
    {
        string device = env.Device is { } d ? $"  |  {OutputFormatter.FormatDevice(d)}" : string.Empty;
        progress.WriteLine(stderrAnsi.Dim($"Target: {env.TargetDirectory} ({env.FileSystem?.Type}){device}  |  engine: {runner.EngineName}"));
        foreach (var note in env.FileSystem?.Note is { } n ? [n] : Array.Empty<string>())
            progress.WriteLine(stderrAnsi.Yellow($"warning: {note}"));
    };

    RunResult result;
    try
    {
        result = runner.Run(cts.Token);
    }
    catch (BenchmarkException e)
    {
        progress.Dispose();
        Console.Error.WriteLine(stderrAnsi.Red($"error: {e.Message}"));
        return e.Kind switch
        {
            BenchmarkErrorKind.InvalidOptions => ExitCodes.Usage,
            BenchmarkErrorKind.Io => ExitCodes.Io,
            _ => ExitCodes.Target,
        };
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
    {
        progress.Dispose();
        Console.Error.WriteLine(stderrAnsi.Red($"error: {e.Message}"));
        return ExitCodes.Io;
    }

    progress.Dispose();

    if (options.Json && options.JsonPath is null)
    {
        Console.Out.WriteLine(OutputFormatter.ToJson(result));
    }
    else
    {
        Console.Out.Write(OutputFormatter.FormatResults(result, stdoutAnsi));
        if (options.JsonPath is not null)
        {
            File.WriteAllText(options.JsonPath, OutputFormatter.ToJson(result));
            Console.Error.WriteLine($"Wrote JSON results to {Path.GetFullPath(options.JsonPath)}");
        }
    }

    return result.Cancelled ? ExitCodes.Cancelled : result.Completed ? ExitCodes.Success : ExitCodes.Io;
}

static PosixSignalRegistration? TryRegisterSigterm(CancellationTokenSource cts)
{
    try
    {
        return PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            cts.Cancel();
        });
    }
    catch (PlatformNotSupportedException)
    {
        return null;
    }
}
