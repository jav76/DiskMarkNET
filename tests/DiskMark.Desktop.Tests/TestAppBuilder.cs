using Avalonia;
using Avalonia.Headless;
using DiskMark.Desktop;

[assembly: AvaloniaTestApplication(typeof(DiskMark.Desktop.Tests.TestAppBuilder))]

namespace DiskMark.Desktop.Tests;

public static class TestAppBuilder
{
    // Skia rendering (not headless drawing) so tests can capture real frames.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
