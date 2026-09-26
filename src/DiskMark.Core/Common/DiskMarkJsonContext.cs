using System.Text.Json.Serialization;

namespace DiskMark.Core.Common;

/// <summary>Source-generated JSON metadata. Reflection-based serialization is unavailable under Native AOT.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RunResult))]
public sealed partial class DiskMarkJsonContext : JsonSerializerContext
{
}
