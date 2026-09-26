# DiskMarkNET

Cross-platform storage benchmark for Windows, Linux, and macOS, following the CrystalDiskMark methodology. It ships as a Native AOT command-line tool (`diskmark`) and an Avalonia desktop app (`DiskMarkNET`), both built on the same engine.

- Unbuffered direct I/O on every OS: `FILE_FLAG_NO_BUFFERING` (Windows), `O_DIRECT` (Linux), `F_NOCACHE` (macOS)
- CrystalDiskMark profiles: Default (SEQ1M Q8T1, SEQ1M Q1T1, RND4K Q32T1, RND4K Q1T1) and NVMe (adds SEQ128K Q32T1, RND4K Q32T16)
- Latency percentiles (p50 to p99.9) from a histogram with under 1% error
- Checks that the OS cache was actually bypassed, and warns about RAM-backed, network, and compressed filesystems
- Test files use random names and are removed even if the process is killed
- JSON output with a versioned schema, for CI regression tracking

## Install

Download an archive for your platform from [Releases](../../releases). Each release contains:

| Archive | Contents |
| :--- | :--- |
| `diskmark-cli-<version>-<rid>` | Single-file CLI (`diskmark`, `diskmark.exe`) |
| `DiskMarkNET-<version>-<rid>` | Desktop app plus its Skia/HarfBuzz native libraries |

Binaries are not code signed yet:
- **macOS:** remove the download quarantine, e.g. `xattr -dr com.apple.quarantine DiskMarkNET-*`.
- **Windows:** SmartScreen may ask you to confirm the first launch.

## CLI

```bash
diskmark run /path/on/the/disk                           # CrystalDiskMark defaults: 1 GiB, 5 passes, 5 s each
diskmark run . --size 64MiB --passes 1 --duration 1s --interval 0s   # quick check
diskmark run D:\ --profile nvme --json=results.json     # table on stdout, JSON to a file
diskmark run . --json > results.json                     # JSON only on stdout; progress goes to stderr
diskmark targets                                         # list testable locations
```

| Option | Default | Notes |
| :--- | :--- | :--- |
| `-p, --profile` | `default` | `default` or `nvme` |
| `-s, --size` | `1GiB` | Binary units; `MB`/`GB` mean MiB/GiB |
| `-n, --passes` | `5` | The best pass is reported, like CrystalDiskMark |
| `-d, --duration` | `5s` | Measurement time per pass |
| `-i, --interval` | `5s` | Pause before each measurement |
| `--warmup` | `0s` | Unmeasured I/O before each measurement |
| `-m, --mode` | `all` | `read`, `write`, or `all` |
| `--data` | `random` | `zeros` shows whether the drive compresses data |
| `--write-through` | off | Forces FUA writes; not CrystalDiskMark behavior |
| `--engine` | `auto` | `threaded` or `async`; see [Engines](#engines) |
| `--json[=path]` | off | JSON to stdout, or to a file alongside the table |

Exit codes: `0` success, `1` usage error, `2` target or filesystem error, `3` I/O error, `130` cancelled (Ctrl+C prints the passes finished so far).

## Desktop app

Pick a target (drive list or folder picker) and press **Start**. The score matrix fills in after each pass, and the active cell shows the live rate. The tabs show:
- **Latency:** histogram for the selected test (click any score cell)
- **Percentiles:** p50 to p99.9 for every test
- **Throughput:** live MB/s timeline
- **Drive:** filesystem and device details

**Save JSON** exports the same format as the CLI.

## Methodology

- **Test file:** written in full with incompressible random data before any read test, so reads hit real data on the device, not sparse extents.
- **Order:** all read tests, then all write tests. Each pass runs for the measure duration after an interval.
- **Scores:** the best pass is the headline; JSON also includes every pass and the median. Latency covers all passes combined.
- **Units:** MB/s = 1,000,000 bytes/s. Block and file sizes are binary (4 KiB, 1 MiB, 1 GiB).
- **Write cache:** writes go through the drive's write cache by default, as in CrystalDiskMark. Use `--write-through` to measure durable writes.
- **Cache checks:** results are flagged when:
  - read throughput exceeds any single-drive interface (16 GB/s)
  - RND4K Q1T1 read latency is under 5 µs
  - more than 5% of the test file is in the OS page cache (Linux, macOS)

### Engines

| Engine | Used by default on | How queue depth works |
| :--- | :--- | :--- |
| `async` | Windows | Overlapped I/O through IOCP; QD requests in flight per thread |
| `threaded` | Linux, macOS | One dedicated thread per outstanding I/O (Q32T1 = 32 threads) |

.NET has no native async file I/O on Linux or macOS, so `threaded` avoids thread-pool ramp-up and scheduling jitter there.

## Building

Requires the .NET SDK pinned in `global.json` (10.0.301 or a later feature band). Native AOT also needs:
- Linux: `clang` and `zlib1g-dev`
- macOS: Xcode command line tools
- Windows: the Visual Studio "Desktop development with C++" workload

```bash
dotnet build DiskMark.slnx
dotnet publish src/DiskMark.Cli/DiskMark.Cli.csproj -c Release -r linux-x64
dotnet publish src/DiskMark.Desktop/DiskMark.Desktop.csproj -c Release -r linux-x64
```

Native AOT builds for the current OS only.

## Testing

```bash
dotnet test --solution DiskMark.slnx
```

Integration tests write real files to `DISKMARK_TEST_DIR` (default: `.testdata/` in the repository). They skip themselves on filesystems that cannot give trustworthy direct I/O, such as tmpfs. The desktop tests run headless and save rendered frames under `tests/DiskMark.Desktop.Tests/bin/**/frames/`.

## Releasing

Push a version tag. `.github/workflows/release.yml` publishes Native AOT builds for linux-x64, linux-arm64, win-x64, osx-arm64, and osx-x64, and attaches them with `SHA256SUMS.txt` to a GitHub release. Tags containing `-` (e.g. `v1.0.0-beta.1`) become prereleases.

```bash
git tag v0.1.0
git push origin v0.1.0
```

## Layout

| Path | Purpose |
| :--- | :--- |
| `src/DiskMark.Core` | Engine, direct I/O interop, histogram, hardware probes (no dependencies) |
| `src/DiskMark.Cli` | `diskmark` CLI |
| `src/DiskMark.Desktop` | Avalonia desktop app |
| `tests/` | xUnit v3 tests: Core (unit and real-disk), CLI parser, headless desktop |

## License

[MIT](LICENSE)
