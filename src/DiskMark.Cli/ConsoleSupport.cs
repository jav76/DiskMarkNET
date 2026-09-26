using System.Runtime.InteropServices;
using System.Text;

namespace DiskMark.Cli;

/// <summary>Terminal capability checks: ANSI colors and whether "µ" renders.</summary>
internal static partial class ConsoleSupport
{
    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_ERROR_HANDLE = -12;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    public static bool StdoutSupportsColor(bool noColorFlag) =>
        !noColorFlag && !Console.IsOutputRedirected && NoColorUnset() && TryEnableVirtualTerminal(STD_OUTPUT_HANDLE);

    public static bool StderrSupportsColor(bool noColorFlag) =>
        !noColorFlag && !Console.IsErrorRedirected && NoColorUnset() && TryEnableVirtualTerminal(STD_ERROR_HANDLE);

    public static string MicroSymbol =>
        Console.OutputEncoding.CodePage == Encoding.UTF8.CodePage ? "µs" : "us";

    private static bool NoColorUnset() => string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

    private static bool TryEnableVirtualTerminal(int standardHandle)
    {
        if (!OperatingSystem.IsWindows())
            return true;

        nint handle = GetStdHandle(standardHandle);
        if (handle == 0 || handle == -1 || !GetConsoleMode(handle, out uint mode))
            return false;

        return (mode & ENABLE_VIRTUAL_TERMINAL_PROCESSING) != 0 || SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetStdHandle(int standardHandle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(nint handle, out uint mode);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(nint handle, uint mode);
}

internal sealed class Ansi(bool enabled)
{
    public string Bold(string text) => Wrap(text, "1");

    public string Dim(string text) => Wrap(text, "2");

    public string Cyan(string text) => Wrap(text, "36");

    public string Green(string text) => Wrap(text, "32");

    public string Yellow(string text) => Wrap(text, "33");

    public string Red(string text) => Wrap(text, "31");

    public string ClearLine => enabled ? "\r\e[2K" : "\r";

    private string Wrap(string text, string code) => enabled ? $"\e[{code}m{text}\e[0m" : text;
}
