using DiskMark.Core.Common;

namespace DiskMark.Core.Engine;

/// <summary>
/// Shared sequential position for all workers of one measurement, so concurrent I/Os cover consecutive
/// blocks and wrap at the end of the file.
/// </summary>
internal sealed class SequentialCursor(long fileLength, int blockSize)
{
    private readonly long _span = fileLength / blockSize * blockSize;
    private readonly int _blockSize = blockSize;
    private long _next;

    public long Next() => (Interlocked.Add(ref _next, _blockSize) - _blockSize) % _span;
}

/// <summary>Per-worker offset generator. Random offsets are block-aligned and uniform across the file.</summary>
internal sealed class OffsetSource
{
    private readonly SequentialCursor? _cursor;
    private readonly Random? _random;
    private readonly long _blockCount;
    private readonly int _blockSize;

    private OffsetSource(SequentialCursor? cursor, Random? random, long blockCount, int blockSize)
    {
        _cursor = cursor;
        _random = random;
        _blockCount = blockCount;
        _blockSize = blockSize;
    }

    public static OffsetSource Create(TestSpec spec, long fileLength, SequentialCursor? sharedCursor) =>
        spec.Pattern == AccessPattern.Sequential
            ? new OffsetSource(sharedCursor ?? new SequentialCursor(fileLength, spec.BlockSize), null, 0, spec.BlockSize)
            : new OffsetSource(null, new Random(), fileLength / spec.BlockSize, spec.BlockSize);

    public long Next() => _cursor?.Next() ?? _random!.NextInt64(_blockCount) * _blockSize;
}
