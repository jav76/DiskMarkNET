# Implementation Plan: DiskMarkNET (.NET 10 Native AOT Direct I/O Benchmark & Modern Avalonia Dashboard)

A cross-platform storage benchmarking suite and analytics dashboard built on .NET 10 with full Native AOT compilation. It delivers unbuffered direct hardware I/O matching CrystalDiskMark (CDM) methodologies across Windows, Linux, and macOS, paired with a modern Avalonia UI featuring real-time latency histograms and percentile telemetry.

---

## Implementation Status

Implemented as planned, with the deviations listed below.

**Verified on Linux x64** (Debian 13, ext4, SATA SSD):
- 106 tests pass: Core unit and real-disk tests, CLI parser tests, and headless desktop tests.
- Native AOT publish of both apps produces zero trim/AOT warnings. The CLI is 3.4 MB; the desktop app is 23.5 MB plus Skia/HarfBuzz libraries.
- Results match fio (libaio, direct=1) within ±6% on every test compared:

  | Test | DiskMarkNET | fio |
  | :--- | :--- | :--- |
  | SEQ1M Q8T1 read | 505 MB/s | 530 MB/s |
  | RND4K Q32T1 read | 267 MB/s | 265 MB/s |
  | RND4K Q1T1 read | 21.98 MB/s | 21.86 MB/s |
  | RND4K Q32T1 write | 284 MB/s | 283 MB/s |
  | RND4K Q1T1 write | 131 MB/s | 124 MB/s |

- On tmpfs, all cache warnings fire, including 100% page-cache residency.
- After `kill -9` mid-run, no test file remains and free space returns fully.

**Verified by CI only** (no local hardware): Windows `NO_BUFFERING` pass-through, macOS arm64 `F_NOCACHE` (via the page-cache residency test), and Linux arm64 `O_DIRECT`.

**Deviations from the original plan:**
- **No `ViewLocator`:** views are composed directly, so no ViewModel-to-View lookup is needed.
- **`IocpIoEngine` is named `AsyncIoEngine`.** It is IOCP-backed on Windows. Either engine can be forced with `--engine`.
- **No `GC.TryStartNoGCRegion`:** the threaded hot path is allocation-free.
- **UI polling is 5 Hz** (not 10 Hz), and there are no gauge animations to pause.
- **The score matrix is always visible.** The tabs are Latency, Percentiles, Throughput, and Drive.
- **The page-cache residency check also runs on macOS** (`mincore`), both as a runtime warning and as a test.
- **Added:**
  - a `diskmark targets` command, plus `--mode` and `--engine` options
  - a `DiskMark.Desktop.Tests` project (Avalonia headless)
  - `release.yml`
