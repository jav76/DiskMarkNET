using DiskMark.Core.Common;

namespace DiskMark.Cli.Tests;

public class CommandLineParserTests
{
    [Fact]
    public void Run_WithTargetOnly_UsesCrystalDiskMarkDefaults()
    {
        var options = Parse("run", "/data");

        Assert.Equal(CliCommand.Run, options.Command);
        Assert.Equal("/data", options.TargetDirectory);
        Assert.Same(BenchmarkProfile.Default, options.Profile);
        Assert.Equal(ByteSize.GiB, options.TestFileSize);
        Assert.Equal(5, options.Passes);
        Assert.Equal(TimeSpan.FromSeconds(5), options.MeasureDuration);
        Assert.Equal(TimeSpan.FromSeconds(5), options.IntervalDuration);
        Assert.Equal(DataPattern.Random, options.DataPattern);
        Assert.False(options.WriteThrough);
        Assert.False(options.Json);
    }

    [Fact]
    public void Run_ParsesAllOptions_InBothSyntaxes()
    {
        var options = Parse(
            "run", ".", "--profile", "nvme", "--size=64MiB", "-n", "3", "--duration", "500ms", "--interval=0s",
            "--warmup", "1s", "--mode", "read", "--data", "zeros", "--write-through", "--engine", "threaded", "--no-color");

        Assert.Same(BenchmarkProfile.Nvme, options.Profile);
        Assert.Equal(64 * ByteSize.MiB, options.TestFileSize);
        Assert.Equal(3, options.Passes);
        Assert.Equal(TimeSpan.FromMilliseconds(500), options.MeasureDuration);
        Assert.Equal(TimeSpan.Zero, options.IntervalDuration);
        Assert.Equal(TimeSpan.FromSeconds(1), options.WarmupDuration);
        Assert.True(options.IncludeReads);
        Assert.False(options.IncludeWrites);
        Assert.Equal(DataPattern.Zeros, options.DataPattern);
        Assert.True(options.WriteThrough);
        Assert.Equal("threaded", options.Engine);
        Assert.True(options.NoColor);
    }

    [Theory]
    [InlineData(new[] { "run", ".", "--json" }, null)]
    [InlineData(new[] { "run", ".", "--json=-" }, null)]
    [InlineData(new[] { "run", ".", "--json=out.json" }, "out.json")]
    [InlineData(new[] { "run", "--json", "." }, null)]
    public void Json_WritesToStdoutUnlessPathGiven(string[] args, string? expectedPath)
    {
        var options = Parse(args);
        Assert.True(options.Json);
        Assert.Equal(expectedPath, options.JsonPath);
        Assert.Equal(".", options.TargetDirectory);
    }

    [Theory]
    [InlineData(new string[0], "Help")]
    [InlineData(new[] { "--help" }, "Help")]
    [InlineData(new[] { "run", ".", "-h" }, "Help")]
    [InlineData(new[] { "--version" }, "Version")]
    [InlineData(new[] { "profiles" }, "Profiles")]
    [InlineData(new[] { "targets" }, "Targets")]
    public void Commands_AreRecognized(string[] args, string expected)
    {
        Assert.Equal(Enum.Parse<CliCommand>(expected), Parse(args).Command);
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("run")]
    [InlineData("run", ".", "extra")]
    [InlineData("run", ".", "--size", "3")]
    [InlineData("run", ".", "--size", "1500K")]
    [InlineData("run", ".", "--passes", "0")]
    [InlineData("run", ".", "--duration", "0s")]
    [InlineData("run", ".", "--duration", "soon")]
    [InlineData("run", ".", "--profile", "turbo")]
    [InlineData("run", ".", "--mode", "sideways")]
    [InlineData("run", ".", "--engine", "iouring")]
    [InlineData("run", ".", "--frobnicate")]
    [InlineData("run", ".", "--size")]
    public void InvalidInput_ReturnsError(params string[] args)
    {
        var result = CommandLineParser.Parse(args);
        Assert.Null(result.Options);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Theory]
    [InlineData("5s", 5)]
    [InlineData("500ms", 0.5)]
    [InlineData("1m", 60)]
    [InlineData("2.5", 2.5)]
    [InlineData("0s", 0)]
    public void TryParseDuration_AcceptsUnits(string text, double seconds)
    {
        Assert.True(CommandLineParser.TryParseDuration(text, out var duration));
        Assert.Equal(seconds, duration.TotalSeconds, 6);
    }

    private static CommandLineOptions Parse(params string[] args)
    {
        var result = CommandLineParser.Parse(args);
        Assert.True(result.Options is not null, result.Error);
        return result.Options;
    }
}
