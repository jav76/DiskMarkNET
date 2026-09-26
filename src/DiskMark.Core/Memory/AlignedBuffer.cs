using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;

namespace DiskMark.Core.Memory;

/// <summary>
/// Native memory aligned for direct I/O. Derives from <see cref="MemoryManager{T}"/> so the same buffer can be
/// passed to both the span-based and the Memory-based (async) RandomAccess APIs.
/// </summary>
public sealed unsafe class AlignedBuffer : MemoryManager<byte>
{
    private void* _pointer;

    public AlignedBuffer(int length, int alignment)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        if (alignment <= 0 || !BitOperations.IsPow2(alignment))
            throw new ArgumentException("Alignment must be a positive power of two.", nameof(alignment));

        Length = length;
        Alignment = alignment;
        _pointer = NativeMemory.AlignedAlloc((nuint)length, (nuint)alignment);
        NativeMemory.Clear(_pointer, (nuint)length);
    }

    public int Length { get; }

    public int Alignment { get; }

    public nint Address => (nint)EnsureAlive();

    public Span<byte> Span => GetSpan();

    public override Span<byte> GetSpan() => new(EnsureAlive(), Length);

    public override MemoryHandle Pin(int elementIndex = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elementIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(elementIndex, Length);
        return new MemoryHandle((byte*)EnsureAlive() + elementIndex);
    }

    public override void Unpin()
    {
    }

    public void FillRandom() => Random.Shared.NextBytes(GetSpan());

    /// <summary>Frees the native memory. There is no finalizer (CA2015): callers must dispose after all I/O completes.</summary>
    public void Dispose() => ((IDisposable)this).Dispose();

    protected override void Dispose(bool disposing)
    {
        if (_pointer != null)
        {
            NativeMemory.AlignedFree(_pointer);
            _pointer = null;
        }
    }

    private void* EnsureAlive() => _pointer != null ? _pointer : throw new ObjectDisposedException(nameof(AlignedBuffer));
}