- **xUnit v3 is pinned to 3.2.2** on Microsoft Testing Platform, because `Avalonia.Headless.XUnit` 12.x fails at runtime with xUnit v3 4.x.
- **License:** MIT (from the repository's initial commit).

---

## User Review Required

The following architectural decisions have been established through the design interview and plan review:

1. **Solution Structure**: Multi-project solution (`DiskMark.Core`, `DiskMark.Cli`, `DiskMark.Desktop`) in `DiskMark.slnx`.
2. **I/O Engine**: Unbuffered direct I/O on every OS (`FILE_FLAG_NO_BUFFERING` on Windows, `O_DIRECT` on Linux, `F_NOCACHE` on macOS) over `SafeFileHandle`s created with `File.OpenHandle`, using page/sector-aligned native memory. Queue depth model per Decision 1.
3. **UI Layout**: Detailed Analytics Dashboard featuring the classic CDM 4x2 matrix plus multi-tab latency histograms, percentile distributions, and real-time sparklines.
4. **Charting Pipeline**: Custom controls rendered through Avalonia's `DrawingContext` (backend-agnostic, no direct Skia usage, zero external charting dependencies, trim-safe under Native AOT).
5. **Storage Safety**: File-based benchmarking only in v1. Raw block device mode is deferred to Phase 2 (CLI-only, read-only first). See [Deferred: Raw Block Device Mode](#deferred-raw-block-device-mode-phase-2).

### Design Review: CLI Argument Parser

| Option | Pros | Cons | Design Considerations |
| :--- | :--- | :--- | :--- |
| **Option A: Minimal Zero-Dependency Handcrafted Parser (Recommended)** | • Zero reflection or trimming risk<br>• Zero external package dependencies<br>• Instant cold start in Native AOT<br>• Small binary footprint (<5MB) | • Subcommand parsing and help text formatting must be implemented manually | • Simple, robust, and immune to future .NET trimmer changes |
| **Option B: System.CommandLine (Microsoft)** | • Standardized POSIX/GNU argument syntax and automatic help generation | • Historically heavy dependency with frequent API changes and trimming considerations across preview releases | • Adds external dependency footprint to a lightweight CLI binary |

**Selected: Option A.** The CLI needs only a `run <target>` subcommand plus flags like `--profile`, `--size`, `--passes`, and `--json`. The parser lives in its own class so it can be unit tested.

### Design Review: Queue Depth / I/O Engine (Decision 1)

.NET's `RandomAccess` async APIs only do true async I/O on Windows handles opened as overlapped. On Linux and macOS they block thread-pool threads. The thread pool ramps up slowly past its minimum, so relying on it under-drives Q32 for the first seconds of each test.

| Option | Pros | Cons | Design Considerations |
| :--- | :--- | :--- | :--- |
| **A: Hybrid (Recommended).** IOCP on Windows (`RandomAccess` async over an overlapped handle); dedicated worker threads (one per outstanding I/O) on Linux/macOS | • True async QD on Windows almost for free<br>• No thread-pool ramp-up<br>• Portable and simple | • On Unix, Q32 = 32 threads; the "T1" label is not literal<br>• Context switches cap very high IOPS | • Behind an `IIoEngine` interface<br>• io_uring can be added later without API changes |
| **B: Native async everywhere** (IOCP plus io_uring; threads only on macOS) | • True QD from one thread, like fio/DiskSpd<br>• Highest IOPS | • io_uring P/Invoke layer (rings, SQE/CQE) is substantial<br>• Disabled on some systems (`io_uring_disabled`, container seccomp) | • Needs a fallback path anyway<br>• Larger test surface |
| **C: `RandomAccess` async as-is** plus a higher `ThreadPool` minimum | • Least code | • Pool scheduling jitter shows up in latency<br>• Still needs the overlapped handle on Windows | • Hard to reason about and tune |

**Selected: Option A.**

### Design Review: Default Write Semantics (Decision 2)

CDM runs DiskSpd with `-S` (software cache disabled) but without write-through. `FILE_FLAG_WRITE_THROUGH` and `O_SYNC` force FUA/flush on every write.

| Option | Pros | Cons | Design Considerations |
| :--- | :--- | :--- | :--- |
| **A: CDM parity (Recommended).** No write-through by default; `--write-through` opt-in | • Numbers comparable with CDM and DiskSpd `-S`<br>• Matches the stated goal | • Measures drive write cache behavior, not durable writes | • The mode is recorded in the result JSON |
| **B: Always write-through** | • Measures durable write latency | • Not comparable with CDM<br>• Heavy FUA penalty on consumer SSDs | • Contradicts "matching CDM methodologies" |

**Selected: Option A.**

### Design Review: Score Aggregation Across Passes (Decision 3)

| Option | Pros | Cons | Design Considerations |
| :--- | :--- | :--- | :--- |
| **A: Max headline plus all passes stored (Recommended)** | • CDM parity in the UI and table<br>• JSON keeps per-pass values and the median for CI | • Two numbers to explain | • JSON carries `passes[]`, `max`, `median` |
| **B: Median only** | • Most stable for regression testing | • Not comparable with CDM | • Loses parity |
| **C: Max only** | • Simplest | • Noisy for CI regression use | • Drops information |

**Selected: Option A.**

### Design Review: macOS `fcntl(F_NOCACHE)` on arm64 (Decision 4)

`fcntl` is variadic. On Apple arm64, variadic arguments are passed on the stack, but a normal P/Invoke passes them in registers. A plain `fcntl(fd, F_NOCACHE, 1)` can silently fail to disable the cache.

| Option | Pros | Cons | Design Considerations |
| :--- | :--- | :--- | :--- |
| **A: Padded P/Invoke on osx-arm64 (Recommended).** Six dummy `nint` arguments fill x2-x7 so the value lands on the stack | • No native code<br>• Only one call site | • ABI-specific trick; needs a comment and a test | • A separate declaration per architecture, selected at runtime |
| **B: Tiny C shim library** built for each RID | • Correct by construction | • Native toolchain in every build<br>• Static-link setup for AOT | • More build complexity than one call justifies |

**Selected: Option A.**

---

## Resolved Questions

### Distribution Scope for v1

| Option | Pros | Cons | Design Considerations |
| :--- | :--- | :--- | :--- |
| **A: Out of scope for v1; CI uploads raw AOT publish outputs (Recommended)** | • Keeps focus on engine accuracy<br>• No signing secrets needed | • macOS Gatekeeper blocks unsigned downloads<br>• Windows SmartScreen warns | • Packaging can be added later without code changes |
| **B: Full packaging in v1** (`.app` + notarization, AppImage/Flatpak, signed Windows exe) | • Smooth install on every OS | • Needs Apple Developer account and code signing certificates<br>• Extra CI complexity | • Adds secrets management to CI |

**Selected: Option A, extended.** `.github/workflows/release.yml` publishes Native AOT builds on version tags (`v*`) and attaches the archives plus `SHA256SUMS.txt` to a GitHub release. Code signing is out of scope for now.

---

## Proposed Architecture

```
DiskMarkNET/
├── DiskMark.slnx
├── global.json                        # SDK 10.0.301, rollForward: latestFeature
├── Directory.Build.props              # Nullable, ImplicitUsings, TreatWarningsAsErrors
├── Directory.Packages.props           # Central package versions
├── .editorconfig / .gitignore / LICENSE (MIT) / README.md
├── .github/workflows/ci.yml           # OS/arch matrix: build, test, AOT publish, smoke run
├── .github/workflows/release.yml      # Tag-triggered AOT builds attached to a GitHub release
├── src/
│   ├── DiskMark.Core/                 # Pure benchmark engine & direct I/O abstractions
│   │   ├── DiskMark.Core.csproj
│   │   ├── Common/                    # TestSpec, profiles, options, results, JSON context
│   │   ├── Hardware/                  # Target discovery, filesystem probe, device info
│   │   ├── Interop/                   # [LibraryImport] declarations for Win32, Linux, macOS
│   │   ├── Memory/                    # AlignedBuffer (MemoryManager<byte>), alignment detection
│   │   ├── Telemetry/                 # Log-linear latency histogram, live counters, snapshots
│   │   └── Engine/                    # BenchmarkRunner, IIoEngine implementations, TestFile
│   │
│   ├── DiskMark.Cli/                  # Headless cross-platform CLI runner
│   │   ├── DiskMark.Cli.csproj        # <PublishAot>true</PublishAot>
│   │   ├── Program.cs                 # Binary name: diskmark
│   │   ├── CommandLineParser.cs       # Handcrafted parser producing CommandLineOptions
│   │   ├── CommandLineOptions.cs
│   │   ├── OutputFormatter.cs         # ANSI terminal tables & JSON output
│   │   └── ProgressReporter.cs        # 5 Hz progress on stderr
│   │
│   └── DiskMark.Desktop/              # Modern Avalonia UI & telemetry dashboard
│       ├── DiskMark.Desktop.csproj    # <PublishAot>true</PublishAot>
│       ├── Program.cs
│       ├── App.axaml & App.axaml.cs   # Binary name: DiskMarkNET
│       ├── Controls/                  # Custom DrawingContext controls
│       │   ├── LatencyHistogramCanvas.cs
│       │   └── ThroughputSparklineCanvas.cs
│       ├── ViewModels/                # CommunityToolkit.Mvvm source-generated ViewModels
│       │   ├── MainViewModel.cs
│       │   ├── BenchmarkItemViewModel.cs
│       │   └── DriveInfoViewModel.cs
│       ├── Views/
│       │   ├── MainWindow.axaml & MainWindow.axaml.cs
│       │   ├── DashboardTabs.axaml
│       │   └── LabeledSetting.cs
│       └── Styles/                    # Dark/light design system with glass effects
│
└── tests/
    ├── DiskMark.Core.Tests/
    │   ├── DiskMark.Core.Tests.csproj
    │   ├── AlignedBufferTests.cs
    │   ├── LatencyHistogramTests.cs
    │   ├── BenchmarkEngineTests.cs    # FakeIoEngine, deterministic
    │   ├── DirectIoIntegrationTests.cs
    │   └── ResultSerializationTests.cs
    ├── DiskMark.Cli.Tests/
    │   ├── DiskMark.Cli.Tests.csproj
    │   └── CommandLineParserTests.cs
    └── DiskMark.Desktop.Tests/        # Avalonia headless: real short run, saves rendered frames
        ├── DiskMark.Desktop.Tests.csproj
        └── MainWindowTests.cs
```

---

## Proposed Changes

### Component 0: Repository Scaffolding

- Run `git init` and add a standard .NET `.gitignore`.
- Add `global.json` pinning SDK `10.0.301` with `rollForward: latestFeature`.
- Add `Directory.Build.props` with `Nullable`, `ImplicitUsings`, and `TreatWarningsAsErrors` (this also turns IL2xxx/IL3xxx trim and AOT warnings into errors).
- Add `Directory.Packages.props` with central versions:
  - Avalonia, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter: `12.1.3`
  - CommunityToolkit.Mvvm: `8.4.2`
  - xunit.v3: `3.2.2`, pinned because `Avalonia.Headless.XUnit` 12.x breaks with 4.x
  - Avalonia.Headless.XUnit, Avalonia.Skia: `12.1.3` (desktop tests)
- `dotnet new sln` on the .NET 10 SDK creates `DiskMark.slnx`.
- Use xUnit v3 on Microsoft Testing Platform (`"test": { "runner": "Microsoft.Testing.Platform" }` in `global.json`).

---

### Component 1: `DiskMark.Core` (Engine & Native Direct I/O)

#### [NEW] `src/DiskMark.Core/DiskMark.Core.csproj`
- Targets `net10.0`.
- `<IsAotCompatible>true</IsAotCompatible>` (enables the trim and AOT analyzers) and `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`.
- Zero external package dependencies.

#### [NEW] `src/DiskMark.Core/Common/` (Domain Model & JSON Schema)
The CLI JSON output and the Desktop UI both use these types.

- **`TestSpec`**: name (e.g. `RND4K Q32T16`), pattern (sequential/random), block size, queue depth, thread count, mode (read/write).
- **`BenchmarkProfile`**: a named list of `TestSpec`s. Built-in profiles:

  | Profile | Tests |
  | :--- | :--- |
  | Default | SEQ1M Q8T1, SEQ1M Q1T1, RND4K Q32T1, RND4K Q1T1 |
  | NVMe | SEQ1M Q8T1, SEQ128K Q32T1, RND4K Q32T16, RND4K Q1T1 |

- **`BenchmarkOptions`**: CDM defaults, all configurable.
  - test file size: 1 GiB
  - passes: 5
  - measure duration: 5 s
  - interval: 5 s
  - data pattern: random (zeros optional)
  - write-through: off
  - target directory
- **`TestResult`**:
  - the spec
  - per-pass values (bytes, I/O count, elapsed time, MB/s, IOPS)
  - headline max and median
  - latency summary (min, mean, p50, p90, p95, p99, p99.9, max in µs)
  - histogram buckets for the UI
- **`RunResult`**:
  - `schemaVersion` and app version
  - timestamp, OS/arch
  - filesystem type and device info
  - options, test results, warnings
- **`DiskMarkJsonContext : JsonSerializerContext`**: source-generated serialization for `RunResult`. Reflection-based `System.Text.Json` is disabled under `PublishAot`.
- **Units:**
  - Throughput: MB/s and GB/s are decimal (10^6 and 10^9 bytes/s).
  - Block and file sizes: binary (KiB, MiB, GiB).
  - Latency: µs.

#### [NEW] `src/DiskMark.Core/Memory/AlignedBuffer.cs`
- `AlignedBuffer : MemoryManager<byte>`, allocated with `NativeMemory.AlignedAlloc` and freed with `NativeMemory.AlignedFree` on dispose.
- Exposes `Span<byte>`, a pointer, and `Memory<byte>` (required by the async `RandomAccess` APIs).

#### [NEW] `src/DiskMark.Core/Memory/DirectIoAlignment.cs`
- **Memory alignment**: `max(Environment.SystemPageSize, device DIO memory alignment)`. Apple Silicon uses 16 KiB pages.
- **Offset and size alignment**, by OS:
  - Linux: `statx` `STATX_DIOALIGN` (kernel 6.1+), falling back to `/sys/class/block/<dev>/queue/logical_block_size`.
  - Windows: `IOCTL_STORAGE_QUERY_PROPERTY` (`StorageAccessAlignmentProperty`), falling back to `GetDiskFreeSpaceW`.
  - macOS: page size.
- Block sizes and file size must be multiples of the offset alignment.

#### [NEW] `src/DiskMark.Core/Interop/NativeStorage.cs`
- All native declarations use `[LibraryImport]` with `SetLastError = true` (source-generated, AOT-safe). There are no `[DllImport]` declarations.
- On every OS, handles are created with `File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, options)`, then adjusted per OS. This avoids P/Invoking the variadic `open` and its mode argument.
- **Windows**:
  - Options: `FileOptions.Asynchronous | (FileOptions)0x20000000 /* FILE_FLAG_NO_BUFFERING */ | FileOptions.DeleteOnClose`.
  - `FileOptions.WriteThrough` is added only when write-through is enabled.
  - .NET passes these flags to `CreateFileW`. `Asynchronous` yields an overlapped handle, so `RandomAccess` async uses IOCP.
- **Linux**:
  - `fcntl(fd, F_GETFL)`, then `fcntl(fd, F_SETFL, flags | O_DIRECT)`.
  - `O_DIRECT` is chosen per architecture: `0x4000` on x64, `0x10000` on arm64 (on arm64, `0x4000` is `O_DIRECTORY`). Unsupported architectures fail fast.
  - Write-through maps to `FileOptions.WriteThrough` (`O_SYNC`).
  - `EINVAL` from `F_SETFL` becomes a `DirectIoNotSupportedException` naming the filesystem type.
- **macOS**:
  - `fcntl(fd, F_NOCACHE /* 48 */, 1)`, using the padded declaration on arm64 (Decision 4) and the plain 3-argument declaration on x64.
  - `F_GLOBAL_NOCACHE` is not used.

#### [NEW] `src/DiskMark.Core/Engine/TestFile.cs`
- **Naming**: random file name (`DiskMarkNET-<random>.tmp`) opened with `FileMode.CreateNew` (`O_EXCL`), so an existing path or planted symlink is never followed.
- **Handle lifetime**: one handle is used for the whole run.
- **Crash-safe cleanup**:
  - Windows: `DeleteOnClose`. The OS deletes the file when the handle closes, even if the process is killed.
  - Linux/macOS: `File.Delete(path)` immediately after opening. The fd keeps the data alive, and the kernel frees it on exit, including after `kill -9`.
- **Free space check**: before Prepare, available space must be at least the file size plus a safety margin.
- **Prepare phase**:
  - Writes the entire file sequentially through the direct handle, using incompressible random data (or zeros if selected).
  - Never rely on `SetLength`/`fallocate` alone. Unwritten extents, and data past NTFS valid data length, read back as zeros without touching the device, which inflates read scores.

#### [NEW] `src/DiskMark.Core/Engine/IIoEngine.cs` and implementations
- **`IIoEngine`**: runs one `TestSpec` for a fixed duration against a handle. It records into per-worker histograms and atomic counters.
- **`AsyncIoEngine`** (default on Windows): keeps `QD x T` `RandomAccess.ReadAsync/WriteAsync` operations in flight on the overlapped handle. They complete through IOCP.
- **`ThreadedIoEngine`** (Linux/macOS, and the fallback everywhere):
  - `QD x T` dedicated threads (not the thread pool), each issuing synchronous `RandomAccess.Read/Write`.
  - Documented behavior: on Unix, Q32T1 is emulated by 32 threads.
- **Offsets**:
  - Random: a per-worker PRNG produces block-aligned offsets uniformly distributed in `[0, fileSize - blockSize]`.
  - Sequential: a shared cursor (`Interlocked.Add`) that wraps at EOF.
- **Hot path**:
  - Buffers are preallocated per worker.
  - Write buffers are filled with random data once per run.
  - `ThreadedIoEngine` performs no allocations per I/O.
- **Future**: an `IoUringEngine` behind the same interface.

#### [NEW] `src/DiskMark.Core/Engine/BenchmarkRunner.cs`
- **Order**: Prepare, then all read tests, then all write tests. CDM fills the Read column before the Write column.
- **Passes**: each test runs `N` passes back to back.
- **Timing**:
  - Each measurement lasts `MeasureDuration`, with `IntervalDuration` between measurements.
  - No warmup beyond Prepare by default (CDM runs DiskSpd with `-W0`). An optional warmup setting is available.
- **Aggregation**: the headline score is the max across passes (Decision 3). The median and all per-pass values are stored.
- **Live telemetry**:
  - The engine publishes a `BenchmarkSnapshot` (current test, pass, bytes, I/O count, elapsed time) backed by atomic counters.
  - Consumers poll it at 5 Hz. There are no per-I/O `IProgress<T>` callbacks, which would allocate and, in the CLI, arrive unordered on the thread pool.
- **Cancellation**: a `CancellationToken` is checked between I/Os. On Windows, outstanding overlapped I/O is cancelled.
- **Checks**:
  - The filesystem probe runs before Prepare.
  - After the run, a warning is added when:
    - read throughput exceeds 16 GB/s
    - RND4K Q1T1 read latency is under 5 µs
    - more than 5% of the test file is in the OS page cache (Linux/macOS `mincore`)

#### [NEW] `src/DiskMark.Core/Telemetry/LatencyHistogram.cs`
- HdrHistogram-style log-linear buckets (about 1% relative error) covering 100 ns to 10 s. Pure power-of-2 buckets would be up to about 50% off at p99.
- Values are recorded in `Stopwatch` ticks and converted to ns.
- There is one histogram per worker, so there are no shared writes or cache-line contention at Q32. Histograms are merged after each measurement.
- Computes min, mean, max, p50, p90, p95, p99, and p99.9, with zero allocations while recording.

#### [NEW] `src/DiskMark.Core/Hardware/TargetEnumerator.cs`
- **Windows**: fixed and removable drive roots from `DriveInfo.GetDrives()`.
- **Linux/macOS**:
  - Mounts are filtered to block-backed filesystems. Excluded: proc, sysfs, devtmpfs, tmpfs, overlay, squashfs, and other pseudo filesystems.
  - The default target is the user's home directory, because mount roots such as `/` are usually not writable.

#### [NEW] `src/DiskMark.Core/Hardware/FileSystemProbe.cs`
- **Resolving the filesystem for a target path**:
  - Linux: longest-prefix mount in `/proc/self/mountinfo`.
  - macOS: `statfs` `f_fstypename`.
  - Windows: `GetVolumeInformationW`.
- **Classification**:
  - **Supported.**
  - **Caches despite direct I/O** (warning): tmpfs, ZFS before 2.3, FUSE, network filesystems, overlayfs.
  - **Rejects direct I/O** (error).
- Flags compressed volumes (NTFS compression, btrfs `compress`) as a warning.

#### [NEW] `src/DiskMark.Core/Hardware/DeviceInfoProvider.cs`
- **Windows**: maps volume to disk with `IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS`, then reads model, bus type, and alignment with `IOCTL_STORAGE_QUERY_PROPERTY`.
- **Linux**: maps the mount source to `/sys/class/block/<dev>`, walking partitions and `slaves/` for LVM, dm-crypt, and md. Reads model, rotational flag, and logical/physical block size.
- **macOS**: parses `diskutil info -plist <mount>` with `XmlReader`.

---

### Component 2: `DiskMark.Cli` (Headless Native AOT Tool)

#### [NEW] `src/DiskMark.Cli/DiskMark.Cli.csproj`
- Targets `net10.0`, `<PublishAot>true</PublishAot>`, `<InvariantGlobalization>true</InvariantGlobalization>`.
- References `DiskMark.Core`.

#### [NEW] `src/DiskMark.Cli/CommandLineParser.cs` & `CommandLineOptions.cs`
- **Subcommands**: `run <target-dir>`, `targets`, `profiles`, `--help`, `--version`.
- **Flags**:

  | Flag | Values |
  | :--- | :--- |
  | `--profile` | `default` or `nvme` |
  | `--size` | e.g. `1GiB` |
  | `--passes` | `N` |
  | `--duration` | e.g. `5s` |
  | `--interval` | e.g. `5s` |
  | `--warmup` | e.g. `1s` |
  | `--mode` | `read`, `write`, or `all` |
  | `--data` | `random` or `zeros` |
  | `--engine` | `auto`, `threaded`, or `async` |
  | `--write-through` | (switch) |
  | `--json[=path]` | JSON to stdout, or to a file alongside the table |
  | `--no-color` | (switch) |

- **Sizes** use binary units (`M`/`MiB`, `G`/`GiB`), matching CDM. `MB`/`GB` are accepted as documented aliases for MiB/GiB.

#### [NEW] `src/DiskMark.Cli/OutputFormatter.cs`
- CDM-style results table showing MB/s, IOPS, and µs.
- ANSI color is disabled when `NO_COLOR` is set or output is redirected.
- Live progress goes to stderr, so `--json` on stdout stays machine-readable.
- JSON is written through `DiskMarkJsonContext`.

#### [NEW] `src/DiskMark.Cli/Program.cs`
- Ctrl+C (`Console.CancelKeyPress`) and SIGTERM (`PosixSignalRegistration`) trigger cancellation.
- Exit codes:

  | Code | Meaning |
  | :--- | :--- |
  | `0` | Success |
  | `1` | Usage error |
  | `2` | Target or filesystem error |
  | `3` | I/O error |
  | `130` | Cancelled |

---

### Component 3: `DiskMark.Desktop` (Avalonia Analytics UI)

#### [NEW] `src/DiskMark.Desktop/DiskMark.Desktop.csproj`
- Targets `net10.0`, `<PublishAot>true</PublishAot>`, `<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>`.
- Uses `Avalonia` (12.1.3), `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`, and `CommunityToolkit.Mvvm` (8.4.2).
- **AOT rules**:
  - `x:DataType` on every view.
  - No `ViewLocator` (the template's version uses `Type.GetType`); views are composed directly.
  - No `Avalonia.Controls.DataGrid`: tables use `ItemsControl`/`Grid`.

#### [NEW] `src/DiskMark.Desktop/Controls/LatencyHistogramCanvas.cs`
- Custom control overriding `Render(DrawingContext context)`.
- Renders log-scale histogram bars with gradient fills, axis markers, and p50/p95/p99 overlays, using Avalonia `DrawingContext` primitives.

#### [NEW] `src/DiskMark.Desktop/Controls/ThroughputSparklineCanvas.cs`
- Renders a real-time throughput timeline (MB/s vs time) from a ring buffer of `BenchmarkSnapshot` samples.

#### [NEW] `src/DiskMark.Desktop/ViewModels/`
- **`MainViewModel`**:
  - target selection, benchmark configuration, run state, and cancellation
  - polls `BenchmarkSnapshot` with a `DispatcherTimer` at 5 Hz
- **`BenchmarkItemViewModel`**: one CDM matrix cell (read/write MB/s, IOPS, latency).
- **`DriveInfoViewModel`**: device and filesystem details, plus probe warnings.

#### [NEW] `src/DiskMark.Desktop/Views/MainWindow.axaml` & `DashboardTabs.axaml`
- **Header**:
  - target picker: drive list plus a folder picker (`StorageProvider.OpenFolderPickerAsync`)
  - free space indicator
  - file size, passes, and profile selectors
  - unit switch (MB/s, GB/s, IOPS, µs)
- **Score matrix** (always visible): the CDM 4x2 grid.
  - The active cell shows the live rate.
  - Clicking a cell shows its latency histogram.
- **Tabbed Telemetry Panel** (`DashboardTabs.axaml`):
  - Latency: histogram with p50/p95/p99 markers
  - Percentiles: table for every test
  - Throughput: live MB/s timeline
  - Drive: hardware and filesystem details
- **Performance guard**: UI updates are throttled to 5 Hz and there are no continuous animations, so rendering doesn't compete with latency-sensitive tests (RND4K Q1T1).
- **Save JSON** exports the same schema as the CLI.

#### [NEW] `src/DiskMark.Desktop/Styles/`
- Dark and light theme resources.
- The glass effect uses `AcrylicBlur` on Windows/macOS and falls back to a translucent solid fill on Linux.

---

### Component 4: Verification Suites

#### [NEW] `tests/DiskMark.Core.Tests/` (xUnit v3)
- **`AlignedBufferTests`**: alignment, `Memory<byte>`/`Span<byte>` access, dispose.
- **`LatencyHistogramTests`**: known distributions produce percentiles within 1%; merging.
- **`BenchmarkEngineTests`**: uses `FakeIoEngine` for deterministic coverage of:
  - offset alignment and range
  - QD accounting
  - duration and interval handling
  - max/median aggregation
  - cancellation and cleanup
- **`DirectIoIntegrationTests`**:
  - Real handle creation, Prepare, read, and write in `DISKMARK_TEST_DIR` (default: `.testdata/` in the repo).
  - Skipped with a reason on filesystems that reject or cache direct I/O.
  - Never uses `Path.GetTempPath()`, which is tmpfs on many Linux systems.
  - Proves direct I/O is active by checking that an unaligned offset fails (`EINVAL` on Linux, `ERROR_INVALID_PARAMETER` on Windows).
  - Linux and macOS: after Prepare, `mincore` over an mmap of the file shows under 5% resident pages. On macOS this proves `F_NOCACHE` took effect.
- **`ResultSerializationTests`**: JSON round trip through `DiskMarkJsonContext`, with `schemaVersion` present.

#### [NEW] `tests/DiskMark.Cli.Tests/`
- **`CommandLineParserTests`**: subcommands, size units and aliases, durations, JSON targets, invalid input.

#### [NEW] `tests/DiskMark.Desktop.Tests/` (Avalonia headless)
- Renders the window, rebuilds the matrix when the profile changes, and runs a real short benchmark through the view model.
- Asserts the matrix and charts fill in, and saves light and dark frames to `bin/**/frames/`.

---

### Deferred: Raw Block Device Mode (Phase 2)

- CLI-only. Phase 2 ships read-only raw mode first.
- Write mode requires `--raw-device <path> --allow-destructive-write` plus typed confirmation of the device name.
- **Requirements**:
  - Admin/root check.
  - Refuse if the device is mounted or in use:
    - Linux: opening a block device with `O_EXCL` fails with `EBUSY` when mounted.
    - Windows: lock and dismount every volume on the disk (`FSCTL_LOCK_VOLUME`, `FSCTL_DISMOUNT_VOLUME`), since sectors of mounted volumes are write-protected.
    - macOS: use `/dev/rdiskN` after `diskutil unmountDisk`.
  - Refuse the system/boot disk.
  - Query device size with `BLKGETSIZE64`, `IOCTL_DISK_GET_LENGTH_INFO`, or `DKIOCGETBLOCKCOUNT` x `DKIOCGETBLOCKSIZE`.

---

## Error Handling

- **Disk full during Prepare**: abort, delete the file, and report required vs available space.
- **`EINVAL` when enabling direct I/O**: explain the filesystem limitation and suggest another target.
- **Access denied**: suggest a writable directory.
- **I/O errors or device removal mid-run**: cancel, and report partial results flagged as incomplete.
- **Windows**: document that real-time antivirus scanning of the test file can skew results.

---

## Verification Plan

### Prerequisites
- .NET SDK 10.0.301 (pinned by `global.json`).
- Native AOT toolchain:
  - Linux: `clang` and `zlib1g-dev`
  - macOS: Xcode command line tools
  - Windows: Visual Studio "Desktop development with C++" workload

### Pre-Implementation Spikes
1. **Windows**: confirm `(FileOptions)0x20000000` passes `FILE_FLAG_NO_BUFFERING` through `File.OpenHandle`. An unaligned read should fail with `ERROR_INVALID_PARAMETER`. *Covered by the CI integration test `DirectIo_IsActive_UnalignedOffsetIsRejected`.*
2. **Linux**: confirm `F_SETFL | O_DIRECT` succeeds on ext4. An unaligned read should then fail with `EINVAL`. *Verified locally.*
3. **macOS arm64**: confirm the padded `fcntl(F_NOCACHE)` declaration works. *Covered by the CI page-cache residency test `Prepare_WritesWholeFile_WithoutFillingPageCache`.*

### Automated Tests
1. **Unit and integration tests**:
   ```bash
   DISKMARK_TEST_DIR=./.testdata dotnet test --solution DiskMark.slnx
   ```
2. **CLI Native AOT build and smoke run.** Publishing must produce zero IL2xxx/IL3xxx warnings.
   ```bash
   dotnet publish src/DiskMark.Cli/DiskMark.Cli.csproj -c Release -r linux-x64
   ./src/DiskMark.Cli/bin/Release/net10.0/linux-x64/publish/diskmark --help
   ./src/DiskMark.Cli/bin/Release/net10.0/linux-x64/publish/diskmark run . --size 64MiB --passes 1 --duration 1s --interval 0s --json
   ```
3. **Desktop Native AOT build**:
   ```bash
   dotnet publish src/DiskMark.Desktop/DiskMark.Desktop.csproj -c Release -r linux-x64
   ```
4. **CI matrix** (`.github/workflows/ci.yml`):
   - Runners: `ubuntu-latest` (x64), `ubuntu-24.04-arm` (arm64), `windows-latest` (x64), `macos-latest` (arm64).
   - Each job builds, runs the tests, AOT-publishes both apps, and runs the CLI smoke test.
   - Native AOT cannot cross-compile between OSes, and this matrix catches architecture-specific bugs (Linux arm64 `O_DIRECT`, the macOS arm64 variadic call).
5. **Releases** (`.github/workflows/release.yml`):
   - Triggered by pushing a `v*` tag.
   - Builds linux-x64, linux-arm64, win-x64, osx-arm64, and osx-x64 (osx-x64 is cross-compiled).
   - Smoke-tests every build except osx-x64, then attaches the archives and `SHA256SUMS.txt` to a GitHub release.

### Accuracy Validation
Compare against a reference tool on the same filesystem. Each test must fall within about ±10%.

- **Linux** (fio):
  ```bash
  fio --name=rnd4k-q32 --filename=./fio.tmp --size=1G --direct=1 --ioengine=libaio --rw=randread --bs=4k --iodepth=32 --numjobs=1 --runtime=5 --time_based
  ```
- **Windows** (DiskSpd or CrystalDiskMark itself):
  ```bash
  diskspd -c1G -b4K -o32 -t1 -r -S -W0 -d5 -w0 -L test.tmp
  ```
- KDiskMark's fio mappings are a useful reference for profile parity.

### Manual Verification
1. Launch `DiskMarkNET`.
2. Check target discovery:
   - Pseudo filesystems are hidden.
   - Filesystem type and free space are shown.
   - Selecting a tmpfs folder (e.g. `/tmp` on Linux) shows a warning.
3. Run a 1-pass 64 MiB smoke test.
4. Run at CDM defaults (1 GiB, 5 passes, 5 s) and compare with the accuracy validation numbers.
5. Check live updates: the active cell, the throughput timeline, and the histogram refresh at 5 Hz without UI stutter.
6. Confirm the test file is gone after completion, after cancellation, and after `kill -9` of the CLI mid-run (disk space returns and no `DiskMarkNET-*.tmp` remains).
