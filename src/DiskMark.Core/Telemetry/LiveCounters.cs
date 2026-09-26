using System.Runtime.InteropServices;

namespace DiskMark.Core.Telemetry;

/// <summary>
/// Per-worker byte and operation counters for live progress. Each worker writes only its own slot, padded to a
/// separate cache line, so the hot path has no shared writes. Readers sum the slots.
/// </summary>
public sealed class LiveCounters(int workerCount)
{
    private readonly Slot[] _slots = new Slot[workerCount];

    public int WorkerCount => _slots.Length;

    public void Publish(int worker, long bytes, long operations)
    {
        ref var slot = ref _slots[worker];
        Volatile.Write(ref slot.Bytes, bytes);
        Volatile.Write(ref slot.Operations, operations);
    }

    public (long Bytes, long Operations) Read()
    {
        long bytes = 0;
        long operations = 0;
        for (int i = 0; i < _slots.Length; i++)
        {
            bytes += Volatile.Read(ref _slots[i].Bytes);
            operations += Volatile.Read(ref _slots[i].Operations);
        }

        return (bytes, operations);
    }

    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct Slot
    {
        [FieldOffset(64)]
        public long Bytes;

        [FieldOffset(72)]
        public long Operations;
    }
}
