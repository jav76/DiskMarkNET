namespace DiskMark.Core.Common;

/// <summary>One CrystalDiskMark-style test, e.g. RND4K Q32T1. Each spec is measured for both reads and writes.</summary>
public sealed record TestSpec(AccessPattern Pattern, int BlockSize, int QueueDepth, int Threads)
{
    public string Name =>
        $"{(Pattern == AccessPattern.Sequential ? "SEQ" : "RND")}{ByteSize.FormatCompact(BlockSize)} Q{QueueDepth}T{Threads}";

    public int OutstandingIos => QueueDepth * Threads;

    public override string ToString() => Name;
}
