namespace DiskMark.Cli;

internal static class HelpText
{
    public const string Text = """
        diskmark: cross-platform direct I/O storage benchmark (CrystalDiskMark methodology)

        Usage:
          diskmark run <directory> [options]   Benchmark the disk holding <directory>
          diskmark targets                     List storage locations that can be tested
          diskmark profiles                    List test profiles
          diskmark --version | --help

        Run options:
          -p, --profile <name>     default | nvme                           (default: default)
          -s, --size <size>        Test file size, binary units: 64MiB, 1GiB (default: 1GiB)
          -n, --passes <n>         Passes per test; the best pass is reported (default: 5)
          -d, --duration <time>    Measurement time per pass: 5s, 500ms       (default: 5s)
          -i, --interval <time>    Pause before each measurement              (default: 5s)
              --warmup <time>      Unmeasured I/O before each measurement     (default: 0s)
          -m, --mode <mode>        read | write | all                         (default: all)
              --data <pattern>     random | zeros                             (default: random)
              --write-through      Bypass the drive's write cache (not CrystalDiskMark behavior)
              --engine <name>      auto | threaded | async                    (default: auto)
              --json[=<path>]      Write JSON to stdout, or to <path> alongside the table
              --no-color           Disable colors (NO_COLOR is also honored)

        Sizes treat MB/GB as MiB/GiB. Throughput is decimal: 1 MB/s = 1,000,000 bytes/s.
        The test file is created with a random name and removed automatically, even if the process is killed.

        Exit codes: 0 success, 1 usage error, 2 target or filesystem error, 3 I/O error, 130 cancelled.

        """;
}
