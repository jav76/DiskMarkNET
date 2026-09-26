using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DiskMark.Core.Common;

namespace DiskMark.Desktop.Controls;

/// <summary>Log-scale latency histogram with p50/p95/p99 markers, drawn directly with DrawingContext.</summary>
public sealed class LatencyHistogramCanvas : Control
{
    public static readonly StyledProperty<IReadOnlyList<HistogramBucket>?> BucketsProperty =
        AvaloniaProperty.Register<LatencyHistogramCanvas, IReadOnlyList<HistogramBucket>?>(nameof(Buckets));

    public static readonly StyledProperty<LatencySummary?> LatencyProperty =
        AvaloniaProperty.Register<LatencyHistogramCanvas, LatencySummary?>(nameof(Latency));

    public static readonly StyledProperty<IBrush?> BarBrushProperty =
        AvaloniaProperty.Register<LatencyHistogramCanvas, IBrush?>(nameof(BarBrush), Brushes.SteelBlue);

    public static readonly StyledProperty<IBrush?> MarkerBrushProperty =
        AvaloniaProperty.Register<LatencyHistogramCanvas, IBrush?>(nameof(MarkerBrush), Brushes.HotPink);

    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<LatencyHistogramCanvas, IBrush?>(nameof(TextBrush), Brushes.Gray);

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<LatencyHistogramCanvas, IBrush?>(nameof(GridBrush), Brushes.DimGray);

    private const double PlotLeft = 52;
    private const double PlotRight = 16;
    private const double PlotTop = 54;
    private const double PlotBottom = 30;

    static LatencyHistogramCanvas()
    {
        AffectsRender<LatencyHistogramCanvas>(BucketsProperty, LatencyProperty, BarBrushProperty, MarkerBrushProperty, TextBrushProperty, GridBrushProperty);
    }

    public IReadOnlyList<HistogramBucket>? Buckets
    {
        get => GetValue(BucketsProperty);
        set => SetValue(BucketsProperty, value);
    }

    public LatencySummary? Latency
    {
        get => GetValue(LatencyProperty);
        set => SetValue(LatencyProperty, value);
    }

    public IBrush? BarBrush
    {
        get => GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    public IBrush? MarkerBrush
    {
        get => GetValue(MarkerBrushProperty);
        set => SetValue(MarkerBrushProperty, value);
    }

    public IBrush? TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var textBrush = TextBrush ?? Brushes.Gray;
        var buckets = Buckets;
        var bounds = Bounds;
        if (buckets is null || buckets.Count == 0)
        {
            var empty = ChartText.Create(this, "Select a completed test to see its latency distribution", textBrush, 13);
            context.DrawText(empty, new Point((bounds.Width - empty.Width) / 2, (bounds.Height - empty.Height) / 2));
            return;
        }

        var plot = new Rect(PlotLeft, PlotTop, Math.Max(10, bounds.Width - PlotLeft - PlotRight), Math.Max(10, bounds.Height - PlotTop - PlotBottom));
        double minLog = Math.Log10(buckets[0].LowerUs);
        double maxLog = Math.Log10(buckets[^1].UpperUs);
        if (maxLog - minLog < 1e-9)
            maxLog = minLog + 1;
        long maxCount = Math.Max(1, buckets.Max(b => b.Count));

        double X(double us) => plot.Left + ((Math.Log10(us) - minLog) / (maxLog - minLog) * plot.Width);

        var gridPen = new Pen(GridBrush ?? Brushes.DimGray, 1);
        for (int i = 1; i <= 4; i++)
        {
            double y = plot.Bottom - (plot.Height * i / 4);
            context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = ChartText.Create(this, $"{maxCount * i / 4:N0}", textBrush, 10);
            context.DrawText(label, new Point(plot.Left - label.Width - 6, y - (label.Height / 2)));
        }

        var barBrush = BarBrush ?? Brushes.SteelBlue;
        foreach (var bucket in buckets)
        {
            if (bucket.Count == 0)
                continue;
            double x0 = X(bucket.LowerUs);
            double x1 = X(bucket.UpperUs);
            double height = bucket.Count / (double)maxCount * plot.Height;
            context.DrawRectangle(barBrush, null, new Rect(x0 + 0.5, plot.Bottom - height, Math.Max(1, x1 - x0 - 1), height), 2, 2);
        }

        var axisPen = new Pen(textBrush, 1);
        context.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));

        // Narrow ranges get 1-2-5 ticks so the axis always has several labels.
        double[] steps = maxLog - minLog <= 2 ? [1, 2, 5] : [1];
        double lastLabelRight = double.NegativeInfinity;
        for (int decade = (int)Math.Floor(minLog); decade <= (int)Math.Ceiling(maxLog); decade++)
        {
            foreach (double step in steps)
            {
                double us = step * Math.Pow(10, decade);
                double log = Math.Log10(us);
                if (log < minLog || log > maxLog)
                    continue;

                double x = X(us);
                var label = ChartText.Create(this, ChartText.Microseconds(us), textBrush, 10);
                double labelLeft = x - (label.Width / 2);
                if (labelLeft < lastLabelRight + 6)
                    continue;

                context.DrawLine(axisPen, new Point(x, plot.Bottom), new Point(x, plot.Bottom + 4));
                context.DrawText(label, new Point(labelLeft, plot.Bottom + 6));
                lastLabelRight = labelLeft + label.Width;
            }
        }

        if (Latency is { Samples: > 0 } latency)
        {
            var markerBrush = MarkerBrush ?? Brushes.HotPink;
            var markerPen = new Pen(markerBrush, 1.5, new DashStyle([4, 3], 0));
            (string Name, double Value)[] markers = [("p50", latency.P50Us), ("p95", latency.P95Us), ("p99", latency.P99Us)];
            for (int i = 0; i < markers.Length; i++)
            {
                double x = Math.Clamp(X(Math.Max(markers[i].Value, 0.001)), plot.Left, plot.Right);
                double labelY = 4 + (i * 16);
                context.DrawLine(markerPen, new Point(x, labelY + 14), new Point(x, plot.Bottom));
                var label = ChartText.Create(this, $"{markers[i].Name} {ChartText.Microseconds(markers[i].Value)}", markerBrush, 11);
                double labelX = Math.Clamp(x - (label.Width / 2), 0, Math.Max(0, bounds.Width - label.Width));
                context.DrawText(label, new Point(labelX, labelY));
            }
        }
    }
}
