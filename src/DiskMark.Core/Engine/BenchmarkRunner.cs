using System.Diagnostics;
using System.Runtime.InteropServices;
using DiskMark.Core.Common;
using DiskMark.Core.Hardware;
using DiskMark.Core.Telemetry;

namespace DiskMark.Core.Engine;

/// <summary>
/// Runs a full benchmark: probe the target, write the test file, then measure every test for every pass.
/// Order matches CrystalDiskMark: all read tests, then all write tests, with an interval before each measurement.
/// </summary>
public sealed class BenchmarkRunner
{
    /// <summary>Faster than PCIe 5.0 x4, the fastest single-drive interface in common use.</summary>
    private const double PlausibleMaxMegabytesPerSecond = 16_000;

    /// <summary>No flash device completes a 4 KiB random read this fast; RAM-cached reads do.</summary>
    private const double PlausibleMinQ1LatencyUs = 5;

    private readonly BenchmarkOptions _options;
    private readonly IIoEngine _engine;
    private readonly Func<BenchmarkOptions, IBenchmarkTarget> _targetFactory;
    private readonly Func<string, FileSystemDetails> _probe;
    private readonly Func<FileSystemDetails, string, StorageDeviceInfo?> _deviceLookup;

    private PhaseState _state = new(BenchmarkPhase.Idle);
    private long _prepareBytes;
    private int _completedMeasurements;
    private int _totalMeasurements;

    public BenchmarkRunner(BenchmarkOptions options, IIoEngine? engine = null)
        : this(
            options,
            engine ?? IoEngineFactory.Create(),
            o => TestFile.Create(o.TargetDirectory, o.TestFileSize, o.WriteThrough),
            FileSystemProbe.Probe,
            DeviceInfoProvider.TryGet)
    {
    }

    internal BenchmarkRunner(
        BenchmarkOptions options,
        IIoEngine engine,
        Func<BenchmarkOptions, IBenchmarkTarget> targetFactory,
        Func<string, FileSystemDetails> probe,
        Func<FileSystemDetails, string, StorageDeviceInfo?> deviceLookup)
    {
        _options = options;
        _engine = engine;
        _targetFactory = targetFactory;
        _probe = probe;
        _deviceLookup = deviceLookup;
    }

    /// <summary>Raised on the benchmark thread after each pass with the aggregated result so far.</summary>
    public event Action<TestResult>? ResultUpdated;

    /// <summary>Raised on the benchmark thread once the target is opened, before the test file is written.</summary>
    public event Action<RunEnvironment>? EnvironmentResolved;

    public BenchmarkOptions Options => _options;

    public string EngineName => _engine.Name;

    public Task<RunResult> RunAsync(CancellationToken cancellationToken = default) =>
        Task.Factory.StartNew(() => Run(cancellationToken), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    /// <summary>
    /// Runs the benchmark. Setup failures throw <see cref="BenchmarkException"/>. Cancellation and I/O errors during
    /// measurement return a result with <see cref="RunResult.Completed"/> false and the passes finished so far.
    /// </summary>
    public RunResult Run(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = RunCore(cancellationToken);
            SetState(new PhaseState(result.Completed ? BenchmarkPhase.Completed : result.Cancelled ? BenchmarkPhase.Cancelled : BenchmarkPhase.Failed));
            return result;
        }
        catch
        {
            SetState(new PhaseState(BenchmarkPhase.Failed));
            throw;
        }
    }

    public BenchmarkSnapshot GetSnapshot()
    {
        var state = Volatile.Read(ref _state);
        long bytes = 0;
        long operations = 0;
        if (state.Phase == BenchmarkPhase.Preparing)
            bytes = Volatile.Read(ref _prepareBytes);
        else if (state.Counters is not null)
            (bytes, operations) = state.Counters.Read();

        double elapsed = Stopwatch.GetElapsedTime(state.StartTimestamp).TotalSeconds;
        int completed = Volatile.Read(ref _completedMeasurements);
        double units = _totalMeasurements + 1;
        double progress = state.Phase switch
        {
            BenchmarkPhase.Completed => 1,
            BenchmarkPhase.Preparing when state.TotalBytes > 0 => bytes / (double)state.TotalBytes / units,
            BenchmarkPhase.Interval => (1 + completed) / units,
            BenchmarkPhase.Measuring when state.DurationSeconds > 0 => (1 + completed + Math.Min(1, elapsed / state.DurationSeconds)) / units,
            BenchmarkPhase.Cancelled or BenchmarkPhase.Failed => (1 + completed) / units,
            _ => 0,
        };

        return new BenchmarkSnapshot(
            state.Phase,
            state.Spec,
            state.Mode,
            state.Pass,
            _options.Passes,
            elapsed,
            state.DurationSeconds,
            bytes,
            operations,
            state.TotalBytes,
            Math.Clamp(progress, 0, 1));
    }

