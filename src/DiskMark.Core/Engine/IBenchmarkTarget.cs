using DiskMark.Core.Common;
using DiskMark.Core.Memory;
using Microsoft.Win32.SafeHandles;

namespace DiskMark.Core.Engine;

/// <summary>The storage a benchmark runs against. <see cref="TestFile"/> is the production implementation.</summary>
public interface IBenchmarkTarget : IDisposable
{
    SafeFileHandle Handle { get; }

    long Length { get; }

    IoAlignment Alignment { get; }

    /// <summary>Writes the whole target so later reads hit real data on the device.</summary>
    void Prepare(DataPattern pattern, Action<long> progress, CancellationToken cancellationToken);

    /// <summary>Fraction of the target held in the OS page cache, or null when unknown.</summary>
    double? GetCachedFraction();
}
