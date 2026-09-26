using System.Diagnostics;
using System.Runtime.ExceptionServices;
using DiskMark.Core.Common;
using DiskMark.Core.Memory;
using DiskMark.Core.Telemetry;

namespace DiskMark.Core.Engine;

/// <summary>
/// Emulates queue depth with one dedicated thread per outstanding I/O, each issuing blocking reads or writes.
/// Portable to every OS; on Linux and macOS it is the default because .NET's async file APIs there are also
/// blocking calls on thread-pool threads. Q32T1 therefore runs as 32 threads.
/// </summary>
public sealed class ThreadedIoEngine : IIoEngine
{
    private const int WorkerStackSize = 256 * 1024;

    public string Name => "threaded";

    public MeasurementResult Run(IoRequest request, CancellationToken cancellationToken)
    {
        var spec = request.Spec;
        int workers = spec.OutstandingIos;
        var cursor = spec.Pattern == AccessPattern.Sequential ? new SequentialCursor(request.FileLength, spec.BlockSize) : null;
        var buffers = new AlignedBuffer[workers];
        var histograms = new LatencyHistogram[workers];
        var totals = new (long Bytes, long Operations)[workers];
        var threads = new List<Thread>(workers);
        var window = new MeasurementWindow();
        ExceptionDispatchInfo? failure = null;

        using var ready = new CountdownEvent(workers);
        using var go = new ManualResetEventSlim(false);

        try
        {
            for (int i = 0; i < workers; i++)
            {
                buffers[i] = new AlignedBuffer(spec.BlockSize, request.Alignment.Memory);
                if (request.Mode == IoMode.Write && request.DataPattern == DataPattern.Random)
                    buffers[i].FillRandom();
                histograms[i] = new LatencyHistogram();
            }

            for (int i = 0; i < workers; i++)
            {
                int worker = i;
                var thread = new Thread(() => Worker(worker), WorkerStackSize)
                {
                    IsBackground = true,
                    Name = $"DiskMark I/O {worker}",
                };
                thread.Start();
                threads.Add(thread);
            }

            ready.Wait(cancellationToken);
            window.Open(request.Warmup, request.Duration);
        }
        finally
        {
            if (!window.IsOpen)
                window.Abort();
            go.Set();
            foreach (var thread in threads)
                thread.Join();
            foreach (var buffer in buffers)
                buffer?.Dispose();
        }

        failure?.Throw();
        cancellationToken.ThrowIfCancellationRequested();

        var merged = new LatencyHistogram();
        long bytes = 0;
        long operations = 0;
        for (int i = 0; i < workers; i++)
        {
            merged.Add(histograms[i]);
            bytes += totals[i].Bytes;
            operations += totals[i].Operations;
        }

        return new MeasurementResult(bytes, operations, request.Duration.TotalSeconds, merged);

        void Worker(int index)
        {
            try
            {
                ready.Signal();
                go.Wait();
                if (window.Aborted)
                    return;

                var span = buffers[index].GetSpan();
                var histogram = histograms[index];
                var offsets = OffsetSource.Create(spec, request.FileLength, cursor);
                var handle = request.Handle;
                bool read = request.Mode == IoMode.Read;
                long countFrom = window.CountFrom;
                long end = window.End;
                long bytes = 0;
                long operations = 0;

                while (!cancellationToken.IsCancellationRequested && !window.Aborted)
                {
                    long offset = offsets.Next();
                    long start = Stopwatch.GetTimestamp();
                    int transferred;
                    if (read)
                    {
                        transferred = RandomAccess.Read(handle, span, offset);
                    }
                    else
                    {
                        RandomAccess.Write(handle, (ReadOnlySpan<byte>)span, offset);
                        transferred = span.Length;
                    }

                    long finish = Stopwatch.GetTimestamp();
                    if (finish > end)
                        break;
                    if (start < countFrom)
                        continue;

                    histogram.RecordTicks(finish - start);
                    bytes += transferred;
                    operations++;
                    request.Counters.Publish(index, bytes, operations);
                }

                totals[index] = (bytes, operations);
            }
            catch (Exception e)
            {
                Interlocked.CompareExchange(ref failure, ExceptionDispatchInfo.Capture(e), null);
                window.Abort();
            }
        }
    }
}

/// <summary>Timestamps bounding a measurement: I/Os count only if they start after warmup and finish before the end.</summary>
internal sealed class MeasurementWindow
{
    private volatile bool _aborted;

    public bool IsOpen { get; private set; }

    public bool Aborted => _aborted;

    public long CountFrom { get; private set; }

    public long End { get; private set; }

    public void Open(TimeSpan warmup, TimeSpan duration)
    {
        long now = Stopwatch.GetTimestamp();
        CountFrom = now + (long)(warmup.TotalSeconds * Stopwatch.Frequency);
        End = CountFrom + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        IsOpen = true;
    }

    public void Abort() => _aborted = true;
}
