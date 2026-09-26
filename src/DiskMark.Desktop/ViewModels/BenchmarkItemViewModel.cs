using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskMark.Core.Common;

namespace DiskMark.Desktop.ViewModels;

/// <summary>One row of the CrystalDiskMark score matrix: a test with its read and write cells.</summary>
public sealed partial class BenchmarkItemViewModel(TestSpec spec, Action<TestResult> select) : ObservableObject
{
    private TestResult? _read;
    private TestResult? _write;
    private (double MegabytesPerSecond, double Iops)? _liveRead;
    private (double MegabytesPerSecond, double Iops)? _liveWrite;

    public TestSpec Spec => spec;

    public string Title => $"{(spec.Pattern == AccessPattern.Sequential ? "SEQ" : "RND")}{ByteSize.FormatCompact(spec.BlockSize)}";

    public string Subtitle => $"Q{spec.QueueDepth}T{spec.Threads}";

    [ObservableProperty]
    public partial string ReadText { get; set; } = Format(null, DisplayUnit.MegabytesPerSecond);

    [ObservableProperty]
    public partial string WriteText { get; set; } = Format(null, DisplayUnit.MegabytesPerSecond);

    [ObservableProperty]
    public partial bool IsReadActive { get; set; }

    [ObservableProperty]
    public partial bool IsWriteActive { get; set; }

    public void SetResult(TestResult result, DisplayUnit unit)
    {
        if (result.Mode == IoMode.Read)
        {
            _read = result;
            _liveRead = null;
        }
        else
        {
            _write = result;
            _liveWrite = null;
        }

        Refresh(unit);
    }

    /// <summary>Shows the in-progress rate in the active cell until the pass completes.</summary>
    public void SetLive(IoMode mode, double megabytesPerSecond, double iops, DisplayUnit unit)
    {
        if (mode == IoMode.Read)
        {
            _liveRead = (megabytesPerSecond, iops);
            IsReadActive = true;
        }
        else
        {
            _liveWrite = (megabytesPerSecond, iops);
            IsWriteActive = true;
        }

        Refresh(unit);
    }

    public void ClearActive()
    {
        IsReadActive = IsWriteActive = false;
        _liveRead = _liveWrite = null;
    }

    public void Refresh(DisplayUnit unit)
    {
        ReadText = FormatCell(_read, _liveRead, unit);
        WriteText = FormatCell(_write, _liveWrite, unit);
    }

    [RelayCommand]
    private void SelectRead()
    {
        if (_read is not null)
            select(_read);
    }

    [RelayCommand]
    private void SelectWrite()
    {
        if (_write is not null)
            select(_write);
    }

    private string FormatCell(TestResult? result, (double MegabytesPerSecond, double Iops)? live, DisplayUnit unit)
    {
        if (live is { } l)
        {
            // Little's law: with a fixed number of outstanding I/Os, mean latency = outstanding / IOPS.
            double? latency = l.Iops > 0 ? spec.OutstandingIos / l.Iops * 1_000_000 : null;
            return Format(unit switch
            {
                DisplayUnit.MegabytesPerSecond => l.MegabytesPerSecond,
                DisplayUnit.GigabytesPerSecond => l.MegabytesPerSecond / 1000,
                DisplayUnit.Iops => l.Iops,
                _ => latency,
            }, unit);
        }

        if (result is null)
            return Format(null, unit);

        return Format(unit switch
        {
            DisplayUnit.MegabytesPerSecond => result.MaxMegabytesPerSecond,
            DisplayUnit.GigabytesPerSecond => result.MaxMegabytesPerSecond / 1000,
            DisplayUnit.Iops => result.MaxIops,
            _ => result.Latency.MeanUs,
        }, unit);
    }

    private static string Format(double? value, DisplayUnit unit) => value switch
    {
        null => unit == DisplayUnit.GigabytesPerSecond ? "0.000" : "0.00",
        _ when unit == DisplayUnit.GigabytesPerSecond => value.Value.ToString("F3", CultureInfo.InvariantCulture),
        _ => value.Value.ToString("F2", CultureInfo.InvariantCulture),
    };
}