    private RunResult RunCore(CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        long startTimestamp = Stopwatch.GetTimestamp();
        _options.Validate();

        SetState(new PhaseState(BenchmarkPhase.Probing));
        string directory = Path.GetFullPath(_options.TargetDirectory);
        var fileSystem = _probe(directory);
        if (fileSystem.Support == FileSystemSupport.Unsupported)
            throw new BenchmarkException(BenchmarkErrorKind.FileSystem, fileSystem.Note ?? $"{fileSystem.Type} cannot hold a test file.");

        var warnings = new List<string>();
        if (fileSystem.Note is not null)
            warnings.Add(fileSystem.Note);

        long required = _options.TestFileSize + Math.Max(64 * ByteSize.MiB, _options.TestFileSize / 20);
        if (fileSystem.AvailableBytes > 0 && fileSystem.AvailableBytes < required)
        {
            throw new BenchmarkException(
                BenchmarkErrorKind.InsufficientSpace,
                $"Not enough free space: the test needs {ByteSize.Format(required)} (file plus margin), " +
                $"but only {ByteSize.Format(fileSystem.AvailableBytes)} is available.");
        }

        var device = _deviceLookup(fileSystem, directory);
        var plan = BuildPlan();
        _totalMeasurements = plan.Count * _options.Passes;

        var results = new List<TestResult>(plan.Count);
        bool completed = false;
        bool cancelled = false;
        string? error = null;
        RunEnvironment environment;

        using (var target = _targetFactory(_options))
        {
            foreach (var spec in _options.Profile.Tests)
            {
                if (spec.BlockSize % target.Alignment.Offset != 0)
                {
                    throw new BenchmarkException(
                        BenchmarkErrorKind.InvalidOptions,
                        $"{spec.Name} uses {spec.BlockSize}-byte blocks, but this device requires multiples of {target.Alignment.Offset} bytes.");
                }
            }

            environment = new RunEnvironment(
                AppInfo.Version,
                RuntimeInformation.OSDescription.Trim(),
                RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
                Environment.ProcessorCount,
                directory,
                fileSystem,
                device,
                target.Alignment.Memory,
                target.Alignment.Offset);
            EnvironmentResolved?.Invoke(environment);

            try
            {
                SetState(new PhaseState(BenchmarkPhase.Preparing, TotalBytes: target.Length));
                target.Prepare(_options.DataPattern, bytes => Volatile.Write(ref _prepareBytes, bytes), cancellationToken);

                foreach (var (spec, mode) in plan)
                    MeasureTest(target, spec, mode, results, cancellationToken);

                completed = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                error = "Cancelled.";
            }
            catch (IOException e)
            {
                error = $"I/O error: {e.Message}";
            }

            if (completed)
                AddPlausibilityWarnings(results, target, warnings);
        }

        var configuration = new RunConfiguration(
            _options.Profile.Name,
            _options.TestFileSize,
            _options.Passes,
            _options.MeasureDuration.TotalSeconds,
            _options.IntervalDuration.TotalSeconds,
            _options.WarmupDuration.TotalSeconds,
            _options.DataPattern,
            _options.WriteThrough,
            _engine.Name);

        return new RunResult(
            RunResult.CurrentSchemaVersion,
            startedAt,
            Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
            completed,
            cancelled,
            error,
            configuration,
            environment,
            results,
            warnings);
    }

