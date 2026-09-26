using System.Diagnostics;
using System.Globalization;
using DiskMark.Core.Common;
using DiskMark.Core.Engine;
using DiskMark.Core.Telemetry;

namespace DiskMark.Cli;

/// <summary>
/// Polls the runner at 5 Hz and writes progress to stderr, keeping stdout clean for tables and JSON. On a terminal
/// it redraws one line; when redirected it prints a line per phase change.
/// </summary>
internal sealed class ProgressReporter : IDisposable
{
    private readonly BenchmarkRunner _runner;
    private readonly Ansi _ansi;
    private readonly bool _interactive;
    private readonly Lock _gate = new();
    private readonly Timer _timer;
    private string? _lastKey;
    private long _lastBytes;
    private long _lastTimestamp;
    private double _rate;
    private bool _lineOpen;
    private bool _stopped;

    public ProgressReporter(BenchmarkRunner runner, Ansi ansi, bool interactive)
    {
        _runner = runner;
        _ansi = ansi;
        _interactive = interactive;
        _timer = new Timer(_ => Tick(), null, 0, 200);
    }

    public void WriteLine(string text)
    {
        lock (_gate)
        {
            ClearLine();
            Console.Error.WriteLine(text);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _stopped = true;
            _timer.Dispose();
            ClearLine();
        }
    }

    private void Tick()
    {
        lock (_gate)
        {
            if (_stopped)
                return;

            var snapshot = _runner.GetSnapshot();
            string key = $"{snapshot.Phase}|{snapshot.Spec?.Name}|{snapshot.Mode}|{snapshot.Pass}";
            long now = Stopwatch.GetTimestamp();
            if (key != _lastKey)
            {
                _rate = 0;
                _lastBytes = snapshot.Bytes;
                _lastTimestamp = now;
            }
            else
            {
                double seconds = Stopwatch.GetElapsedTime(_lastTimestamp, now).TotalSeconds;
                if (seconds >= 0.1)
                {
                    double instant = (snapshot.Bytes - _lastBytes) / seconds / 1_000_000d;
                    _rate = _rate == 0 ? instant : (_rate * 0.6) + (instant * 0.4);
                    _lastBytes = snapshot.Bytes;
                    _lastTimestamp = now;
                }
            }

            bool changed = key != _lastKey;
            _lastKey = key;
            if (_interactive)
            {
                string line = Format(snapshot, live: true);
                if (line.Length > 0)
                {
                    Console.Error.Write(_ansi.ClearLine + line);
                    _lineOpen = true;
                }
            }
            else if (changed && snapshot.Phase is not BenchmarkPhase.Interval)
            {
                string line = Format(snapshot, live: false);
                if (line.Length > 0)
                    Console.Error.WriteLine(line);
            }
        }
    }

    /// <param name="live">False for redirected output: one line per phase, without instantaneous rates.</param>
    private string Format(BenchmarkSnapshot s, bool live)
    {
        string percent = _ansi.Dim($"[{s.OverallProgress * 100,3:F0}%]");
        string test = s.Spec is null ? string.Empty : $"{_ansi.Cyan($"{s.Mode,-5} {s.Spec.Name}")}  pass {s.Pass}/{s.PassCount}";
        if (!live)
        {
            return s.Phase switch
            {
                BenchmarkPhase.Preparing => $"{percent} Writing {ByteSize.Format(s.PhaseTotalBytes)} test file",
                BenchmarkPhase.Measuring => $"{percent} {test}",
                _ => string.Empty,
            };
        }

        return s.Phase switch
        {
            BenchmarkPhase.Probing => $"{percent} Probing target...",
            BenchmarkPhase.Preparing => string.Create(
                CultureInfo.InvariantCulture,
                $"{percent} Writing test file  {ByteSize.Format(s.Bytes)} / {ByteSize.Format(s.PhaseTotalBytes)}  {_rate,8:F1} MB/s"),
            BenchmarkPhase.Interval => string.Create(
                CultureInfo.InvariantCulture,
                $"{percent} {test}  interval {Math.Max(0, s.PhaseDurationSeconds - s.PhaseElapsedSeconds):F1} s"),
            BenchmarkPhase.Measuring => string.Create(
                CultureInfo.InvariantCulture,
                $"{percent} {test}  {_rate,9:F2} MB/s  {Math.Min(s.PhaseElapsedSeconds, s.PhaseDurationSeconds):F1}/{s.PhaseDurationSeconds:F1} s"),
            _ => string.Empty,
        };
    }

    private void ClearLine()
    {
        if (_interactive && _lineOpen)
        {
            Console.Error.Write(_ansi.ClearLine);
            _lineOpen = false;
        }
    }
}
