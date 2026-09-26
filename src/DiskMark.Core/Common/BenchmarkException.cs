namespace DiskMark.Core.Common;

public enum BenchmarkErrorKind
{
    InvalidOptions,
    Target,
    FileSystem,
    InsufficientSpace,
    DirectIoUnsupported,
    Io,
}

public sealed class BenchmarkException(BenchmarkErrorKind kind, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public BenchmarkErrorKind Kind { get; } = kind;
}
