using System.Text.Json;
using DiskMark.Core.Common;

namespace DiskMark.Core.Tests;

public class ResultSerializationTests
{
    [Fact]
    public void RunResult_RoundTripsThroughSourceGeneratedContext()
    {
        var result = FakeRunner.Create(FakeRunner.Options(passes: 2), new FakeIoEngine(100, 200)).Run(TestContext.Current.CancellationToken);

        string json = JsonSerializer.Serialize(result, DiskMarkJsonContext.Default.RunResult);
        var restored = JsonSerializer.Deserialize(json, DiskMarkJsonContext.Default.RunResult);

        Assert.NotNull(restored);
        Assert.Equal(RunResult.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.Equal(result.Tests.Count, restored.Tests.Count);
        Assert.Equal(result.Tests[0].MaxMegabytesPerSecond, restored.Tests[0].MaxMegabytesPerSecond);
        Assert.Equal(result.Tests[0].Spec, restored.Tests[0].Spec);
        Assert.Equal(result.Environment.Device, restored.Environment.Device);
        Assert.Equal(result.Configuration, restored.Configuration);
    }

    [Fact]
    public void Json_UsesCamelCaseAndStringEnums()
    {
        var result = FakeRunner.Create(FakeRunner.Options(passes: 1), new FakeIoEngine()).Run(TestContext.Current.CancellationToken);
        string json = JsonSerializer.Serialize(result, DiskMarkJsonContext.Default.RunResult);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        var test = root.GetProperty("tests")[0];
        Assert.Equal("Read", test.GetProperty("mode").GetString());
        Assert.Equal("Sequential", test.GetProperty("spec").GetProperty("pattern").GetString());
        Assert.Equal("SEQ1M Q8T1", test.GetProperty("spec").GetProperty("name").GetString());
        Assert.True(test.GetProperty("latency").GetProperty("p999Us").GetDouble() > 0);
    }
}
