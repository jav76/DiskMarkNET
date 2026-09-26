using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DiskMark.Desktop.ViewModels;

namespace DiskMark.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => ApplyGlass();
        Closing += (_, _) =>
        {
            // The test file is removed by the OS when the process exits, so closing mid-run is safe.
            if (DataContext is MainViewModel { IsRunning: true } vm)
                vm.StopCommand.Execute(null);
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainViewModel vm)
        {
            vm.PickFolder = PickFolderAsync;
            vm.SaveFile = SaveFileAsync;
        }
    }

    /// <summary>Uses a translucent background only where the platform actually provides blur (Windows, macOS).</summary>
    private void ApplyGlass()
    {
        var level = ActualTransparencyLevel;
        Classes.Set("glass", level == WindowTransparencyLevel.AcrylicBlur || level == WindowTransparencyLevel.Mica || level == WindowTransparencyLevel.Blur);
    }

    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a directory on the disk to benchmark",
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private async Task SaveFileAsync(string suggestedName, string content)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save benchmark results",
            SuggestedFileName = suggestedName,
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
        });
        if (file is null)
            return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);
    }
}
