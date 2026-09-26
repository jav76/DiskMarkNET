namespace DiskMark.Core.Common;

public enum AccessPattern
{
    Sequential,
    Random,
}

public enum IoMode
{
    Read,
    Write,
}

public enum DataPattern
{
    Random,
    Zeros,
}

public enum BenchmarkPhase
{
    Idle,
    Probing,
    Preparing,
    Interval,
    Measuring,
    Completed,
    Cancelled,
    Failed,
}
