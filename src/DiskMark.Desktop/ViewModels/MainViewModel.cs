using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskMark.Core.Common;
using DiskMark.Core.Engine;
using DiskMark.Core.Hardware;
using DiskMark.Core.Telemetry;

namespace DiskMark.Desktop.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const int SparklineCapacity = 300;

    // 5 Hz keeps the UI responsive without competing for CPU with latency-sensitive tests such as RND4K Q1T1.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    private readonly DispatcherTimer _timer;
    private readonly Queue<double> _samples = new();
    private BenchmarkRunner? _runner;
    private CancellationTokenSource? _runCancellation;
    private CancellationTokenSource? _probeCancellation;
    private string? _lastKey;
    private long _lastBytes;
    private long _lastOperations;
    private long _lastTimestamp;
    private double _rate;
    private double _iops;

    public MainViewModel()
    {
        _timer = new DispatcherTimer(PollInterval, DispatcherPriority.Background, (_, _) => Poll());

        SelectedProfile = BenchmarkProfile.Default;
        SelectedFileSize = FileSizes.First(o => o.Value == ByteSize.GiB);
        SelectedPasses = PassOptions.First(o => o.Value == 5);
        SelectedDuration = DurationOptions.First(o => o.Value == 5);
        SelectedInterval = IntervalOptions.First(o => o.Value == 5);
        SelectedMode = ModeOptions[0];
        SelectedUnit = UnitOptions[0];
        RebuildItems();
        RefreshTargets();
    }

    public ObservableCollection<StorageTarget> Targets { get; } = [];

    public IReadOnlyList<BenchmarkProfile> Profiles => BenchmarkProfile.All;

    public IReadOnlyList<Option<long>> FileSizes { get; } =
        new long[] { 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536 }
            .Select(mib => new Option<long>(mib * ByteSize.MiB, ByteSize.Format(mib * ByteSize.MiB)))
            .ToArray();

    public IReadOnlyList<Option<int>> PassOptions { get; } =
        Enumerable.Range(1, 9).Select(n => new Option<int>(n, n.ToString(CultureInfo.InvariantCulture))).ToArray();

    public IReadOnlyList<Option<int>> DurationOptions { get; } =
        new[] { 1, 3, 5, 10, 20, 30 }.Select(s => new Option<int>(s, $"{s} s")).ToArray();

    public IReadOnlyList<Option<int>> IntervalOptions { get; } =
        new[] { 0, 1, 3, 5, 10, 30 }.Select(s => new Option<int>(s, $"{s} s")).ToArray();

    public IReadOnlyList<Option<ModeSelection>> ModeOptions { get; } =
    [
        new(ModeSelection.All, "Read + Write"),
        new(ModeSelection.Read, "Read only"),
        new(ModeSelection.Write, "Write only"),
    ];

    public IReadOnlyList<Option<DisplayUnit>> UnitOptions { get; } =
    [
        new(DisplayUnit.MegabytesPerSecond, "MB/s"),
        new(DisplayUnit.GigabytesPerSecond, "GB/s"),
        new(DisplayUnit.Iops, "IOPS"),
        new(DisplayUnit.Microseconds, "µs"),
    ];

    public ObservableCollection<BenchmarkItemViewModel> Items { get; } = [];

    public ObservableCollection<TestResult> CompletedResults { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    public DriveInfoViewModel Drive { get; } = new();

    /// <summary>Set by the view: shows a folder picker and returns the chosen local path.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    /// <summary>Set by the view: saves text through a file picker, given a suggested file name.</summary>
    public Func<string, string, Task>? SaveFile { get; set; }

    [ObservableProperty]
    public partial StorageTarget? SelectedTarget { get; set; }

    [ObservableProperty]
    public partial string TargetDirectory { get; set; } = string.Empty;

    [ObservableProperty]
    public partial BenchmarkProfile SelectedProfile { get; set; }

    [ObservableProperty]
    public partial Option<long> SelectedFileSize { get; set; }

    [ObservableProperty]
    public partial Option<int> SelectedPasses { get; set; }

    [ObservableProperty]
    public partial Option<int> SelectedDuration { get; set; }

    [ObservableProperty]
    public partial Option<int> SelectedInterval { get; set; }

    [ObservableProperty]
    public partial Option<ModeSelection> SelectedMode { get; set; }

    [ObservableProperty]
    public partial Option<DisplayUnit> SelectedUnit { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand), nameof(SaveJsonCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Ready";

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string LiveRateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<double> ThroughputSamples { get; set; } = [];

    [ObservableProperty]
    public partial string ThroughputSummary { get; set; } = "Throughput is sampled five times per second while a benchmark runs.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedLatencyText))]
    public partial TestResult? SelectedResult { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveJsonCommand))]
    public partial RunResult? LastResult { get; set; }

    public bool IsIdle => !IsRunning;

    public string UnitLabel => SelectedUnit.Label;

    public string SelectedLatencyText => SelectedResult is { Latency: { Samples: > 0 } l } r
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"{r.Name}: mean {l.MeanUs:F2} µs · p50 {l.P50Us:F2} · p90 {l.P90Us:F2} · p99 {l.P99Us:F2} · p99.9 {l.P999Us:F2} · max {l.MaxUs:F2} µs · {l.Samples:N0} I/Os")
        : string.Empty;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task StartAsync()
    {
        var options = new BenchmarkOptions
        {
            TargetDirectory = TargetDirectory,
            Profile = SelectedProfile,
            TestFileSize = SelectedFileSize.Value,
            Passes = SelectedPasses.Value,
            MeasureDuration = TimeSpan.FromSeconds(SelectedDuration.Value),
            IntervalDuration = TimeSpan.FromSeconds(SelectedInterval.Value),
            IncludeReads = SelectedMode.Value != ModeSelection.Write,
            IncludeWrites = SelectedMode.Value != ModeSelection.Read,
        };

        try
        {
            options.Validate();
        }
        catch (BenchmarkException e)
        {
            StatusText = e.Message;
            return;
        }

        RebuildItems();
        CompletedResults.Clear();
        Warnings.Clear();
        SelectedResult = null;
        LastResult = null;
        _samples.Clear();
        ThroughputSamples = [];
        _lastKey = null;
        Progress = 0;

        var runner = new BenchmarkRunner(options);
        runner.ResultUpdated += result => Dispatcher.UIThread.Post(() => OnResultUpdated(result));
        runner.EnvironmentResolved += environment => Dispatcher.UIThread.Post(() => Drive.Show(environment));
        _runner = runner;
        _runCancellation = new CancellationTokenSource();
        IsRunning = true;
        StatusText = "Starting...";
        _timer.Start();

        try
        {
            var result = await runner.RunAsync(_runCancellation.Token);
            LastResult = result;
            foreach (var warning in result.Warnings)
                Warnings.Add(warning);

            StatusText = result.Completed
                ? string.Create(CultureInfo.InvariantCulture, $"Completed in {TimeSpan.FromSeconds(result.DurationSeconds):m\\:ss} with the {result.Configuration.Engine} engine.")
                : result.Error ?? "Stopped.";
            Progress = result.Completed ? 100 : Progress;
        }
        catch (BenchmarkException e)
        {
            StatusText = e.Message;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Error: {e.Message}";
        }
        finally
        {
            _timer.Stop();
            foreach (var item in Items)
            {
                item.ClearActive();
                item.Refresh(SelectedUnit.Value);
            }

            LiveRateText = string.Empty;
            _runCancellation.Dispose();
            _runCancellation = null;
            _runner = null;
            IsRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Stop()
    {
        _runCancellation?.Cancel();
        StatusText = "Stopping...";
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (PickFolder is null)
            return;
        var folder = await PickFolder();
        if (!string.IsNullOrEmpty(folder))
            TargetDirectory = folder;
    }

    [RelayCommand]
    private void RefreshTargets()
    {
        Targets.Clear();
        foreach (var target in TargetEnumerator.GetTargets())
            Targets.Add(target);
        SelectedTarget = Targets.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanSaveJson))]
    private async Task SaveJsonAsync()
    {
        if (LastResult is null || SaveFile is null)
            return;

        string json = JsonSerializer.Serialize(LastResult, DiskMarkJsonContext.Default.RunResult);
        string name = $"diskmark-{LastResult.StartedAt:yyyyMMdd-HHmmss}.json";
        try
        {
            await SaveFile(name, json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Could not save results: {e.Message}";
        }
    }

    private bool CanSaveJson() => !IsRunning && LastResult is not null;

    partial void OnSelectedTargetChanged(StorageTarget? value)
    {
        if (value is not null)
            TargetDirectory = value.DefaultDirectory;
    }

    partial void OnTargetDirectoryChanged(string value) => _ = ProbeTargetAsync(value);

    partial void OnSelectedProfileChanged(BenchmarkProfile value)
    {
        if (!IsRunning)
            RebuildItems();
    }

    partial void OnSelectedUnitChanged(Option<DisplayUnit> value)
    {
        OnPropertyChanged(nameof(UnitLabel));
        foreach (var item in Items)
            item.Refresh(value.Value);
    }

    private async Task ProbeTargetAsync(string directory)
    {
        _probeCancellation?.Cancel();
        var cancellation = _probeCancellation = new CancellationTokenSource();
        try
        {
            // Debounce typing in the path box.
            await Task.Delay(250, cancellation.Token);
            var (fileSystem, device) = await Task.Run(
                () =>
                {
                    var fs = FileSystemProbe.Probe(directory);
                    return (fs, DeviceInfoProvider.TryGet(fs, directory));
                },
                cancellation.Token);

            if (!cancellation.IsCancellationRequested)
                Drive.Show(Path.GetFullPath(directory), fileSystem, device);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is BenchmarkException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (!cancellation.IsCancellationRequested)
                Drive.ShowError(directory, e.Message);
        }
    }

    private void RebuildItems()
    {
        Items.Clear();
        foreach (var spec in SelectedProfile.Tests)
            Items.Add(new BenchmarkItemViewModel(spec, result => SelectedResult = result));
        foreach (var item in Items)
            item.Refresh(SelectedUnit?.Value ?? DisplayUnit.MegabytesPerSecond);
    }

    private void OnResultUpdated(TestResult result)
    {
        Items.FirstOrDefault(i => i.Spec == result.Spec)?.SetResult(result, SelectedUnit.Value);

        int index = CompletedResults.ToList().FindIndex(r => r.Spec == result.Spec && r.Mode == result.Mode);
        bool wasSelected = SelectedResult is not null && SelectedResult.Spec == result.Spec && SelectedResult.Mode == result.Mode;
        if (index >= 0)
            CompletedResults[index] = result;
        else
            CompletedResults.Add(result);

        if (wasSelected || SelectedResult is null)
            SelectedResult = result;
    }

    private void Poll()
    {
        if (_runner is null)
            return;

        var snapshot = _runner.GetSnapshot();
        Progress = snapshot.OverallProgress * 100;
        UpdateRates(snapshot);

        _samples.Enqueue(snapshot.Phase is BenchmarkPhase.Measuring or BenchmarkPhase.Preparing ? _rate : 0);
        while (_samples.Count > SparklineCapacity)
            _samples.Dequeue();
        ThroughputSamples = _samples.ToArray();
        ThroughputSummary = string.Create(
            CultureInfo.InvariantCulture,
            $"Now {_rate:F1} MB/s · peak in window {(_samples.Count > 0 ? _samples.Max() : 0):F1} MB/s · last {_samples.Count / 5} s");

        foreach (var item in Items)
        {
            bool active = snapshot.Phase == BenchmarkPhase.Measuring && item.Spec == snapshot.Spec;
            if (!active && (item.IsReadActive || item.IsWriteActive))
            {
                item.ClearActive();
                item.Refresh(SelectedUnit.Value);
            }
        }

        if (snapshot is { Phase: BenchmarkPhase.Measuring, Spec: { } spec, Mode: { } mode })
            Items.FirstOrDefault(i => i.Spec == spec)?.SetLive(mode, _rate, _iops, SelectedUnit.Value);

        StatusText = snapshot.Phase switch
        {
            BenchmarkPhase.Probing => "Inspecting target...",
            BenchmarkPhase.Preparing => $"Writing test file: {ByteSize.Format(snapshot.Bytes)} of {ByteSize.Format(snapshot.PhaseTotalBytes)}",
            BenchmarkPhase.Interval => string.Create(
                CultureInfo.InvariantCulture,
                $"{snapshot.Mode} {snapshot.Spec?.Name}: pass {snapshot.Pass}/{snapshot.PassCount}, waiting {Math.Max(0, snapshot.PhaseDurationSeconds - snapshot.PhaseElapsedSeconds):F1} s"),
            BenchmarkPhase.Measuring => $"{snapshot.Mode} {snapshot.Spec?.Name}: pass {snapshot.Pass}/{snapshot.PassCount}",
            _ => StatusText,
        };
        LiveRateText = snapshot.Phase is BenchmarkPhase.Measuring or BenchmarkPhase.Preparing
            ? string.Create(CultureInfo.InvariantCulture, $"{_rate:F1} MB/s")
            : string.Empty;
    }

    private void UpdateRates(BenchmarkSnapshot snapshot)
    {
        string key = $"{snapshot.Phase}|{snapshot.Spec?.Name}|{snapshot.Mode}|{snapshot.Pass}";
        long now = Stopwatch.GetTimestamp();
        if (key != _lastKey)
        {
            _lastKey = key;
            _rate = _iops = 0;
            _lastBytes = snapshot.Bytes;
            _lastOperations = snapshot.Operations;
            _lastTimestamp = now;
            return;
        }

        double seconds = Stopwatch.GetElapsedTime(_lastTimestamp, now).TotalSeconds;
        if (seconds < 0.1)
            return;

        double rate = (snapshot.Bytes - _lastBytes) / seconds / 1_000_000d;
        double iops = (snapshot.Operations - _lastOperations) / seconds;
        _rate = _rate == 0 ? rate : (_rate * 0.6) + (rate * 0.4);
        _iops = _iops == 0 ? iops : (_iops * 0.6) + (iops * 0.4);
        _lastBytes = snapshot.Bytes;
        _lastOperations = snapshot.Operations;
        _lastTimestamp = now;
    }
}