    private void MeasureTest(IBenchmarkTarget target, TestSpec spec, IoMode mode, List<TestResult> results, CancellationToken cancellationToken)
    {
        var passes = new List<PassResult>(_options.Passes);
        var latency = new LatencyHistogram();
        double duration = (_options.WarmupDuration + _options.MeasureDuration).TotalSeconds;

        for (int pass = 1; pass <= _options.Passes; pass++)
        {
            if (_options.IntervalDuration > TimeSpan.Zero)
            {
                SetState(new PhaseState(BenchmarkPhase.Interval, spec, mode, pass, _options.IntervalDuration.TotalSeconds));
                if (cancellationToken.WaitHandle.WaitOne(_options.IntervalDuration))
                    cancellationToken.ThrowIfCancellationRequested();
            }

            var counters = new LiveCounters(spec.OutstandingIos);
            SetState(new PhaseState(BenchmarkPhase.Measuring, spec, mode, pass, duration, counters));

            var measurement = _engine.Run(
                new IoRequest
                {
                    Handle = target.Handle,
                    FileLength = target.Length,
                    Spec = spec,
                    Mode = mode,
                    Duration = _options.MeasureDuration,
                    Warmup = _options.WarmupDuration,
                    Alignment = target.Alignment,
                    DataPattern = _options.DataPattern,
                    Counters = counters,
                },
                cancellationToken);

            passes.Add(PassResult.Create(measurement.Bytes, measurement.Operations, measurement.ElapsedSeconds));
            latency.Add(measurement.Histogram);
            Interlocked.Increment(ref _completedMeasurements);

            var result = CreateResult(spec, mode, passes, latency);
            int existing = results.FindIndex(r => r.Spec == spec && r.Mode == mode);
            if (existing >= 0)
                results[existing] = result;
            else
                results.Add(result);

            ResultUpdated?.Invoke(result);
        }
    }

    private List<(TestSpec Spec, IoMode Mode)> BuildPlan()
    {
        var plan = new List<(TestSpec, IoMode)>();
        if (_options.IncludeReads)
            plan.AddRange(_options.Profile.Tests.Select(t => (t, IoMode.Read)));
        if (_options.IncludeWrites)
            plan.AddRange(_options.Profile.Tests.Select(t => (t, IoMode.Write)));
        return plan;
    }

    internal static TestResult CreateResult(TestSpec spec, IoMode mode, IReadOnlyList<PassResult> passes, LatencyHistogram latency)
    {
        var throughput = passes.Select(p => p.MegabytesPerSecond).ToArray();
        var iops = passes.Select(p => p.Iops).ToArray();
        return new TestResult(
            spec,
            mode,
            passes.ToArray(),
            throughput.Length == 0 ? 0 : throughput.Max(),
            Median(throughput),
            iops.Length == 0 ? 0 : iops.Max(),
            Median(iops),
            latency.Summarize(),
            latency.ToDisplayBuckets());
    }

    internal static double Median(double[] values)
    {
        if (values.Length == 0)
            return 0;

        var sorted = values.Order().ToArray();
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static void AddPlausibilityWarnings(List<TestResult> results, IBenchmarkTarget target, List<string> warnings)
    {
        foreach (var result in results.Where(r => r.Mode == IoMode.Read && r.MaxMegabytesPerSecond > PlausibleMaxMegabytesPerSecond))
        {
            warnings.Add(
                $"{result.Spec.Name} read reached {result.MaxMegabytesPerSecond:F0} MB/s, faster than any single-drive interface. " +
                "Results are likely served from a cache.");
        }

        var q1Read = results.FirstOrDefault(r =>
            r.Mode == IoMode.Read && r.Spec is { Pattern: AccessPattern.Random, QueueDepth: 1, Threads: 1 });
        if (q1Read is not null && q1Read.Latency.Samples > 0 && q1Read.Latency.MeanUs < PlausibleMinQ1LatencyUs)
        {
            warnings.Add(
                $"{q1Read.Spec.Name} read latency averaged {q1Read.Latency.MeanUs:F2} µs, faster than flash storage. " +
                "Results are likely served from a cache.");
        }

        if (target.GetCachedFraction() is > 0.05 and var cached)
            warnings.Add($"{cached * 100:F0}% of the test file is in the OS page cache, so direct I/O may not have bypassed it.");
    }

    private void SetState(PhaseState state) => Volatile.Write(ref _state, state);

    private sealed record PhaseState(
        BenchmarkPhase Phase,
        TestSpec? Spec = null,
        IoMode? Mode = null,
        int Pass = 0,
        double DurationSeconds = 0,
        LiveCounters? Counters = null,
        long TotalBytes = 0)
    {
        public long StartTimestamp { get; } = Stopwatch.GetTimestamp();
    }
}
