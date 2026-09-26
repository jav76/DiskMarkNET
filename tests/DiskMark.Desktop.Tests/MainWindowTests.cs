using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using DiskMark.Core.Common;
using DiskMark.Desktop.ViewModels;
using DiskMark.Desktop.Views;

namespace DiskMark.Desktop.Tests;

public class MainWindowTests
{
    [AvaloniaFact]
    public void Window_ShowsDefaultMatrix()
    {
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();

        Assert.Equal(BenchmarkProfile.Default.Tests.Count, vm.Items.Count);
        Assert.All(vm.Items, item => Assert.Equal("0.00", item.ReadText));
        Assert.NotNull(window.CaptureRenderedFrame());
    }

    [AvaloniaFact]
    public void ChangingProfile_RebuildsMatrix()
    {
        var vm = new MainViewModel { SelectedProfile = BenchmarkProfile.Nvme };
        Assert.Equal(["SEQ1M", "SEQ128K", "RND4K", "RND4K"], vm.Items.Select(i => i.Title));
        Assert.Equal("Q32T16", vm.Items[2].Subtitle);
    }

    /// <summary>Runs a real short benchmark through the view model and window, saving frames to the output directory.</summary>
    [AvaloniaFact]
    public async Task ShortBenchmark_FillsMatrixAndCharts()
    {
        string directory = Environment.GetEnvironmentVariable("DISKMARK_TEST_DIR") is { Length: > 0 } configured
            ? configured
            : Path.Combine(FindRepositoryRoot(), ".testdata");
        Directory.CreateDirectory(directory);
        string frames = Path.Combine(AppContext.BaseDirectory, "frames");
        Directory.CreateDirectory(frames);

        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 780 };
        window.Show();
        vm.TargetDirectory = directory;
        vm.SelectedFileSize = vm.FileSizes.First(o => o.Value == 16 * ByteSize.MiB);
        vm.SelectedPasses = vm.PassOptions.First(o => o.Value == 1);
        vm.SelectedDuration = vm.DurationOptions.First(o => o.Value == 1);
        vm.SelectedInterval = vm.IntervalOptions.First(o => o.Value == 0);

        var run = vm.StartCommand.ExecuteAsync(null);
        bool savedMidRun = false;
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (!run.IsCompleted && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            if (!savedMidRun && vm.CompletedResults.Count >= 3)
            {
                window.CaptureRenderedFrame()?.Save(Path.Combine(frames, "running.png"), PngBitmapEncoderOptions.Default);
                savedMidRun = true;
            }
        }

        await run;
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(vm.LastResult);
        Assert.True(vm.LastResult.Completed, vm.StatusText);
        Assert.Equal(8, vm.CompletedResults.Count);
        Assert.All(vm.Items, item =>
        {
            Assert.NotEqual("0.00", item.ReadText);
            Assert.NotEqual("0.00", item.WriteText);
        });
        Assert.NotNull(vm.SelectedResult);
        Assert.NotEmpty(vm.SelectedResult.Histogram);
        Assert.Contains(vm.Drive.Rows, r => r.Label == "Filesystem");
        Assert.False(vm.IsRunning);

        window.CaptureRenderedFrame()?.Save(Path.Combine(frames, "completed.png"), PngBitmapEncoderOptions.Default);
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Save(Path.Combine(frames, "completed-dark.png"), PngBitmapEncoderOptions.Default);
        Application.Current.RequestedThemeVariant = ThemeVariant.Default;
        vm.SelectedUnit = vm.UnitOptions.First(o => o.Value == DisplayUnit.Iops);
        Assert.All(vm.Items, item => Assert.NotEqual("0.00", item.ReadText));
    }

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DiskMark.slnx")))
                return dir.FullName;
        }

        return Directory.GetCurrentDirectory();
    }
}
