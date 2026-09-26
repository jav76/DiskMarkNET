namespace DiskMark.Desktop.ViewModels;

/// <summary>A selectable value with a display label; ComboBoxes render it through ToString.</summary>
public sealed record Option<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

public enum DisplayUnit
{
    MegabytesPerSecond,
    GigabytesPerSecond,
    Iops,
    Microseconds,
}

public enum ModeSelection
{
    All,
    Read,
    Write,
}
