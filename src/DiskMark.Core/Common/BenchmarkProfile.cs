namespace DiskMark.Core.Common;

public sealed record BenchmarkProfile(string Name, string Description, IReadOnlyList<TestSpec> Tests)
{
    public static BenchmarkProfile Default { get; } = new(
        "default",
        "CrystalDiskMark default profile",
        [
            new(AccessPattern.Sequential, (int)ByteSize.MiB, 8, 1),
            new(AccessPattern.Sequential, (int)ByteSize.MiB, 1, 1),
            new(AccessPattern.Random, 4 * (int)ByteSize.KiB, 32, 1),
            new(AccessPattern.Random, 4 * (int)ByteSize.KiB, 1, 1),
        ]);

    public static BenchmarkProfile Nvme { get; } = new(
        "nvme",
        "CrystalDiskMark NVMe SSD profile",
        [
            new(AccessPattern.Sequential, (int)ByteSize.MiB, 8, 1),
            new(AccessPattern.Sequential, 128 * (int)ByteSize.KiB, 32, 1),
            new(AccessPattern.Random, 4 * (int)ByteSize.KiB, 32, 16),
            new(AccessPattern.Random, 4 * (int)ByteSize.KiB, 1, 1),
        ]);

    public static IReadOnlyList<BenchmarkProfile> All { get; } = [Default, Nvme];

    public static BenchmarkProfile? Find(string name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public override string ToString() => Name;
}
