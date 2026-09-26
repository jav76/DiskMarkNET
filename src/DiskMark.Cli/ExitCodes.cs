namespace DiskMark.Cli;

internal static class ExitCodes
{
    public const int Success = 0;
    public const int Usage = 1;
    public const int Target = 2;
    public const int Io = 3;
    public const int Cancelled = 130;
}
