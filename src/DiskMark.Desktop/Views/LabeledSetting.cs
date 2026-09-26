using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Metadata;

namespace DiskMark.Desktop.Views;

/// <summary>A small caption stacked above a setting control.</summary>
public sealed class LabeledSetting : StackPanel
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<LabeledSetting, string?>(nameof(Label));

    private readonly TextBlock _caption = new() { Classes = { "caption" } };

    public LabeledSetting()
    {
        Spacing = 4;
        Margin = new Thickness(0, 0, 14, 0);
        Orientation = Orientation.Vertical;
        Children.Add(_caption);
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    [Content]
    public Control? Setting
    {
        get => Children.Count > 1 ? Children[1] : null;
        set
        {
            while (Children.Count > 1)
                Children.RemoveAt(1);
            if (value is not null)
                Children.Add(value);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LabelProperty)
            _caption.Text = change.GetNewValue<string?>();
    }
}
