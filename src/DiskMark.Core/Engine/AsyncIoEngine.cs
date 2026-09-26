using System.Diagnostics;
using DiskMark.Core.Common;
using DiskMark.Core.Memory;
using DiskMark.Core.Telemetry;

namespace DiskMark.Core.Engine;

/// <summary>
/// Keeps QD x T asynchronous operations in flight with <see cref="RandomAccess"/> async APIs. On Windows the test
/// file handle is overlapped, so this is true async I/O completed through IOCP. On other platforms .NET runs these
/// calls as blocking I/O on the thread pool, so <see cref="ThreadedIoEngine"/> is the default there.
/// </summary>
public sealed class AsyncIoEngine : IIoEngine
{
    public string Name => OperatingSystem.IsWindows() ? "async-iocp" : "async-threadpool";

    public MeasurementResult Run(IoRequest request, CancellationToken cancellationToken)
    {
        var spec = request.Spec;
        int lanes = spec.OutstandingIos;
        var cursor = spec.Pattern == AccessPattern.Sequential ? new SequentialCursor(request.FileLength, spec.BlockSize) : null;
        var buffers = new AlignedBuffer[lanes];
        var histograms = new LatencyHistogram[lanes];
        var totals = new (long Bytes, long Operations)[lanes];
        var window = new MeasurementWindow();

        try
        {
            for (int i = 0; i < lanes; i++)
            {
                buffers[i] = new AlignedBuffer(spec.BlockSize, request.Alignment.Memory);
                if (request.Mode == IoMode.Write && request.DataPattern == DataPattern.Random)
                    buffers[i].FillRandom();
                histograms[i] = new LatencyHistogram();
            }

            window.Open(request.Warmup, request.Duration);
            var tasks = new Task[lanes];
            for (int i = 0; i < lanes; i++)
                tasks[i] = RunLaneAsync(i);

            try
            {
                Task.WaitAll(tasks, CancellationToken.None);
            }
            catch (AggregateException e) when (e.InnerExceptions.Count > 0)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerExceptions[0]).Throw();
            }
        }
        finally
        {
            window.Abort();
            foreach (var buffer in buffers)
                buffer?.Dispose();
        }

        cancellationToken.ThrowIfCancellationRequested();

        var merged = new LatencyHistogram();
        long bytes = 0;
        long operations = 0;
        for (int i = 0; i < lanes; i++)
        {
            merged.Add(histograms[i]);
            bytes += totals[i].Bytes;
            operations += totals[i].Operations;
        }

        return new MeasurementResult(bytes, operations, request.Duration.TotalSeconds, merged);

        async Task RunLaneAsync(int lane)
        {
            // Each lane has one operation in flight at a time, so its histogram and counters have a single writer.
            var memory = buffers[lane].Memory;
            var histogram = histograms[lane];
            var offsets = OffsetSource.Create(spec, request.FileLength, cursor);
            bool read = request.Mode == IoMode.Read;
            long laneBytes = 0;
            long laneOperations = 0;

            try
            {
                while (!cancellationToken.IsCancellationRequested && !window.Aborted)
                {
                    long offset = offsets.Next();
                    long start = Stopwatch.GetTimestamp();
                    int transferred;
                    if (read)
                    {
                        transferred = await RandomAccess.ReadAsync(request.Handle, memory, offset).ConfigureAwait(false);
                    }
                    else
                    {
                        await RandomAccess.WriteAsync(request.Handle, (ReadOnlyMemory<byte>)memory, offset).ConfigureAwait(false);
                        transferred = memory.Length;
                    }

                    long finish = Stopwatch.GetTimestamp();
                    if (finish > window.End)
                        break;
                    if (start < window.CountFrom)
                        continue;

                    histogram.RecordTicks(finish - start);
                    laneBytes += transferred;
                    laneOperations++;
                    request.Counters.Publish(lane, laneBytes, laneOperations);
                }
            }
            catch
            {
                window.Abort();
                throw;
            }

            totals[lane] = (laneBytes, laneOperations);
        }
    }
}
