using DiskMark.Core.Hardware;

namespace DiskMark.Core.Tests;

/// <summary>
/// Where integration tests create real test files: DISKMARK_TEST_DIR, or .testdata/ in the repository.
/// The system temp directory is avoided because it is RAM-backed (tmpfs) on many Linux systems.
/// </summary>
internal static class TestDirectory
{
    /// <summary>
    /// Returns a fresh subdirectory per call. On Windows an open DeleteOnClose file stays visible until its handle
    /// closes, so tests (and test projects running in parallel) must not share a directory for leftover checks.
    /// </summary>
    public static string Get()
    {
        string root = Environment.GetEnvironmentVariable("DISKMARK_TEST_DIR") is { Length: > 0 } configured
            ? configured
            : Path.Combine(FindRepositoryRoot(), ".testdata");
        string directory = Path.Combine(root, "core-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Skips the calling test when the directory's filesystem cannot give trustworthy direct I/O.</summary>
    public static string GetForDirectIo()
    {
        string directory = Get();
        var fileSystem = FileSystemProbe.Probe(directory);
        if (fileSystem.Support != FileSystemSupport.Supported)
            Assert.Skip($"{directory} is on {fileSystem.Type} ({fileSystem.Support}); set DISKMARK_TEST_DIR to a local disk.");
        return directory;
    }

    public static string[] LeftoverTestFiles(string directory) => Directory.GetFiles(directory, "DiskMarkNET-*.tmp");

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
