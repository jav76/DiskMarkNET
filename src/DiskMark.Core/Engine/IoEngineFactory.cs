using DiskMark.Core.Common;

namespace DiskMark.Core.Engine;

public static class IoEngineFactory
{
    public static IReadOnlyList<string> Names { get; } = ["auto", "threaded", "async"];

    /// <summary>
    /// "auto" picks true async I/O (IOCP) on Windows and dedicated threads elsewhere, where .NET has no native
    /// async file I/O.
    /// </summary>
    public static IIoEngine Create(string? name = null) => name?.Trim().ToLowerInvariant() switch
    {
        null or "" or "auto" => OperatingSystem.IsWindows() ? new AsyncIoEngine() : new ThreadedIoEngine(),
        "threaded" => new ThreadedIoEngine(),
        "async" => new AsyncIoEngine(),
        _ => throw new BenchmarkException(
            BenchmarkErrorKind.InvalidOptions,
            $"Unknown I/O engine '{name}'. Expected one of: {string.Join(", ", Names)}."),
    };
}
