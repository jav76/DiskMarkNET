using System.Buffers.Binary;
using DiskMark.Core.Common;
using DiskMark.Core.Interop;
using DiskMark.Core.Memory;
using Microsoft.Win32.SafeHandles;

namespace DiskMark.Core.Engine;

/// <summary>
/// A temporary benchmark file opened for direct I/O. It is removed even if the process is killed: Windows deletes
/// it on handle close (DeleteOnClose), and on Linux/macOS the path is unlinked right after opening, so the kernel
/// frees the data when the descriptor closes.
/// </summary>
public sealed class TestFile : IBenchmarkTarget
{
    private const int PrepareChunkSize = 4 * 1024 * 1024;
    private const int StampInterval = 4096;

    private readonly string _path;

    private TestFile(SafeFileHandle handle, string path, long length, IoAlignment alignment)
    {
        Handle = handle;
        _path = path;
        Length = length;
        Alignment = alignment;
    }

    public SafeFileHandle Handle { get; }

    public long Length { get; }

    public IoAlignment Alignment { get; }

    public static TestFile Create(string directory, long length, bool writeThrough)
    {
        string fullDirectory = Path.GetFullPath(directory);
        if (!Directory.Exists(fullDirectory))
            throw new BenchmarkException(BenchmarkErrorKind.Target, $"Directory not found: {fullDirectory}");

        // A random name plus CreateNew (O_EXCL) never follows an existing path or planted symlink.
        string path = Path.Combine(fullDirectory, $"DiskMarkNET-{Guid.NewGuid():N}.tmp");
        SafeFileHandle handle;
        try
        {
            handle = File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, NativeStorage.GetOpenOptions(writeThrough));
        }
        catch (UnauthorizedAccessException e)
        {
            throw new BenchmarkException(
                BenchmarkErrorKind.Target,
                $"Cannot create a test file in {fullDirectory}: access denied. Choose a writable directory.",
                e);
        }
        catch (IOException e)
        {
            throw new BenchmarkException(BenchmarkErrorKind.Target, $"Cannot create a test file in {fullDirectory}: {e.Message}", e);
        }

        try
        {
            if (!OperatingSystem.IsWindows())
                File.Delete(path);

            NativeStorage.EnableDirectIo(handle);
            var alignment = DirectIoAlignment.Resolve(handle, fullDirectory);
            if (length % alignment.Offset != 0)
            {
                throw new BenchmarkException(
                    BenchmarkErrorKind.InvalidOptions,
                    $"Test file size must be a multiple of the device's {alignment.Offset}-byte I/O alignment.");
            }

            return new TestFile(handle, path, length, alignment);
        }
        catch
        {
            handle.Dispose();
            TryDelete(path);
            throw;
        }
    }

    public void Prepare(DataPattern pattern, Action<long> progress, CancellationToken cancellationToken)
    {
        int chunkSize = (int)Math.Min(PrepareChunkSize, Length);
        using var buffer = new AlignedBuffer(chunkSize, Alignment.Memory);
        if (pattern == DataPattern.Random)
            buffer.FillRandom();

        var span = buffer.GetSpan();
        long chunkIndex = 0;
        for (long offset = 0; offset < Length; offset += chunkSize, chunkIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int length = (int)Math.Min(chunkSize, Length - offset);

            // Stamping each 4 KiB block keeps random data unique so deduplicating controllers cannot skip it.
            if (pattern == DataPattern.Random)
            {
                for (int block = 0; block + sizeof(long) <= length; block += StampInterval)
                    BinaryPrimitives.WriteInt64LittleEndian(span[block..], (chunkIndex << 20) | (uint)block);
            }

            try
            {
                RandomAccess.Write(Handle, (ReadOnlySpan<byte>)span[..length], offset);
            }
            catch (IOException e) when (IsDiskFull(e))
            {
                throw new BenchmarkException(
                    BenchmarkErrorKind.InsufficientSpace,
                    $"The disk filled up while writing the {ByteSize.Format(Length)} test file.",
                    e);
            }

            progress(offset + length);
        }
    }

    public double? GetCachedFraction() => PageCache.GetResidentFraction(Handle, Length);

    public void Dispose()
    {
        Handle.Dispose();
        if (OperatingSystem.IsWindows())
            TryDelete(_path);
    }

    private static bool IsDiskFull(IOException e)
    {
        const int ENOSPC = 28;
        const int ERROR_HANDLE_DISK_FULL = 39;
        const int ERROR_DISK_FULL = 112;
        int code = e.HResult & 0xFFFF;
        return OperatingSystem.IsWindows() ? code is ERROR_DISK_FULL or ERROR_HANDLE_DISK_FULL : e.HResult == ENOSPC;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
