using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DiskMark.Desktop.Controls;

/// <summary>Scrolling throughput timeline (MB/s) sampled while a benchmark runs.</summary>
public sealed class ThroughputSparklineCanvas : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> SamplesProperty =
        AvaloniaProperty.Register<ThroughputSparklineCanvas, IReadOnlyList<double>?>(nameof(Samples));

    public static readonly StyledProperty<int> CapacityProperty =
        AvaloniaProperty.Register<ThroughputSparklineCanvas, int>(nameof(Capacity), 300);

    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<ThroughputSparklineCanvas, IBrush?>(nameof(LineBrush), Brushes.SteelBlue);

    public static readonly StyledProperty<IBrush?> FillBrushProperty =
        AvaloniaProperty.Register<ThroughputSparklineCanvas, IBrush?>(nameof(FillBrush));

    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<ThroughputSparklineCanvas, IBrush?>(nameof(TextBrush), Brushes.Gray);

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<ThroughputSparklineCanvas, IBrush?>(nameof(GridBrush), Brushes.DimGray);

    private const double PlotLeft = 60;
    private const double PlotTop = 12;
    private const double PlotBottom = 12;
    private const double PlotRight = 8;

    static ThroughputSparklineCanvas()
    {
        AffectsRender<ThroughputSparklineCanvas>(SamplesProperty, CapacityProperty, LineBrushProperty, FillBrushProperty, TextBrushProperty, GridBrushProperty);
    }

    public IReadOnlyList<double>? Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public int Capacity
    {
        get => GetValue(CapacityProperty);
        set => SetValue(CapacityProperty, value);
    }

    public IBrush? LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public IBrush? FillBrush
    {
        get => GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
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
        var bounds = Bounds;
        var textBrush = TextBrush ?? Brushes.Gray;
        var plot = new Rect(PlotLeft, PlotTop, Math.Max(10, bounds.Width - PlotLeft - PlotRight), Math.Max(10, bounds.Height - PlotTop - PlotBottom));
        var samples = Samples ?? [];
        double max = samples.Count == 0 ? 1 : Math.Max(1, samples.Max() * 1.15);

        var gridPen = new Pen(GridBrush ?? Brushes.DimGray, 1);
        for (int i = 0; i <= 4; i++)
        {
            double y = plot.Bottom - (plot.Height * i / 4);
            context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = ChartText.Create(this, $"{max * i / 4:N0}", textBrush, 10);
            context.DrawText(label, new Point(plot.Left - label.Width - 6, y - (label.Height / 2)));
        }

        var unit = ChartText.Create(this, "MB/s", textBrush, 10);
        context.DrawText(unit, new Point(4, PlotTop - 2));

        if (samples.Count < 2)
            return;

        int capacity = Math.Max(Capacity, samples.Count);
        double step = plot.Width / (capacity - 1);
        double startX = plot.Right - ((samples.Count - 1) * step);

        Point At(int index) => new(startX + (index * step), plot.Bottom - (samples[index] / max * plot.Height));

        var fill = new StreamGeometry();
        using (var ctx = fill.Open())
        {
            ctx.BeginFigure(new Point(startX, plot.Bottom), true);
            for (int i = 0; i < samples.Count; i++)
                ctx.LineTo(At(i));
            ctx.LineTo(new Point(plot.Right, plot.Bottom));
            ctx.EndFigure(true);
        }

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(At(0), false);
            for (int i = 1; i < samples.Count; i++)
                ctx.LineTo(At(i));
            ctx.EndFigure(false);
        }

        if (FillBrush is { } fillBrush)
            context.DrawGeometry(fillBrush, null, fill);
        context.DrawGeometry(null, new Pen(LineBrush ?? Brushes.SteelBlue, 2, lineJoin: PenLineJoin.Round), line);
    }
}
