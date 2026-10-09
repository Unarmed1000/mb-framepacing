# mb-framepacing: notes for Claude

Frame pacing / animation error measurement.

The repository has two parts, and the license follows them (see Conventions):

- **`sdk/`** (BSD 3-Clause) holds everything applications embed or use to read the results: one SDK with one version, laid out by
  language (`sdk/cpp/`, `sdk/csharp/`, `sdk/python/`, `sdk/unity/`, `sdk/shaders/`), its docs and its golden data. Its modules:
  - **marker**: what goes **into** the application, a QR marker drawn into every frame (C++ `MB::FramePacing::Marker`, C#
    `MB.FramePacing.Marker`, Python `mb_framepacing.marker`, the Unity package);
  - **data**: reads the tools' capture data and analysis output (C++ `MB::FramePacing::Data`, C# `MB.FramePacing.Data`, Python
    `mb_framepacing.data`);
  - **pacer** (C++ `MB::FramePacing::Pacer` only; **experimental**, off by default): paces a frame loop from what the application
    says its platform can do (its capabilities), with a steady clock and the display's refresh period at the least: it gives every
    frame its waits, its start time, its swap interval, its animation time, how to present it and the marker's pacing values, holds
    a target frame rate, and adapts the swap interval (the full-window rule of mb-framepacing-explained's simulation, and its fix
    as the default); it counts what the display did from the display times the platform reports (statistics only);
  - **core**: the types every module shares, `Point` and `Rectangle` (always valid: a negative size is 0; its edges must fit int32, which is asserted and never clamped) in every
    language (C++
    `MB::FramePacing` with the library version and the time types in `core/time/`: `NanosecondTimeSpan`, `NanosecondTickCount` and `NanosecondTimeDuration` (a signed interval, a point on a clock and a length of time that is never negative, in nanoseconds: **what the marker, the data modules and the tools hold every time in**; exact from ticks, and to ticks truncated for an interval and the tick it is in for a point; `FromSeconds(double)` truncates to the nanosecond and `NanosecondTickCount::FromCounter` rounds down to it; in C# and Python too, where they are the only time types Python has), the tick types `TimeSpan` (C#'s `System.TimeSpan`, out of range throws), `TickCount64`, `TickCount32` (wraps every 429.5 s, compares across the wrap), `TimeSpan32` and `TimeDuration` (ticks of 100 ns, for applications and .NET's own APIs: nothing in the marker, the data modules or the tools holds a measured time in them), and the optional `core/time/ChronoConversion.hpp` (the tick and the nanosecond types); `ByteSpanUtil` (`WriteLE`/`ReadLE<T>`: little-endian values, the
    byte count from the type) for every module's file and wire formats; the core and the marker module have 100 % test coverage
    (regions, functions, lines, branches), measured with llvm-cov without asserts (`NDEBUG`); the C# core has the same time types
    (`sdk/csharp/core/source/Time/`, member for member, .NET exceptions; among the tick types `System.TimeSpan` is the signed interval, and `TimeSpanUtil.FromSeconds`
    converts seconds to the tick on every runtime, since Unity's `TimeSpan.FromSeconds` rounds to a millisecond), and the C# core and
    marker module have 100 % line and branch coverage too: `python tools/check_csharp_coverage.py` (Microsoft code coverage through `dotnet test --collect`; CI's `dotnet-lint`), C# assembly `MB.FramePacing`, Python
    `mb_framepacing`). The SDK never reads a clock: applications pass their own clock's times (the C++ tests' `SteadyClock` is a test
    helper). The core's types hide same-named types that a `using` brings into `MB.FramePacing.*` code: the GUI writes `Avalonia.Point`.
- **`measure/`** holds the .NET tools that **measure**: they record a capture card through ffmpeg and analyse the markers.

See `README.md` for the overview and `sdk/doc/marker-format.md` for the marker specification. **The document is the reference**: C++
(`sdk/cpp/marker/source/mb/framepacing/marker/FrameMarker.cpp`) and C# (`sdk/csharp/marker/source/FrameMarker.cs`) must match it byte for byte. The tools'
`MarkerPayload` (`measure/libs/MB.FramePacing.MarkerDecoding`) delegates to the C# module; ZXing is only used for decoding.

## Layout

| Path                                              | Contents                                                                                                          |
| ------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| `sdk/`                                            | **BSD 3-Clause**: everything below it; `sdk/README.md` is its entry point (where to start, parts, versions)       |
| `sdk/VERSION`, `sdk/LICENSE`                      | The SDK's one version (every module and language, released with `sdk-v*` tags) and its BSD text                   |
| `sdk/cpp/`                                        | The C++20 library: one CMake project (`mb_framepacing`), presets, `package_release.py`, `tests/consumer`          |
| `sdk/cpp/core/`                                   | Core module `mb_framepacing::core`: library version, time types, `Point`, `Rectangle` + tests                     |
| `sdk/cpp/marker/`                                 | Marker module `mb_framepacing::marker` with its own QR encoder, `reference/` (qrcodegen), `marker-render`, tests  |
| `sdk/cpp/data/`                                   | Data module `mb_framepacing::data` (reads; nlohmann/json via FetchContent, inside only) + GoogleTest tests        |
| `sdk/cpp/pacer/`                                  | Pacer module `mb_framepacing::pacer`, GoogleTest tests with the simulations and `pacer-sim` (`tests/`, test code) |
| `sdk/cpp/conan/`                                  | Conan 2 recipe `mb-framepacing`, a component per module (conan-center-index layout, a local-recipes-index remote) |
| `sdk/csharp/core/`                                | C# core module `MB.FramePacing` (`Point`, `Rectangle`, the time types; .NET Standard 2.1, C# 9) + NUnit tests     |
| `sdk/csharp/marker/`                              | C# marker module `MB.FramePacing.Marker` (.NET Standard 2.1, C# 9, no dependencies) + NUnit tests                 |
| `sdk/csharp/data/`                                | C# data module `MB.FramePacing.Data` (.NET 10): reads and writes captures.mbcd and the analysis output            |
| `sdk/python/`                                     | Python package `mb_framepacing` (`marker`, `data`; standard library only, Python 3.12) + unittest tests           |
| `sdk/unity/`                                      | Unity package `com.manabattery.framepacing` sources (helpers, samples), `build_upm.py`, `check_in_unity.py`       |
| `sdk/shaders/`                                    | Reference shaders that draw the marker as one quad: HLSL, GLSL for OpenGL 3.3/ES 3.0, OpenGL ES 2.0 and Vulkan    |
| `sdk/doc/`                                        | Marker format and fields, integrating, Unity, vocabulary, the data formats, the pacer guide, encoding performance |
| `sdk/test-data/markers/`                          | Golden marker images and module digest from the C++ library                                                       |
| `sdk/test-data/data/`                             | The data modules' golden data: a test clip imported and analysed, and `digest.json`                               |
| `sdk/test-data/pacer/`                            | The pacer's golden data (`pacer-sim --golden`): scenario frames and results, the tier loops' digests and frames   |
| `measure/VERSION`                                 | Version of the tools (released with `tools-v*` tags)                                                              |
| `measure/app/`, `measure/libs/`, `measure/tools/` | CLI, Avalonia GUI, MarkerDecoding/Capture/Analysis/Charts libraries (+ `UnitTest/`), DocImages, Benchmarks        |
| `measure/doc/`                                    | Usage, install guides, live capture and camera (both experimental), the README images (`measure/doc/images`)      |
| `measure/test-data/videos/`                       | 60 Hz test clips with manifests from mb-framepacing-explained, `VideoClipTests`                                   |
| `doc/`                                            | Project docs: releasing, roadmap                                                                                  |
| `pacer-captures/`                                 | Capture sessions of the pacer: results documents, charts, `runs.csv`; the zips are assets of a GitHub release     |
| `tools/`                                          | Repository scripts (checks, golden data, shaders, camera rate table)                                              |
| root `Directory.*.props`, `UnitTest.props`        | Shared .NET build settings (C# projects only; see below), central package versions                                |
| `mb-framepacing.slnx`                             | IDE solution with every .NET project                                                                              |
| `licenses/`                                       | Third-party licenses                                                                                              |

## Build and test

```
mb-quality -r --all .                            # the standard check: dotnet format + CSharpier + build + tests + vulnerable packages
mb-quality -r --repair .                         # apply formatting, then build and test
dotnet build mb-framepacing.slnx                 # warnings are errors (Directory.Build.props)
dotnet test  mb-framepacing.slnx
cd sdk/cpp && cmake --preset windows && cmake --build --preset windows && ctest --preset windows   # every module, the pacer too (on in windows and linux-sanitize); linux / linux-clang / macos
dotnet run --project measure/app/FramePacing/FramePacing.csproj -- selftest --fps 500  # end to end without hardware
uv sync                                          # the Python dev tools in .venv, on the Python of .python-version (3.12)
uv run python -m unittest discover -s sdk/python -t sdk/python    # the Python package against sdk/test-data
uv run tools/check_conan.py                      # the Conan recipe built from this checkout, in a temporary Conan home
```

- **mb-quality**
  - It is on the PATH; its config is the root `.mb-quality.json`, and the CSharpier tool manifest is `.config/dotnet-tools.json`.
  - It only checks folders that contain a solution and takes the `*.csproj` files next to it. So **every project folder has its own
    one-line `.slnx`**; keep them when adding a project. The root `mb-framepacing.slnx` is the IDE solution.
  - A run that reports "0 projects" means a project folder is missing its `.slnx`.
- **The root `Directory.Build.props` applies to C# projects only.** Its `PropertyGroup` is conditioned on `.csproj` because MSBuild
  also imports it into the Visual Studio C++ projects CMake generates. Without the condition, the `Platform`/`PlatformTarget` pin
  breaks CMake's compiler detection.
  - `measure/Directory.Build.props` imports it and adds `net10.0` plus `measure/VERSION`.
- **Every .NET project is pinned to AnyCPU**, because some machines set a `Platform=x64` environment variable. Keep that pin:
  without it, single-project builds go to `bin/x64` and cause MSB3270 warnings.
- **C++ formatting and linting**
  - C++ follows `sdk/cpp/.clang-format` and `sdk/cpp/.clang-tidy` (namespaces are CamelCase: `MB::FramePacing::Marker`). The
    `.clang-tidy` checks and options are gtec-demo-framework's (plus `hicpp-exception-baseclass`). Suppress a finding only with
    `NOLINTNEXTLINE(<check>)` and a comment line saying why (the MSVC standard library makes `bugprone-exception-escape` report
    allocation failures that are caught).
  - `python tools/check_cpp.py` runs both on our sources only (never `third_party/` or fetched dependencies) of every module
    (`--module core|marker|data|pacer` for one), with the versions CI pins in `uv.lock` (`uv run tools/check_cpp.py`). clang-tidy needs a
    configured build: `sdk/cpp/build/<preset>`, default `windows` (the VS generator writes no compile database, so the script passes the
    include paths); `--preset` takes another one. With a compile database (`linux-sanitize`, CI) clang-tidy checks the sources that build
    compiles: a module that build leaves out is skipped, and the script says so. To apply formatting: `clang-format -i` on the files the script lists.
  - **The C++ library is one project of modules** (Boost/Poco style): a folder per module (`sdk/cpp/<module>/{include,source,tests}`),
    each a static library `mb_framepacing_<module>` (alias and export `mb_framepacing::<module>`), headers `<mb/framepacing/<module>/<Type>.hpp>`,
    grouped in subfolders where a module has many (`core/time/`, `marker/geometry/`, `marker/payload/`, `data/analysis/`, `data/capture/`,
    `pacer/capability/`, `pacer/display/`, `pacer/frame/`, `pacer/hold/`, `pacer/placement/`, `pacer/rule/`, `pacer/tier/`, `pacer/timeline/`)
    (no umbrella headers: callers include each type's header; functions live in a header of their own, e.g. `marker/FrameMarker.hpp`, as
    C#'s static classes), sources mirroring them (`source/mb/framepacing/<module>/<Name>.cpp`, one per header; private helpers in
    `source/.../detail/`, in a namespace named for what they are, such as `Marker::WireFormat` and `Data::CaptureDataFormat`, and named in
    full at every use: no `using namespace`), namespaces
    `MB::FramePacing` (core) and `MB::FramePacing::<Module>`. One export set and package (`find_package(mb_framepacing CONFIG
COMPONENTS ...)`). `MB_FRAMEPACING_BUILD_MARKER` / `_DATA` / `_PACER` leave modules out (no data module: nlohmann/json is never fetched).
    `MB_FRAMEPACING_INSTALL` (on only when top-level) is around every install rule and the CMake package: an application that builds the
    library inside its own project installs its own files only (`tests/consumer/check_consumers.py` checks it).
    Each module's tests are their own executable. What they share is in `sdk/cpp/testing` (`mb_framepacing_test_support`, an OBJECT
    library built with the tests only, never installed): `Testing::AllocationCounter` and the counting global `operator new`/`delete`
    the allocation tests link.
  - **Executable size:** `sdk/cpp/tests/size` holds size probes (a baseline program, then one using the core, the marker, the marker
    and data modules), linked so unused code is dropped (the modules are built with `-ffunction-sections -fdata-sections`).
    `tools/measure_sdk_size.py --toolchain <id>` builds them in Release and MinSizeRel (in `sdk/cpp/build/size`) and reports what each
    one loads from its file minus what the baseline loads (the sum of its sections, `tools/executable_sections.py`: file sizes grow a
    page at a time, 16 KiB on macOS); `--update-doc` rewrites the table in `sdk/cpp/README.md` (between `sdk-size-table` markers),
    `--check` fails beyond 10 % (at least 4 KiB). CI's `cpp-size` job checks MSVC, GCC, Clang and AppleClang and uploads each
    measurement (`--update-doc size-*.json` takes them). Refresh the table in the change that alters a module's size. `--csharp`
    measures the C# modules' Release assemblies for the table in `sdk/README.md` (`--check` in CI's `dotnet-lint`, `--update-doc` to rewrite).
  - **QR encoder (`sdk/doc/encoding-performance.md`):** the marker module encodes with its own encoder
    (`marker/source/mb/framepacing/marker/detail/QrEncoder.cpp`: versions 2 and 6, level M; the symbol as a 64-bit word per row and
    column, compile-time tables, masks by XOR, the mask penalty scored with word operations), about 40 times faster than qrcodegen
    with exactly its symbols: the mask rule (lowest penalty, the lowest numbered on a tie) is part of the format. qrcodegen stays
    unchanged in `marker/reference/third_party/qrcodegen` (`QrcodegenReference` adds access to its penalty score), built only for the
    tests and benchmarks: `QrEncoderTests` compares every symbol, every mask's score and drawn symbols with it, and the benchmark
    `QrcodegenEncode` runs next to `GenerateModules`. Refresh the document's numbers and the size table in the change that alters the
    encoder.
    After any change to the encoder also run the stress test (`mb_framepacing_marker_qr_stress`, `marker/tests/stress`: no arguments
    for about two million payloads on every core plus every 25-module line; ctest runs a short one) and, with Clang, the libFuzzer
    target (`marker/tests/fuzz`, `MB_FRAMEPACING_BUILD_FUZZERS`, on in `linux-sanitize`; CI's `cpp-analysis` fuzzes for a minute).
    Both compare with qrcodegen and stop at the first difference.
    The C# marker has the same encoder (`sdk/csharp/marker/source/QrEncoder.cs` with `QrVersionTables`, `QrMaskPatterns`,
    `QrReedSolomon`, `BitUtil`: .NET Standard 2.1 has no `BitOperations`). Its reference is the module-by-module encoder the library
    had before, `sdk/csharp/marker/Reference/ReferenceQrEncoder.cs`: `Reference` folders, like `UnitTest` and `Benchmarks`, are kept
    out of the library's compile by the root `Directory.Build.props`, and the unit tests and benchmarks link the file. `QrEncoderTests`
    compares the two (and pins the reference to the golden modules), `QrEncoderStressTests` is the stress test (`MB_QR_STRESS_SCALE`
    multiplies its amount; 200 for a long run), and the benchmark `ReferenceEncode` runs next to `GenerateModules`.
  - **Benchmarks:** `sdk/cpp/marker/benchmarks` (Google Benchmark 1.9.5 through FetchContent, `MB_FRAMEPACING_BUILD_BENCHMARKS`, off
    by default, on in the `windows` and `linux-sanitize` presets): encoding, every drawing output, a whole frame. CI builds them
    (`cpp-analysis`) but never runs them: timings on shared runners are noise. Run a Release build locally. The C# marker has the
    same groups with BenchmarkDotNet (`sdk/csharp/marker/Benchmarks`, built with the solution; `Benchmarks` folders, like `UnitTest`,
    are kept out of their library's compile by the root `Directory.Build.props`).
  - Clang's `-Wconversion` includes `-Wsign-conversion` (GCC's and MSVC's do not), so macOS CI can fail where Windows and Linux
    pass: shift and combine small unsigned types after casting them to `uint32_t`.
  - The `linux-sanitize` preset (Clang, AddressSanitizer + UndefinedBehaviorSanitizer, compile database) is what CI runs the tests
    and clang-tidy with.
  - `cmake/Version.hpp.in` is guarded with `// clang-format off`, because formatting breaks its `@VAR@` placeholders.
- **Python scripts** (`measure/build_standalone.py`, `tools/`, `sdk/unity/build_upm.py`): standard library only. They must pass
  `ruff check .`, `ruff format --check .` and `basedpyright` (config: `ruff.toml`, `pyrightconfig.json`, recommended mode). CI runs
  all three.
- **uv manages the Python dev tools:** the root `pyproject.toml` (not a package) pins them in its `dev` group, `uv.lock` pins
  everything they bring, and `.python-version` is the minimum Python (3.12). `uv sync` creates `.venv` on it; `uv run <tool or
script>` runs inside it, and CI runs `uv sync --locked`. `tools/check_cpp.py` and `tools/check_conan.py` use the tools next to the
  Python that runs them, so `uv run tools/check_cpp.py` gets the pinned versions. Raise a tool: change its pin in `pyproject.toml`,
  then `uv lock` (Dependabot's `uv` ecosystem does this weekly). A one-off extra: `uv run --with <package> ...`.
- **Unity package** (`com.manabattery.framepacing`):
  - It isn't stored as one folder: `sdk/unity/build_upm.py` assembles it from `sdk/csharp/core/source` (`Runtime/Core`, asmdef
    `MB.FramePacing`), `sdk/csharp/marker/source` (`Runtime/Marker`, `MB.FramePacing.Marker`) plus `sdk/unity/Runtime/Unity` (helpers,
    all wrapped in `#if UNITY_2021_3_OR_NEWER`), and generates `.meta` files with stable GUIDs.
  - CI runs it with `--check`: every file the package takes from the repository equals its source, and each default reference names a
    file and a field of the package.
  - `sdk/unity/check_in_unity.py` verifies it in a real Unity editor in batch mode (every drawing method pixel exact; `--graphics
glcore|vulkan|d3d12` forces another graphics API); it takes the newest editor Unity Hub installed, or `--unity`. Run it after changing
    the core, the marker module or the helpers. It also checks that `FrameMarkerTexture` keeps a quiet zone in its range and that
    `FrameMarkerOverlay` keeps drawing after a frame's draw threw (it reports the first failure and tries the next frame).
  - The core and the marker module must stay C# 9 / .NET Standard 2.1 without UnityEngine (Unity 2021.2+ has .NET Standard 2.1 in both API levels). Buffer
    APIs take `ReadOnlySpan<T>` for input and `Span<T>` for output, as the C++ library takes `std::span`.
- **Standalone release archive:** `sdk/cpp/CMakeLists.txt` finds `VERSION`, `LICENSE`, `licenses/` and `shaders/` next to itself in a
  release archive, and falls back to `sdk/VERSION`, `sdk/LICENSE`, `sdk/shaders` and the repository root's `licenses/` otherwise. The
  archive (`sdk/cpp/package_release.py`) holds every module, the docs, `test-data/data` and `test-data/pacer`, not `conan/`.
- **Docs**
  - Formatting: `npm install && npm run format` (Prettier: Markdown/JSON/YAML; config `.prettierrc.json`, ignores in
    `.prettierignore`).
  - Regenerate the README images with `dotnet run --project measure/tools/DocImages`. The SVG report examples (`report-example-*.svg`) come from test clips
    imported through ffmpeg (skipped without it): example pictures use the test clips, not the synthetic game. It renders the real GUI **offscreen**
    (Avalonia.Headless) in no-save mode and neutralises machine specific text; the GUI screenshots import the test clip `60-busy-adaptive` as a
    video file (so they need ffmpeg). Never take desktop screenshots. The README's two pictures of the marker in an application
    (`example-app-marker.png`, `example-app-sync-marker.png`) are not DocImages': they are the Vulkan FramePacing sample of the user's
    unofficial gtec-demo-framework (`DemoApps/Vulkan/FramePacing/ExampleMarker.png`, `ExampleSyncMarker.png`, taken with the
    framework's screenshot option), copied unchanged; new ones come from there.
- **Data modules (BSD 3-Clause):** `MB.FramePacing.Data` reads and writes `captures.mbcd` (`sdk/doc/capture-data-format.md`) and
  the analysis output (`sdk/doc/analysis-output-format.md`: `summary.json` with `formatVersion`, which covers the CSVs, and the CSVs). The
  tools write and read every file through it; their own types map to it (`CaptureDataMapping` in Capture, `AnalysisDataMapping` in
  Analysis). Keep the output byte for byte: the golden data (`sdk/test-data/data`, `digest.json`) is written back exactly, and every
  language's reader must read the digest's values. After a format change: `python tools/update_test_data.py` (needs ffmpeg).
  - **A `captures.mbcd` record is 256 bytes** with two equal 112-byte slots for the markers' bytes as read: either holds any payload
    a main marker's QR code can carry (106), so a field added to the markers does not change the records (the user's choice over
    the smallest slots that fit). Readers refuse another record size.
  - **Typed times** (C++ and C#, the same names; the tools are typed throughout too, with these names: `PresentedFrame`, `CaptureRow`,
    `ChartRun`): every time is a whole number of nanoseconds. Points in time are `NanosecondTickCount` (named `…Time`:
    `FirstSeenTime`, `HostTime`, `DeviceTime`, empty when unknown), spans `NanosecondTimeSpan` (named for what they are: `DisplayDelta`,
    `Drift`, `TargetFrameTime`), and a length of time that is never negative is `NanosecondTimeDuration`: the marker's frame times and
    CPU busy (`MarkerTargetFrameTime`, `MarkerPreferredFrameTime`, `CpuBusy`). The tools' other lengths (display time step, time on
    screen, frametime, CPU wait, periods) are signed spans. `System.TimeSpan` remains only for .NET waits, timeouts and progress,
    converted with the types' own helpers (`ToTimeSpan()`, `FromTimeSpan(...)`: never `* 100` or `/ 100` at a call site). Python's
    marker and data modules keep plain `int`s named `*_ns`.
  - **The files hold times as whole nanoseconds** (`…Ns` columns and fields), in the integer type the value has: a marker's value is
    in the CSV exactly as the marker carried it (`animationNs` an `i64`, `markerTargetNs` a `u32`, 4294967295 = on demand), and
    `summary.json`'s settings too (`capturePeriodNs`, `errorThresholdNs`, `pacing.refreshPeriodNs`, `targetFrameNs`). The tools'
    `MarkerPayload` holds the marker's nanoseconds as they are: nothing between a marker and a file is converted or rounded, and
    nothing goes through a floating point number: never add a milliseconds column or field for a time.
    Only `summary.json`'s statistics and histograms are milliseconds (`…Ms`): a mean or an interpolated percentile is no whole
    nanosecond. A whole number is digits with a `-` in front when negative, in every language's reader (no `+`, spaces, fraction or
    exponent). The rule covers the files the tools read too: an image sequence's timestamp file (`import --timestamps`) is
    `fileName,timeNs`. **The format versions stayed 1** (changed in place; the user's decision: no migration): a marker, a recording,
    a capture folder or an analysis output from before the nanoseconds is invalid and is made again
    (`sdk/doc/analysis-output-format.md` and `capture-data-format.md` say what is refused and what reads a hundred times too small).
  - **Readers are strict, and alike:** what every file has is required (`sdk/doc/analysis-output-format.md` marks it), a value must be
    of its field's type and in its range, and content that is not is one error type per module (C++ `DataFormatError`, C#
    `InvalidDataException`, Python `DataFormatError`), with the file and line for a CSV. A file that cannot be opened is the
    platform's error, not a format error.
  - **The C# CSV code costs a row per line, not a string per cell** (a run of an hour at 240 Hz has 864,000 lines in each file):
    `CsvLineWriter` builds a line in its own buffer and `CsvLineReader` hands out each line as a span of its buffer; `CsvRow` is a view
    of the line's cells; the texts a file repeats (status, kind, a set of flags) are made once (`CsvTextCache`, and the name caches in
    `AnalysisDataMapping`). The output is byte for byte what `ToString` and `string.Join` wrote. `OutputFileBenchmarks`
    (`--long-running`): an hour's frames CSV writes in 87 ms with 1 KB allocated and reads in 295 ms with 340 MB (the rows). Regenerating
    the golden data (`update_test_data.py`) always changes its wall-clock values (start and analysis times, duration, host times);
    anything else that changes is a real difference.
  - **Capture times** (`MB.FramePacing.Capture`): `CaptureClock.Now` and a frame's host time are `NanosecondTickCount` (the time since the capture
    started, from the stopwatch counter: `NanosecondTickCount.FromCounter`); ffmpeg's timestamps become nanoseconds in integer
    arithmetic, rounded to the nearest nanosecond. A frame's device time is a `DeviceTimestamp`: a time, `Unknown` (the device gave none) or `Pending` (it arrives after the
    pixels: ffmpeg's showinfo lines, resolved by the recorder through `IDeviceTimestampSource`). Pending exists only between a source and
    the recorder; files hold a time or unknown, and everything after the recorder has a `NanosecondTickCount?`.
  - **Analysis times** (`PresentedFrame`, `CaptureRow`): the marker's own values keep the marker's 0 = unknown (`IntendedDisplayTime`,
    `CpuStartTime`, `MarkerTargetFrameTime`, `MarkerPreferredFrameTime`, `CpuBusy`); what the analysis works out is nullable
    (`DisplayDelta`, `TargetFrameTime`, `Lateness`). `IntendedDisplayTime` and `CpuStartTime` are on the pacer's clock, `FirstSeenTime`
    on the capture's: `PacingAnalyzer.CaptureMinusPacer` is where the analysis subtracts them. Milliseconds are
    `NanosecondTimeSpan.TotalMilliseconds`, one division of the nanosecond count (for a value of whole ticks, the same double as ticks / 10000.0).
    The charts read the typed values; what they keep as nanosecond counts is integer arithmetic (whole refreshes, strip cells) and the prepared
    sequences (`FrameSequence`, `WaveletMatrix`), which rank plain integers. The GUI's `Stopwatch` timestamps stay raw.
- **Pacer module (`sdk/cpp/pacer`, `sdk/doc/pacer.md`): EXPERIMENTAL.** The pacer of the redesign of 2026-10 (`TierPacer`), which
  replaced the first one (`FramePacer`, retired on 2026-10-09 with its refresh clock and its present feedback). It is checked
  against its own simulation and, on one machine, against the display times a graphics driver reports (the first integration: the
  user's unofficial gtec-demo-framework, three FramePacing samples, linked from the guide's Status and from `integrating.md`), but
  no capture of it has been analysed with the tools. Off by default (`MB_FRAMEPACING_BUILD_PACER`, Conan's `with_pacer`); build it
  with `-DMB_FRAMEPACING_BUILD_PACER=ON`.
  - **Two documents.** `sdk/doc/pacer.md` is the guide: how to use it, with a Status table (checked / not checked). **`sdk/doc/pacer-design.md`
    is the record of the redesign**: what was wrong, every measurement with its conditions, each decision with what decided it, and
    "What is still missing". A measurement goes into the record as numbers with their conditions; nothing from single runs goes
    into the guide as advice. Keep the guide's Status table and the record's "What is still missing" current with every pacer change.
  - **Off for users, on where we check:** the `windows` and `linux-sanitize` presets, CI's and the release workflow's C++ build and
    tests (Windows, Ubuntu, macOS) build it. `check_consumers.py` runs twice more with it (`subdirectory-pacer`, `package-pacer`) and
    `check_conan.py` once more (the consumer and the Conan test package pace one frame with `TierPacer` and fill a `Payload` from the
    schedule), and the consumer check fails when a default install holds anything of the pacer.
  - **Every place users meet it says "experimental":** the guide's notice and its Status table, the READMEs, `integrating.md`, the
    CMake and Conan option descriptions, the release notes, and the first line of every public type's comment. Keep it that way
    until it has been measured on real swap chains.
  - **The rule it is built to (the user's): the pacer holds every pacing rule and every time calculation; the application supplies
    information and carries out the waits and the present.** Logic about pacing that an application has to write itself is a fault
    of the pacer. Values in, values out: no platform API, no callbacks, no clock reads, no waits inside the library; made once (the
    rule's frame windows), no allocation per frame after that (only `SetSettings` with a larger frame window allocates).
  - **Capabilities and tiers** (`pacer/capability/`): an application says what it has and what of it is active (`PacerCapabilities`;
    the active set is how it controls the pacer). A tier is the capabilities a set needs (`PacerTierUtil::Rate`, usable without a
    pacer). **Three major tiers of four sub tiers, written "3.1"** (the user, 2026-10-09): major = who places a frame on its refresh
    (1: the display's side, which also skips an overdue frame, `PresentAtTime` + `PresentSkipsOverdue`, **rated only, no pacer**; 2:
    the display's side, `PresentAtTime`, built against the simulation only; 3: the frame loop). The sub tier is a rank inside it
    from `WaitForPresent` and `VBlankTimes` (the wait first where the display places the frame, the vertical blank times first
    where the loop does). No capability that changes how good the pacing is may hide inside a place. **Only a time before which a
    frame is not shown makes a tier** (decision 16): `PresentAfterDuration` and `PresentSwapInterval` are rated beside it
    (`DisplaySideHolds`) and given on every present where active. `PacerTierText` has the words and `NumberOf(tier)`; an
    application shows no texts of its own. **Every tier has both aims** (`PacerAim::Smoothness`, the default, and `LowLatency`): an
    aim is never built in as a tier's only behaviour, and the pacer does not pick it (the user: not now).
  - **Built from parts** (the user: no restart, four whole pacers taken apart under their tests): `TierPacer` (`pacer/tier/`) holds
    two ways of placing frames, `ClockGridLoopPacer` (a grid on the clock) and `VBlankLoopPacer` (the display's vertical blanks),
    and hands a run over between them (`PacerHandover`) when the active set changes; the parts are `hold/PresentWaitRule`,
    `hold/GpuWaitRule`, `timeline/VBlankTimeline`, `placement/DisplayPlacementUtil`, `display/DisplayErrorCounter`,
    `rule/SwapIntervalRule` and `rule/FrameWorkRule`. The four single-tier classes (`TimerPeriodOnlyPacer`, …) are test code
    (`pacer/tests/simulation`): `TierPacer` with its capabilities fixed, kept for their tests.
  - **A frame's calls:** `AddVBlank`, `PlanFrame` (a wait for a present or for the GPU's work, then a start time; planned again
    after each wait), `AddPresentWait` / `AddGpuWait`, `BeginFrame` → `FrameSchedule`, `EndFrame` → `PresentPlan` (a time to wait
    until, `NotBeforeTime`, `MinimumDuration`, `SwapInterval`, `CpuBusy`), `AddPresent`; and where the application has them
    `AddGpuWork`, `AddSystemWait`, `AddDisplayReport`. `EndFrame` comes before the present and before any wait, which would count
    as work. A wait may take a few swap intervals and 50 ms at the least (`MinWaitTimeout`, the user's choice).
  - **No catching up** (the user): after a refresh that was lost the animation step stays the swap interval, and the animation
    time is that refresh behind the clock (`RefreshesBehindClock`); a constant offset does not show, correcting it does. A loss
    that repeats is followed. A start the display's side held is not stepped over (decision 17).
  - **Nothing a pacer uses about a display unless it is certain to be the window's display** (the user: 100 % certain; a
    consistency check is no substitute). The refresh period is the application's to give: a swap chain's reported refresh was the
    fastest display's of the desktop.
  - **Display times are statistics only** (the user, 2026-10-04: "stats only, to simplify"): `AddDisplayReport` →
    `DisplayErrors()` (the animation error by the tools' rules, and the time from a frame's start to its display). Never let them
    change a swap interval or an animation time without the user's word; pacing by them is the guide's "Not used yet".
  - **Fixed refresh rates only** (the user: fixed refresh first, variable refresh once this works, as more tier pacers behind the
    same calls). The test display is a G-SYNC one: ask for its state, and what displays were on at what rates, before reading a
    capture, and what else ran on the machine.
  - **Target frame rate:** `PacerSettings::SetPreferredFrameRate` / `SetPreferredFrameTime` → `PreferredSwapIntervalAt(RefreshPeriod)`
    with the tools' rounding (`FrameTimeRounding.WholeRefreshes`: up, a twentieth of a refresh of slack, at least 1); with
    `PreferredSwapInterval` the slower of the two counts. It is the fastest rate: the rule only goes slower.
    **The frame rates a display can show** (the user, 2026-10-07: for a menu, "nothing lower than 20"): `FrameRateStep` and
    `FrameRateStepUtil` (`StepCount`, `StepAt`, `StepFor`/`StepForRate`, `IsStep`/`IsStepRate`; functions of the refresh period
    alone), judged with the same slack through the settings' own rounding, so the two never disagree.
  - **Typed, in nanoseconds** (the user, 2026-10-07; the marker's payload takes the same types, so a schedule's values go into
    one as they are): points in time `NanosecondTickCount`, spans `NanosecondTimeSpan`, lengths that can not be negative
    `NanosecondTimeDuration`. The pacer caps nothing: what a marker's four bytes do not hold is capped by the payload.
    `RefreshPeriod`'s 2⁻³² nanoseconds are private. **Always valid:** `RefreshPeriod` (100 µs to 1 s, no default) and
    `PacerSettings` (constructed from the period; setters assert, then clamp). `SetRefreshPeriod` with another period and
    `SetSettings` with other settings start the frames again and keep the animation time; the same ones change nothing.
  - **Names:** the rule's stretch of frames is the **frame window** (`FrameWindowLength`, `FrameWindowState`), never "window" alone
    (in graphics that is the window system's). The pacer's animation time is the display's, in refreshes: the application's game
    time is its own (`sdk/doc/marker-fields.md`). The default `FrameMargin` is the smaller of 1 ms and an eighth of the refresh
    period (`PacerSettings::FrameMarginAt`, agreed with the user); a margin that was set stays as set.
  - **How it is checked.** Integer arithmetic only: the golden data must come out byte for byte, and does from MSVC and Clang.
    100 % test coverage as the core and the marker (llvm-cov on a Clang build, `NDEBUG`); a header that is on the disk and not in
    its library's `FILE_SET HEADERS` stops the configure. There is no C# port (the roadmap lists one as a possible upgrade).
    - Test code, built with the tests only and never part of the library: `pacer/tests/simulation` (the sister repository's frame
      model, `PacerSimulation`; a frame loop on a display model that queues presents, `SimulateTierLoop` with `LoopSettings`; the
      golden runs, `TierLoopGolden`) and `pacer-sim` (`--golden`, `--loop`).
    - Golden data: `python tools/update_pacer_test_data.py` writes the scenarios' frames from the test clips and runs
      `pacer-sim --golden`: the scenarios' results (`60-busy`: the full-window rule reproduces the sister repo's swap intervals and
      refreshes; `60-busy-full-rate`: at a fixed swap interval every frame is on the clip's refresh), and the tier loops
      (`tier-loops.csv`: a line per run with the length and CRC-32 of its frames as text; `tier-loop-<run>.csv`: eight runs whole).
      Run it after a change to a rule or to how a frame is paced and review the difference.
    - `TierMonitorRateTests.cpp`: 23 rates from 50 to 540 Hz; `WorkloadLoopTests.cpp`: the loads of another pacer's own tests.
  - **Capture sessions** of the first integration: a session is one zip, kept as an asset of the GitHub release `pacer-captures`
    (the user's choice: never in git, so no clone carries them; `gh release upload pacer-captures <zip>`), with a row in
    `pacer-captures/README.md`. Git ignores a zip in `pacer-captures/`: download one there to work with it. Before packing, check the
    files for hardware models (the user's graphics card must never be named: vendor and driver version only), user names and
    local directories. **A session's numbers come from its logs through `tools/pacer_capture_report.py`** (standard library;
    reads the zip in place): `<session>/runs.csv`, the charts and the tables of `<session>.md` between `pacer-capture:<name>`
    comments (`--update-doc`); `--check` fails when a file is not what the zip gives. The two sessions of 2026-10-04 are of the
    first pacer and of a sample that did not hold its frame start at a swap interval of one: the guide links them for what they
    say of variable refresh only. Driver display times are not a measurement by the tools: say so. The guide **never embeds a
    chart**: the SDK archive ships `doc/pacer.md` without images.
  - **Wait for more data before drawing conclusions** (the user): three explanations of one finding were written into the guide
    and withdrawn within a day. A finding from one run, or one that another session has not confirmed, goes into the record as a
    number and not into the guide as advice. Try a diagnostic on stored runs before shipping it.
  - **Android's frame pacing library** may be read (the user, 2026-10-09) and its behaviour described; its code name does not go
    into code, comments, documents or commit messages (cite it by its developer page), and none of its code is copied.
- **Capture data (the default):** a capture decodes every frame's markers live and stores only them, with the timestamps, in
  `captures.mbcd` (`sdk/doc/capture-data-format.md`); the frames themselves (`frames.mbfc`) only with `--keep-frames` / the GUI's "Store
  video frames".
  - The recorder's inspection thread runs `LiveFrameDecoder` (search until `MarkerLocator` locks, then `FrameMarkerDecoder`); the
    start/end triggers (`SequenceMonitor`) use that decode instead of decoding again.
  - The analysis starts from `captures.mbcd` (`CaptureDecoder.FromData`). A capture with only `frames.mbfc` is decoded with the same
    steps (`CaptureDecoder.DecodeFrames`) and gets `captures.mbcd`; `analyze --redecode` redoes that. `VideoClipTests` checks that
    live and afterwards give identical records, so keep the two paths on the shared decoder.
- **The suggested way to measure today** is OBS recording a capture card and `import` of the recording (`measure/doc/usage.md` section 2):
  the display's refresh rate must be the capture source's FPS and OBS's video FPS (the rate the file is saved at), with no scaling and
  little or no compression. Its OBS settings are proposed, not yet verified with a recording: say so until they are. The README
  opens with the sister repository's page (the slides), then how it works and this workflow.
- **The root README never mentions an experimental feature** (the user's rule) except in its one "Experimental features" section,
  which names each (live capture, camera capture, the pacer) with a link to its guide: no experimental commands, options, GUI
  switches or diagram paths anywhere else in it. The other documents say "experimental" wherever the feature appears.
- **Live capture is EXPERIMENTAL** (`measure/doc/live-capture.md`, agreed with the user): recording a capture card or a network stream
  with the tools themselves. It is out of the workflow documents (README, usage.md, the install guides); live-capture.md has it,
  with the per-platform details and the caution that on some platforms, macOS among them, it is probably too slow to be usable.
  - **GUI:** the capture cards and "Network stream (URL, experimental)..." are offered only with **Settings → Experimental features**
    (`CaptureViewModel.ListSources`, `IsExperimental`). The default source is a video file, and the start button says
    **Analyze recording** for a video file or an image folder, **Start capture** for a live source (`StartText`).
  - **CLI:** `--experimental` (`CommonOptions.Experimental`, recursive) shows and allows the experimental parts: `capture`, `locate`,
    `devices` and `camera-rig` (`CommonOptions.ExperimentalCommand`), `--camera`, `--recorded-fps` and `selftest`'s `--camera`,
    `--refresh`, `--tear-every` (`ExperimentalOption`), and a stream URL for `import`. Without it they are left out of `--help`
    (read from the arguments before the commands are made: `ExperimentalRequested`) and refused with a one-line hint. Live capture
    prints `CommonOptions.LiveCaptureNotice` first. Every documented command for them carries `--experimental`.
  - **No synthetic test game in the GUI** (agreed with the user): no source, no `--demo`. `selftest` keeps it (the CLI's check
    without hardware), and the synthetic camera (very experimental) stays behind the experimental switch.
- **Playback reports** (`MB.FramePacing.Charts/source/Playback`, `measure/doc/usage.md` "The playback page"; agreed with the user):
  - **100 % opt-in:** `--playback` on `import`/`analyze`/`render`, the GUI's **Save playback page**; never by `--charts` or Save charts.
  - **One HTML page that runs from the disk** (`PlaybackPage.html`, an embedded resource: markup, CSS, vanilla JS; no network): the
    report card inline (its title and tiles in the page's header), the data inline (`PlaybackData`: the card's `CardPlot`s, the
    section's frames as columns of whole nanoseconds; `PlaybackData.FormatVersion` 3: `periodNs`, `originNs`, `firstNs`, `lastNs`), a
    player bar, a playhead on every panel. The frame columns are written small, in whole
    numbers the page's `readFrames` turns back exactly (the file's header comment has the form: steps from the frame before, the
    time as what is left after its captures' whole periods, runs of equal values as value and count): about 5 bytes a frame on a
    test clip, and exact in the page's JavaScript for runs under 104 days (2^53 ns). `PlaybackFrameColumns` (the unit tests) reads them the same way: change the three together. Video time = the capture's time: an import keeps the
    file's own pts (`-copyts`), so only video-file imports with the Device time source qualify (`PlaybackCapture.Problem`: no camera,
    no `--recorded-fps`, not images). capture.json `inputPath` names the recording (`--video` for older imports).
  - **Zoom steps** (`PlaybackZoom`: whole, 60, 10, 2 s per screen): a zoomed card is `ReportCard.Build(..., visible:)` with its
    `ScrollShape` layers kept (`SvgCardWriter.Write(..., scrollLayers:)`, clipped groups the page translates, as the GUI's Timeline);
    the steps that fit a 64 MB estimate (300 bytes per frame or pixel column drawn, measured), each kept as inert text until shown.
  - **Speed and memory:** the cards build at once (`Parallel.For`, each also builds its panels at once), then their SVG; the page is
    streamed to its file (`PlaybackPage.Write`: the template's pieces, each SVG, the data straight from its `Utf8JsonWriter`), never held as
    one string. `PlaybackBenchmarks` (`measure/tools/Benchmarks`, `--long-running`): a whole page of 10 min or an hour at 60 or 240 Hz takes about 0.2 s and
    allocates about 100 MB; the video's copy is ffmpeg's time. What keeps it small, shared with every card: numbers are written in place
    (`SvgNumber` is `ISpanFormattable`, `SvgMarkup.TryFormatFixed`: interpolate `N(...)`, not `Fixed(...)`, into paths), `SvgCardWriter`
    writes into one `StringBuilder`, and the per-column searches (`PixelColumns.Walk`, `RunEvents`) make no closure.
  - **A folder per report** (`analysis/playback/<prefix>[-<from>s-<to>s]/`: `index.html`, its own video, `playback.json`). An export
    touches only the folders it writes; saving the same report again replaces that folder only. Never share or delete another
    report's files. **Nothing in it names anything outside it** (agreed with the user): no link to the recording, no local path
    (`playback.json` keeps the recording's file name, size and time; the page's command says `<capture folder>`).
  - **The video is always a copy** (`PlaybackExport`, shared by CLI and GUI, which differ only in `decide` and progress): a playable
    recording is copied without a question; one browsers cannot play (`VideoCodecInfo`) gets a playable copy (`PlayableCopy`: remux,
    else H.264, every frame and pts kept) only after a yes, else the report has no video. Answer in advance: `--playback-transcode`,
    else the configuration's `playbackTranscode`, else ask; redirected stdin makes no copy. The GUI's dialog can remember the answer.
  - **The one exception: a video the user names** (`--playback-video-url`, CLI only; asked for by mb-framepacing-explained, whose
    slides serve their own web encode next to the report): written into the page verbatim (`PlaybackVideoKind.External`), nothing
    copied, probed or asked, the recording not needed; only a local file that is there and shorter than the run is a warning
    (`PlaybackResult.Warnings`), never an error.
- **Media sources**
  - Sources other than capture cards (`mb-framepacing import`, and the GUI's "Video file... / Image folder... / Network stream") go through
    `MediaInput` -> ffmpeg.
  - Image sequences get their exact times from `--fps` or the timestamp CSV (`FrameTimestamps`), not from ffmpeg: its concat
    timestamps are 40 ms coarse. The CSV (`ImageSequence.ReadTimestamps`) needs its header line, `fileName,timeNs` found by name,
    and holds whole nanoseconds: a headerless file, or one with `timeTicks` or `timeMs`, is refused, since ticks or whole milliseconds
    would read as nanoseconds.
  - Non-live sources make the recorder wait instead of dropping frames (`IsLive`).
- **Only the markers are read** (an import's default; `--roi auto`, `locate`, the GUI's "Locate marker" for live capture, which is
  experimental; agreed with the user):
  - **The rule is `RegionRule`** (Capture, shared by the CLI and the GUI): a recording (a video file or an image folder:
    `InputPath` set) with no `--roi`, no `--scale` and no `--keep-frames` stores only its markers' regions; `--roi full` (the GUI's
    region box: `full`) is the whole frame; `auto` asks for the regions anywhere; a rectangle is one rectangle, and two joined by `+`
    (`RegionRule.ParseRegions`, agreed with the user) are the main marker's region and the sync marker's, stacked, with `--scale` the
    size of the two stacked (a whole-number downscale of both, `StackedDownscale`). That is how a located pair is written down:
    `MarkerLocateResult.RegionText` and `Scale` (what `locate` prints and the GUI's "Locate marker" fills in) give back exactly
    `Apply`, the same ffmpeg command line (`RegionRuleTests`). Live sources keep the
    whole frame unless asked. It goes in `CaptureCommand.ApplyRegion` / `CaptureViewModel.ApplyRegion`, never in
    `MediaInput.ToCaptureOptions` (the camera-rig tools and the ffmpeg tests call that for whole frames).
  - `FfmpegMarkerLocator` runs ffmpeg uncropped into `MarkerProbe` (nothing is recorded), which returns the main marker's lock and
    the sync marker's when the frames show one (`MarkerProbeResult`). A live source gets 10 s; a recording is read until its first
    marker, to its end (it may start before the application): none at all is `MarkerNotFoundException` and the import stops. A marker
    that moved while located or does not fit the frame is `MarkerRegionException`: the default falls back to the whole frame and
    says so, an explicit `auto` fails.
  - `MarkerCrop` picks a region per marker and one integer downscale; each crop starts a whole number of downscale steps before its
    marker's origin (otherwise module edges fall between stored pixels and dense start markers stop decoding). Regions that would
    overlap become one. Two regions are stored as one frame, the main marker's on top (`FfmpegCommandBuilder.BuildStackedFilter`:
    split, crop, scale, pad the narrower with white, vstack; `FfmpegCaptureOptions.SyncRoi` / `RoiDownscale`), so the tearing check
    stays. `capture.json` has `roi` and `syncRoi`, the `captures.mbcd` header the second region at offset 168 (`SyncRegion`, all
    three languages), and `frames.mbfc`'s header at offset 64 (`CaptureFileHeader.SyncRoi`), so frames decoded again give the same
    capture data.
  - **`frames.mbfc`'s header says how long it is** (128 bytes as written; the records start after it) and its first 64 bytes are what
    every file has: a field after them is there when the header is long enough, and "none" otherwise (`CaptureFileHeader.Read`,
    `MinHeaderSize`). Add a field in the reserved bytes the same way: optional, never a reason to refuse a file.
  - The analysis needs no change: locks are in stored pixels. It warns when a region capture has many undecodable captures, counted
    from the first capture with a marker to the end, or to the end marker when that is the last marker seen (footage before and
    after the run is not a marker that moved).
  - The golden data stays a whole-frame capture: `update_test_data.py` imports with `--roi full`.
  - Measured (60 fps H.264, both markers): 720p 2000 fps against 1400 for whole frames, 1080p 1600 against 700, 2160p 540 against
    200, where it is as fast as ffmpeg decodes the file.
- **Refresh rate and pacing:**
  - A capture card captures at the display's native refresh rate: that is a fundamental assumption, not a setting. The analysis
    takes the capture period as the refresh period; display time steps are whole refreshes. `SyntheticCaptureSource` refuses a capture rate that differs
    from the refresh; `selftest --fps N` simulates an N Hz display.
  - **Charts** live in `MB.FramePacing.Charts` (no GUI): the report cards as shapes (below). The GUI draws them itself with `CardView`
    (`FramePacing.Gui`, the style from `CardStyle`, which parses the SVG's style sheet; hover texts from `CardHover`); the Timeline card
    shows five panels only (`AnalysisViewModel`'s `g_timelineItems`: animation error, display time step, frametime, late share,
    refresh strip; not the events panel or the reference lines, which the report card has), zooms (wheel), pans (drag) and resets
    (double-click), the distribution tabs follow its section, and **Save view** writes the card
    on screen as SVG or PNG (`CardImage`). DocImages checks the width, zoom, the sliding window (sideways scroll, scrollbar and drag
    move the same card; near its edge the next one is built), reset and hover with headless input.
    - **The sliding window:** zoomed, the GUI's Timeline is built for the view plus a screen either side (`TimelineWindow`;
      `ReportCard.Build(..., visible)`): what moves with time goes into `ScrollShape` layers that `CardView` clips to the plots and moves
      by `ScrollOffset`, so scrolling builds nothing and only the distribution cards follow (`SectionCards.Follow`). A window's start is
      snapped to a whole pixel column, so windows at one zoom draw the same columns and swap without a visible change. Files never
      contain a window (`SvgCardWriter` writes the layers' shapes in place), and **Save view** builds the card of exactly the view.
    - **Long runs cost per pixel column, not per frame:** `RunChartData` (cached per `ChartRun`, immutable, built on first use) holds
      each kind of value as a `FrameSequence` (`RankBits`: which frames have one; a `WaveletMatrix`: exact k-th smallest and counts below a
      bound for any range in about 20 steps) and the late share (`LateShareData`). A `RunSection` is a frame range found by binary search;
      its statistics are computed only when a tile needs them. The panels walk the pixel columns (`PixelColumns.Walk`) and query min, max,
      median, percentiles and histograms (`SectionHistograms`) exactly: the output is byte-identical to sorting. The GUI builds cards
      at the window's width (`width`), with the whole run's scales (`wholeRunScales`), in the background (`SectionCards`,
      `LatestRequest`: a newer zoom cancels an older build). `CardBenchmarks` (`--long-running`) measures 1 and 10 hours at 240 Hz: a few ms per zoom or
      window. **Preparing the data** is the cost of a run's first card (an hour at 240 Hz: about 0.14 s and 145 MB allocated to keep
      25 MB; `CardBenchmarks.FirstReportCard`): a `WaveletMatrix` collects and sorts only the different values while there are few
      (`MaxCollectedDistinct`; else all of them, `NanosecondSort` with a scratch array of its own, since a rented one would stay in the pool
      while the panels prepare at once); a run without a static frame prepares each `Animating…` sequence as its unfiltered twin, the
      same object; sequences over the same frames share their `RankBits` (`Errors`, `AbsoluteErrors`); the holds of a kind are
      prepared when a panel asks for that kind (never the holds as planned). `--charts` and "Save
      charts" write the SVG cards (`ChartFiles`: the report and every `DistributionCard`). Test chart changes with
      `ChartVideoClipTests`: every series of every chart is compared exactly with the test clips' manifests (`ClipManifest`, shared
      with `VideoClipTests` by linked source files).
    - The report card (`ReportCard`, `mb-framepacing render`) is a port of mb-framepacing-explained's `generate_charts.py`: its style sheet
      and `text()`/`ms()` helpers are verbatim in `SvgMarkup` (Python's half-to-even rounding included; `ReportSvgTests` pins their
      output). It draws from the analysis output (`AnalysisOutput` reads `summary.json` and the frames CSV back to the nanosecond), any section
      (`RunSection`); more frames than pixels draw per column. PNG goes through a headless Edge/Chrome (`HeadlessBrowser`, `MB_BROWSER`):
      a run that fails gets one more (CI's browsers abort or hang now and then), Linux drops the sandbox when the browser says it has
      none, and an image already at the target is deleted first (the browser is ended as soon as the file is whole).
    - `ReportOptions` picks the items (`ReportItem` ids: `--hide`/`--only`); overlays (`ReportItem.Overlays`, now the animation time
      step on the display time step panel) are opt-in (`Show`, `--show`) and need their panel. The tiles frames dropped and out of order
      (`ReportItem.AutoTiles`) show only when the run or section has either (`RunHeadline.Shown`, which the GUI uses too), unless shown
      on purpose (`Show`); the tiles go in two rows, half of them each (`TilesPerRowFor`: 8 → 4, 10 → 5). The layout options are for a card in a
      document (explained's charts page): `Title`, `StripSeconds` (the strip over the first seconds, its own axis), `HideEmpty` (tiles
      without a value, `HeadlineTile.HasValue`, and the late share without late frames), `TilesPerRow`. The defaults are the standard
      card: `--charts`, Save charts, the GUI and DocImages never set them. `ClampStatic` (on by default) is the one the GUI switches:
      its "Clamp static" check box, which Save view and Save charts follow (`ChartFiles.Write(report, options)`).
    - Cards are shapes (`CardDrawing`: `RectShape`, `LineShape`, `PathShape`, `TextShape`, `GroupShape`, plus `CardPlot` per plot
      area with its data range, for the GUI's zoom and hover and for tests that read values back); `SvgCardWriter` writes them.
      `DistributionCard` builds the error and display time step histograms, the error percentiles and the drift (`render --cards`).
    - The frame timeline (`FrameTimelineCard`, `render --timeline`, at most 40 frames) is explained's timing diagram from the data: CPU
      boxes (CPU start time + CPU busy, overlapping ones in further lanes) placed on the capture's clock by `PacerToCapture` (display time
      - intended display time - lateness; without a schedule, no frame presented after it appears), present arrows, display cells. A
        static frame's cells are violet (`strip-static-a`/`-b`) and the error row says "static" on the static step after it; a frame presented on demand is never
        "held longer" (no interval to be late for).
    - The headline numbers come from `RunHeadline` only: the GUI's tiles and the report card's show the same tiles, and the GUI's
      Display card the report's display box (`RunHeadline.Display`, `DisplaySummary`), so add or change a number there, not in the GUI.
      A tile without a number shows "-" with `HasValue` false.
    - The animation error panel draws a refresh line (`error-refresh`, dashed amber, labelled "+1 refresh (16.7 ms)") at every whole
      refresh an error reaches within a tenth of a refresh, over the frames the scale covers, and only inside the scale
      (`ChartScale.ErrorRefreshTicks`, at most four per side).
    - The error and display time step charts cover every value unless a few are more than 8 times beyond the 99th percentile (a hitch);
      those get a mark with their value at the edge (`ChartScale` decides the scales, `ReportCard.ClipMarks` draws the marks).
    - **Keys:** every panel's title says what it shows; its key (`ReportCard.Key`, right aligned) names each colour with one swatch per
      entry and lists only what the section shows. A key, a tile's value with its detail and the display box's rate line are one
      `TextRunsShape` (pieces of text, each in its own classes) that the renderer lays out end to end (a `<text>` with `<tspan>`s in SVG,
      measured pieces in `CardView`), so no width is guessed. Swatches are coloured characters in the `key-*` classes (■ fill, ━ line,
      ┅ refresh and reference lines, ▐▌ for colours that alternate per frame).
    - **Holds and strip cells by kind** (`RunChartData.HoldKinds`, `HoldKind`): unknown (a capture gap, dashed grey), late (red), an
      older frame came back (out of order, pink), frames dropped by the target (orange), as planned (green). Dropped = skipped frame
      indices over a gap-free capture that never came back out of order (`DroppedBeforeFrame`); the strip colours the refreshes where
      they were due orange and the out-of-order refreshes (`PresentedFrame.OlderFrames`, CSV `olderFrames`) pink (anywhere in a frame's cells).
    - **Reference lines** (`frame-time-lines`, shown by default): the target (light grey) and preferred frame time (amber, only where it
      differs) as dashed stepped lines on the display time step panel (whole refreshes, `FrameTimeRounding.WholeRefreshes`, the
      analysis's rule) and the frametime panel (as written). Per hold, the values of the frame that ends it (`FrameReference`: the
      marker's, else the analysis's fallbacks, without the schedule's step or the 1 + dropped multiple); `RunChartData.StepReferences`
      / `FrameTimeReferences` are stretches of equal values, and the scales take the animating frames' values (static ones stay out).
    - **Events panel** (`events`, under the strip, at every zoom): two lanes never mixed up, **frames** (dropped by the target, out of
      order, torn) and **capture** (not recorded, dropped by the source, missed, not decoded), one mark per pixel column and lane, the
      first kind of `RunEvents.FrameKinds` / `CaptureKinds` that the column has. `RunEvents` (`RunChartData.Events`) holds each kind's
      events sorted with running totals; the capture's come from `ChartRun.Captures` (the capture rows as captures.csv has them: live from
      the report, `ChartRun.CapturesOf` once per capture; from files, `AnalysisOutput` reads captures.csv), so the report from the files
      is the live one (`ChartVideoClipTests`). The key counts each kind in the section, the description names the capture gaps by kind,
      and `CardHover` lists a column's events with the frames they name (on a card that has the panel: the GUI's Timeline has not).
      The strip has no marks.
    - **Static stretches:** a violet band (`static-band`) behind every time panel from the first static frame's display time to the next
      frame's (`RunChartData.StaticStretches`), violet strip cells (`strip-static-a`/`-b`). With `ReportOptions.ClampStatic` (the
      default; `render --no-static-clamp`, the GUI's "Clamp static" check box, `GuiSettings.ClampStatic`) the display time step and
      frametime scales leave static frames' values out: holds, the animation time step overlay, frametimes, CPU busy and the reference
      lines of holds ending at a static frame (`RunChartData.AnimatingHolds`, `AnimatingAnimationHolds`, `AnimatingFrameTimes`,
      `AnimatingCpuBusy`, `FrameTimesAndCpuBusy`, `AnimatingStepReferences`); they get edge marks with their value. Off, the `All…`
      and unfiltered sequences set the scales.
  - **The report is the same for every capture source** (it only pairs decoded markers with display times): histograms in fixed
    0.1 ms bins (`Histogram.DefaultBinWidth`), one error threshold (1 ms, `analyze --error-threshold-ms`,
    `TimelineOptions.ErrorThreshold`), and a display time step is off its target from half a refresh on. A source's precision (a
    camera's period) goes into warnings, never into the binning or the thresholds.
  - Camera captures film faster and calculate the refresh from the frames (`Capture/source/Camera/RefreshEstimator.cs`:
    `EstimatePeriodNanoseconds` finds which period from the intervals, `GridPeriodNanoseconds` asks the first-seen times which grid of refreshes
    they are on (a search of the periods with `GridFit`, the periodogram of the times; the intervals mislead a camera that sees a
    refresh in two or three frames, and the times are on no grid at exactly twice the refresh rate with half of the sightings late:
    the analysis then warns that the rate is unreliable), `RefinePeriodNanoseconds` measures it with a line through every
    first-seen time, the maximum likelihood estimate once the refresh numbers are known; also used
    by the calibration). After a change to it run `RefreshGridTests` and `selftest --experimental --camera --fps <2 x refresh> --refresh <rate>`
    for a few monitor rates. The user's expected display rate (`--display-hz`, capture.json `expectedRefreshHz`) settles an ambiguous
    estimate and is compared with the calculated rate (a capture card: with its capture rate); more than 1 %
    (`TimelineAnalyzer.RefreshTolerance`) is a warning.
  - **Capture gaps** (a capture card's captures not decoded, not recorded, dropped by the source (`CaptureRow.SourceDrops`, a count), or
    missed without a word: `MissedCaptures`, a step of 1.5 periods or more on the device clock, never the host clock): the next
    frame's first sighting is uncertain (`UncertainStart`), so the steps into and out of it are not judged (`UncertainStep`: no animation
    error, no late verdict, not in the frame rates; `RunStatistics.UncertainSteps`), and frame indices skipped across the gap are never
    called dropped. A camera decides uncertain starts itself. Out-of-order captures are kept with the newest frame
    (`PresentedFrame.OlderFrames`).
  - **A long capture's analysis sorts nanosecond counts, not doubles, and copies no rows:** the statistics gather each kind of value as
    whole nanoseconds (`NanosecondList`), sort it once with a radix sort (`NanosecondSort`) and take every number from that (`Statistics.FromSortedNanoseconds`,
    `RunStatistics.From`: to the bit what the formulas over sorted milliseconds give, which `StatisticsTests` keeps as the reference);
    never `OrderBy` or a sorted array per statistic. A run's rows are stretches of the capture's row list (`RowRanges`), not a copy (a
    row is over 200 bytes). The nanosecond lists own their arrays: rented ones would stay in the pool after the analysis.
    `AnalysisBenchmarks` (`--long-running`): the timeline of an hour at 240 Hz takes 0.35 s. Each frame is still made twice (the
    pacing pass copies it to add its values): measured as about 0.05 s and 269 MB of short-lived garbage, left as it is.
  - Late frames, the 2 s late share and the "which cause" verdict: `PacingAnalyzer` → `RunPacing` (`runs[].pacing` in
    summary.json). Every frame's target comes from, in order: the pacer's intended display times in the markers (`PacingSource.Schedule`:
    lateness against the schedule, and the animation error split into pacing and prediction error), its target frame time in the
    markers, the target frame rate (`--target-fps`, GUI "Target frame rate", stored in capture.json, overridable at analysis time),
    and otherwise one refresh (the native rate). Without a schedule, a frame after frames the target dropped (`DroppedFrames`) is due one
    target per frame later (1 + dropped): dropped, not late. The synthetic game writes a schedule; its prediction error is 0 by construction.
- **Markers (format version 1, `sdk/doc/marker-format.md`):** a 53 byte header shared by frame, start and end, its fields grouped by
  category: format (magic, version, kind), which run and frame (run id, frame index), what the frame shows (flags, animation time),
  pacing (preferred frame time, target frame time, intended display time), the CPU's work (CPU start time and CPU busy, named as
  PresentMon's CPUStartTime and MsCPUBusy). Start/end carry the values of the frame that shows them, and the tearing check compares
  their run id and frame index with the sync marker's. Every main marker (frame, start, end) is QR version 6 (41×41), so it never changes size and has room for future fields.
  - **CRC** (agreed with the user): every payload ends with the CRC-32 of zlib and Ethernet over all the bytes before it, little
    endian: a frame or end marker is 57 bytes, a start marker 81, a sync marker 20, and a field added later goes before the CRC. A
    payload whose CRC does not match is not decoded (`TryDecodePayload` false: `MarkerDecodeStatus.InvalidPayload`, the capture
    `Undecodable`): the QR code's Reed-Solomon code repairs, the CRC refuses what it repaired into other bytes. In C++ it is the
    core's `Crc32Util` (`core/Crc32Util.hpp`: a 16-entry table, for the executable size; its 256-entry form with the same result is for
    callers that want speed), in C# a private `Crc32.cs` with the 16-entry table, in Python `binascii.crc32`. The tests' expected
    CRC bytes come from Python's `binascii`, never from our own code.
  - **Times are nanoseconds** (the user, 2026-10-07 and 08; the bytes did not move, the unit did): the animation time an `i64`
    (`NanosecondTimeSpan`), the intended display and CPU start time points on the pacer's clock (`NanosecondTickCount`), the two frame
    times and CPU busy four unsigned bytes each. **A length of time is the duration type**, never negative by construction
    (`NanosecondTimeDuration`, 64-bit: what the code computes with and what a decoded payload returns), never a signed span and
    never a 32-bit time type (the four bytes are the marker's business).
    `Payload` caps a duration that is too long for its four bytes where it is made, never an error (it runs in a frame loop): CPU
    busy at `Payload::MaxCpuBusy` (`0xFFFFFFFF`, 4.294967295 s), a frame time at `Payload::MaxFrameTime` (`0xFFFFFFFE`), since
    `0xFFFFFFFF` there is `Payload::OnDemandFrameTime`, a duration of exactly 4294967295 ns. So every payload is valid on the wire
    and decodes to exactly what was encoded. Python keeps plain `int`s named `*_ns` (`ON_DEMAND_FRAME_NS`, `MAX_FRAME_NS`,
    `MAX_CPU_BUSY_NS`, `seconds_to_ns`). The start marker's start time stays C# `DateTime` UTC ticks: a calendar time does not fit
    64 bits of nanoseconds. The format version stayed 1 (changed in place): a marker from before reads without an error and a
    hundred times too small, and every recording and capture from before is invalid (the user's decision: no migration).
  - **Pacing terms:** the intended display time is the pacer's aim; the animation time is the predicted display time the game
    animated for (`sdk/doc/vocabulary.md`). The target frame time is what the pacer aims for now, the **preferred frame time** what the
    application wants (it differs only while the pacer runs slower); `0xFFFFFFFF` in both = on demand. Never call the preferred frame
    time "desired": that is Vulkan's word for a present time point.
  - **Flags:** static is a frame's time on screen (nothing animates until the next frame; the frame itself may have moved). Bit 0
    `StaticAfter` says it on the frame (known upfront: no pending work), bit 1 `StaticBefore` on the next frame (known in hindsight,
    only for frame index − 1). `TimelineAnalyzer.BuildFrames` turns both into `PresentedFrameFlags.StaticAfter` on the frame and
    `StaticBefore` on the next: that static step gets no animation or prediction error (the drift sums only judged errors), the step
    into the static frame is judged. Independent flags, not an enum; bits 2 to 7 are reserved (write 0, decoders keep them).
  - **Assumed static** (`TimelineOptions.AssumeStatic`, on by default; `analyze --no-static-guess`, the GUI's "Guess static",
    `GuiSettings.AssumeStatic`): `TimelineAnalyzer.AssumeStatic` flags a frame `StaticAfter | StaticAssumed` when a frame the target
    dropped took the rest's flag: the next presented frame carries `StaticBefore` for a frame never shown, or (the flag lost) the run
    uses static flags, the next frame is on demand or later than the frames in between were due, and the animation clock stood still
    (animation step at most one frame time per rendered frame and two refreshes short of the display step; the frame time is the next
    frame's target or preferred frame time, else the animation step into the held frame). Never across a capture gap or for a camera.
    The report's description and the hover say "assumed"; `ClipManifest.IsAssumedStatic` mirrors the rule for the clip tests.
  - **Field order:** every payload type (C++ and C# `Payload`, Python `Payload`, the tools' `MarkerPayload`) lists its fields in the
    order of the wire format: kind, run id, frame index, flags, animation time (required by every constructor; C++ also has a default
    constructor and stays trivially copyable and standard layout: private fields read through getters, `WithKind` for another kind; the
    constructor asserts the kind and `EncodePayload` refuses an unknown one, the flags are kept as given), then the optional
    preferred frame time, target frame time, intended display time, CPU start time and CPU busy. The golden CSVs' columns
    (`sdk/test-data/markers`) and the docs' field lists follow it; keep new fields in their wire position.
  - **Frame rates describe the frames that animate:** a static frame's time on screen (the next frame's display time step, flag
    `StaticBefore`) is left out of average fps, the lows and the display time step statistics and histogram
    (`RunStatistics.CountsTowardFrameRate`, `RunChartData.FrameRateSteps`), and counted (`excludedStaticFrames`); the tile, the report's
    description and the statistics tables say "excluding N static frames" (`RunHeadline.ExcludedStatic`).
  - **Late share:** amber = on screen at least half a refresh longer than the preferred frame time (the marker's, else
    `--target-fps`, else one refresh) without being late; never for static steps or on-demand frames (`LateShareData`, `PresentedFrame.PreferredFrameTime`).
  - The start marker (81 bytes) carries a 16 byte opaque sequence id (`SequenceId`: a UUID or a text tag of at
    most 16 ASCII characters, shown as text or UUID hex), not a name. Format version 1 is the baseline for all data (markers,
    captures.mbcd, analysis output): change it in place, no version bump, until there are users. The sync marker (kind 3, 20 bytes: the header's start, run id and frame index, and the CRC; matched to its main marker by both) is QR version 2 (25×25), drawn
    bottom-left: it checks tearing (capture cards, optional) and times the frames for a camera (required). `Options.RecommendedOrigin(kind, …)`
    places both; there are no other slots.
- **Camera capture (VERY EXPERIMENTAL, `measure/doc/camera.md`):**
  - The GUI hides it (the camera card, the synthetic camera source) unless **Settings → Experimental features** is on, and the CLI
    unless `--experimental` is given (see live capture above)
    (`GuiSettings.ExperimentalFeatures`, `CaptureViewModel.ExperimentalFeatures`); switched off, a camera chosen earlier is not used.
  - Every place users meet it says "very experimental": CLI help, the GUI card, `CameraRig.ExperimentalNotice` in rig files and
    analysis warnings, docs. Keep it that way until it is validated with real hardware, and keep `measure/doc/camera-status.md`
    (status, known issues, next steps) current with every camera change.
  - Saved cameras: `CameraRigLibrary` (`camera-rigs/` next to the config file; automation (DocImages) and `--output-root` GUI runs use their output root, never
    the user's library). GUI: `CameraWizardViewModel` + `CameraWizardWindow` (the wizard), `CameraRigViewModel` (the capture page card).
  - `Capture/source/Camera/`: `CameraCalibrator` (calibrate/verify), `CameraRig`/`CameraZone` (the rig file), `CameraRectifier` and
    `RectifyingCaptureSource` (C# path). `FfmpegCommandBuilder.BuildCameraFilter` is the ffmpeg path; both produce the same layout:
    zones of `CameraZone.StoredSizePx` stacked in scanout order.
  - The analysis switches to `ScanoutModel.Camera` when `capture.json` has a `camera` section.
  - Zones: `CameraZone.MainZone` (the main marker, identifies each frame) and `CameraZone.SyncZone` (the sync marker, times it:
    `TimelineAnalyzer.TimeBySyncMarker`). A camera decode only counts when every sampled module matches the decoded payload's symbol
    (`MarkerDecoder.MaxModuleMismatchFraction`).
  - The synthetic camera (`Capture/source/Synthetic/SyntheticCamera.cs`) is the ground truth. `selftest --experimental --camera --fps 1000
--refresh 60 [--tear-every 9]` runs it end to end.
  - Benchmarks: `dotnet run -c Release --project measure/tools/Benchmarks/Benchmarks.csproj -- --filter "*"`. Name the csproj: the
    folder's `.slnx` does not build the libraries optimized. The long-running ones (`[BenchmarkCategory(BenchmarkCategories.LongRunning)]`:
    `CardBenchmarks`, `PlaybackBenchmarks`, `OutputFileBenchmarks`, `AnalysisBenchmarks`, many minutes) are left out unless the run adds `--long-running`; mark a new one that takes
    minutes the same way.
  - The precision-by-camera-rate table in `measure/doc/camera.md` is generated: `python tools/camera_rate_table.py --update-doc` (runs
    `selftest --experimental --camera` per rate; selftest prints its error against the simulation for it). Rerun it after changes to the camera
    pipeline.
- **ffmpeg tests:** the end-to-end tests (`FfmpegImportTests`, category `ffmpeg`) are skipped when no ffmpeg is found; CI installs
  ffmpeg.
- **CI** (mb-quality is not available there; CI runs the same commands directly):
  - `.github/workflows/ci.yml` builds and tests C++, the CMake consumer project and .NET on Windows, Ubuntu and macOS.
  - Its other jobs: `lint` (Prettier, one type per file, license headers, ruff, basedpyright, the Python library's tests, the
    reference shaders, actionlint, the Unity package assembly), `dotnet-lint` (dotnet format as mb-quality applies it, CSharpier,
    vulnerable packages, C# coverage, C# assembly sizes), `cpp-analysis` (sanitizer tests, a minute of fuzzing, clang-format,
    clang-tidy), `cpp-size` (MSVC, GCC, Clang, AppleClang), `conan` (Windows, Ubuntu, macOS) and `semver`
    (`tools/check_semver.py`).
  - `.github/workflows/release-sdk.yml` releases the SDK (C++ archive, Unity package, Conan check) on an `sdk-v*` tag (see
    `doc/releasing.md`). A `tools-v*` tag (which must match `measure/VERSION`) also builds the self-contained tools.
  - `.github/dependabot.yml` opens weekly grouped updates for GitHub Actions, NuGet (packages and dotnet tools), npm and uv (the dev tools).
    GoogleTest and nlohmann/json (FetchContent URLs) and qrcodegen (vendored) are updated by hand.
- **Conan recipe (`sdk/cpp/conan`):**
  - `recipes/mb-framepacing/all/` holds `conanfile.py` (a component per module; options `with_marker`, `with_data`, `with_pacer`), `conandata.yml`
    (each version's release archive URL and SHA-256) and `test_package/`; `recipes/mb-framepacing/config.yml` lists the versions. It
    builds the release archive (`package_release.py`), never the checkout.
  - A version is added after its release: `python tools/add_conan_version.py <version>` (reads the release's `SHA256SUMS`), then
    commit. The release workflow tests the new archive through the recipe (`check_conan.py --released`).
  - `tools/check_conan.py` (CI `conan` job, Windows/Ubuntu/macOS) builds the recipe from this checkout's archive, using a copy of
    `sdk/cpp/conan` as a local-recipes-index remote: `conan test` of the test package with `compiler.cppstd=20`, the same with
    `with_pacer=True` (the test package then paces a frame), and a build with the core and marker modules only.
  - **Every Conan run uses a temporary `CONAN_HOME`**: never touch the user's cache (their global Conan may be another version;
    running a newer one migrates the cache). Conan is pinned in `pyproject.toml`'s dev group.
  - basedpyright excludes `sdk/cpp/conan` (Conan's API is untyped); ruff still checks the recipe. The test package's C++ is formatted
    by `check_cpp.py` (with `sdk/cpp/.clang-format`).
- **Semantic versions:** `python tools/check_semver.py` (after `dotnet tool restore`) checks the two VERSION files and compares the
  public API of every C# module (`MB.FramePacing`, `MB.FramePacing.Marker`, `MB.FramePacing.Data`) with the newest stable `sdk-v*`
  release using ApiCompat.
  `sdk/VERSION` is the next release's version: raise it in the change that alters the API (0.x: minor for any API change; from 1.0:
  major for breaking changes).
  - A version may be a pre-release: `-alpha.N`, `-beta.N` or `-rc.N` only (doc/releasing.md "Pre-releases"). CMake splits it off
    (`project()` takes the numbers; `Version.hpp` has `VersionString` and `VersionPrerelease`), .NET makes it the `VersionSuffix`, and
    Python spells it as PEP 440 does (`0.2.0b1`, by hand in `pyproject.toml` and `__version__`; `test_version.py` checks both). The
    API baseline is the newest stable release.
  - CMake reads VERSION on every configure (a normal variable; an edit reconfigures); `-D<NAME>_VERSION_OVERRIDE` builds one tree as
    another version.
- **Golden set:** if you change the marker payload or geometry, regenerate it with
  `sdk/cpp/build/<preset>/marker/marker-render --golden sdk/test-data/markers` (Windows: `sdk\cpp\build\windows\marker\Release\marker-render.exe`),
  then run the C# tests and the Python tests (`python -m unittest discover -s sdk/python -t sdk/python`).
  - The digest's seed (`marker-render`'s `WriteModuleDigest`) is one with which both symbol versions use all eight masks in its
    rows. Check that again when the payload's bytes change (the mask is in a symbol's format bits: row 8, columns 2 to 4, XOR 5)
    and take the next seed that does.
  - **A change to the payload's bytes also needs new test clips** (`measure/test-data/videos`: their markers are in the pixels, and their manifests hold `animationNs`, `cpuStartNs` and `cpuBusyNs`,
    each value rounded once from the exact time).
    mb-framepacing-explained makes them from a local, unpublished commit of this repository (it fetches a named branch into its
    submodule, exports into its own folder and never writes here). Copy the 22 folders unchanged, then `update_test_data.py`,
    DocImages and `camera_rate_table.py --update-doc`. The format change, the golden data and the clips go in one commit: `master`
    never holds clips its own tools refuse. The sister repository and the applications that embed the SDK move their pins after.
- **Verify the GUI without touching the desktop:** `dotnet run --project measure/tools/DocImages -c Release -- <scratch dir>` renders
  every page offscreen (Avalonia.Headless), imports and analyses a test clip, and runs the synthetic camera; compare the images with
  `measure/doc/images` (live numbers on the camera capture page vary). `--output-root` and DocImages (`Program.Automation`) runs never
  load or save the user's GUI settings. Do not take screenshots.

## Conventions

- **Branches:** `master` restarted on 2026-09-30 as one commit holding the tree of `legacy` 595e0f4, so its history has one folder
  structure (`sdk/`, `measure/`). All development happens on `master`, and its history stays linear. `legacy` holds the history
  before that. It is frozen (it ends in a merge of `master`, with the same tree) and may be deleted: old commit SHAs, such as
  submodule pins, only stay reachable while it exists.
- **Vocabulary:** use the terms of `sdk/doc/vocabulary.md` in code, UI text and docs. **Display time** is the moment a frame was first
  seen on screen; **display time step** is how long it stayed (the time to the next frame). Animation error = animation time step -
  display time step.
- **Versions:** there are two version files.
  - `sdk/VERSION`: the SDK, every module and language (C#: `sdk/Directory.Build.props`; Python: by hand in `pyproject.toml` and
    `mb_framepacing.__version__`). CMake reads it into `mb/framepacing/core/Version.hpp`, which only `core/source/mb/framepacing/core/GetLibraryVersion.cpp` and the
    tests include: `core/GetLibraryVersion.hpp` declares `GetLibraryVersion()`, so a version bump rebuilds one file.
  - `measure/VERSION`: the tools. `measure/Directory.Build.props` reads it.
- **One type per file:** C++ and C# use one class/struct/enum per file (nested private helpers may stay nested). The C++ public API
  has one header per type and no umbrella headers (a header that includes a whole module is a god file:
  `tools/check_one_type_per_file.py` rejects one); the functions have headers of their own. CI runs `tools/check_one_type_per_file.py`.
- **Hot path:** the marker APIs run every frame, so they must not allocate. Add zero-allocation tests for new API.
- **No X11 macro names in public C++ headers:** `Xlib.h` and `X.h` define plain words as macros (`None`, `Status`, `Bool`, `True`,
  `False`, `Success`, `Always`, `Above`, `Below`, `Complex`, ...), and Vulkan's and EGL's platform headers pull them in on Linux, so
  an enumerator `None` or a member `Status` does not compile in an application that includes them first. Every module's tests compile
  all its public headers after those macros (`mb_framepacing_add_x11_macro_check`, `sdk/cpp/testing/X11Macros.cpp.in`). The names are
  the same in every language (agreed with the user, although .NET names a flags enum's zero `None`): `MarkerFlags::NoFlags`
  (Python `NO_FLAGS`), `SwapIntervalChange::Unchanged`, the data rows' `CaptureStatus` (Python `capture_status`). Files keep their
  words: the CSV column is `status`, the pacer's golden files say `None`.
- **Reference shaders (`sdk/shaders/`, BSD):** one quad whose fragment shader finds each pixel's module, from the packed bits
  (`Bits()` as constants; the fastest way to draw the marker) or a texel per module. One folder per API: `hlsl/` (`FrameMarker.hlsl` is
  the lookup, also included by the Unity shaders; `build_upm.py` copies it into the package), `gl/`, `gles2/` (GLSL ES 1.00: no
  integers, so float arithmetic, and `#error` without `highp`: not 100 % exact with `mediump`), `vulkan/`. `python
tools/check_shaders.py` compiles them all (glslang, and DXC when found; CI runs it); `--render` (`uv run --with moderngl tools/check_shaders.py --render`, needs a
  GPU; `VULKAN_SDK` for DXC and SPIRV-Cross) draws every one and compares every pixel with `modules_to_bitmap`. Run it after touching
  a shader, and `check_in_unity.py` (also `--graphics glcore|gles|vulkan`) after touching the Unity ones.
- **A matrix is a marker's:** `ModuleMatrix::TryFromBits` (C#, and Python's `ModuleMatrix(size, bits)`) takes the two marker sizes only (41 and
  25), the ones the drawing functions and the grid know. **Seconds truncate** toward zero in every language: to the nanosecond for the
  marker (`NanosecondTimeSpan::FromSeconds`, Python's `seconds_to_ns`: 1/60 s is 16 666 666 ns), to the tick for the tick types
  (`TimeSpan::FromSeconds`, `TimeSpanUtil.FromSeconds`: 166 666 ticks).
- **Encode once, draw from the modules:** every marker library encodes a marker once (`GenerateModules` / C# `TryGenerateModules`: the
  `ModuleMatrix`, 1 bit per module, packed exactly as `modules.csv`) and draws it with `ModulesToQuads`, `ModulesToTriangles`,
  `ModulesToIndexed`, `ModulesToBitmap` or the static grid (`GridVertices` once, `ModulesToGridIndices` per frame). A drawn rectangle is a
  `MarkerQuad` (a core `Rectangle` `Rect` and `Dark`). `Options` (module size and quiet zone) is always valid: C++ asserts a value outside its range and clamps it
  without asserts, C# and Python clamp (C# stores it relative to the defaults, so `default(Options)` is `Options.Default`); the sizing
  and placement are its members (`Recommended`, `Minimum`, `MarkerSizePx`, `QuietZonePx`, `RecommendedOrigin`). Pixel formats are named
  by their channels: `R8`, `R8G8B8`, `R8G8B8A8`. The C# marker mirrors the C++ one: the wire format is the internal `WireFormat`, the symbol sizes are `ModuleMatrix`'s
  (`MainSize`, `SyncSize`, `SizeFor`, `PackedModuleByteCount`), the sizing limits `Options`', `Payload.MaxEncodedByteCount` and
  `Payload.OnDemandFrameTime` the payload's, `PixelFormatUtil.BytesPerPixel` the pixel formats'; `Payload`'s fields are typed and named
  as C++'s getters, and its constructor throws `ArgumentOutOfRangeException` for a kind that is not a `MarkerKind`. The C# static
  class is `FrameMarker` (not `Marker`: a class named like its namespace `MB.FramePacing.Marker` breaks lookups in `MB.FramePacing.*` code). C#'s `ModuleMatrix` is a `ref struct` view over the caller's bytes (no allocation), C++'s a
  value with an inline `std::array`. There are no payload-taking draw functions.
- **.NET:**
  - hand-maintained SDK csproj files;
  - package versions only in the root `Directory.Packages.props`;
  - C# style: a boxed file header, 2-space indent, block namespaces, `m_`/`g_` field prefixes, CSharpier (`.csharpierrc`, width 150).
  - SDK value types are `readonly struct`s with `public readonly` fields (derived values are expression-bodied properties) and
    `GetHashCode` through `HashCode.Combine` (a `HashCode` builder past 8 fields, `Payload`).
- **C++:** CMake 4.0+, C++20, warnings as errors, no allocations in the per-frame path, qrcodegen (C variant) vendored as the QR encoder's reference (tests and benchmarks only), GoogleTest
  through FetchContent (`FIND_PACKAGE_ARGS` lets an installed or Conan GTest win).
- **Our license is split by path** (root `LICENSE` lists it, and every commit in the history carries it):
  - BSD 3-Clause: everything under `sdk/`: the SDK's libraries in every language (`sdk/LICENSE` holds the text alone and is what the
    C++ archive, the CMake install, the Python package and the Unity package ship), their docs (`sdk/doc/`) and their golden data
    (`sdk/test-data/`).
  - PolyForm Perimeter 1.0.1: everything else (the tools, their libraries, scripts, other docs, build and CI files). The tools ship
    the root `LICENSE`, which holds both texts, since they include the BSD marker library.
  - Every source file names its license on an `SPDX-License-Identifier` line near its top: `BSD-3-Clause` under `sdk/`,
    `LicenseRef-PolyForm-Perimeter-1.0.1` elsewhere (inside the boxed C# header, after a shebang, as an XML
    comment in XAML/MSBuild/solution files). Every code file (not MSBuild, solutions or workflows) has
    `SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS` on the line right before it, in the same comment style: the SPDX
    short form (as the Linux kernel and REUSE use it), never the license text in the file. `python tools/check_license_headers.py`
    checks both (CI `lint`), `--fix` adds missing ones (the copyright with the current year). It also reports a source file that holds a NUL
    byte (git treats such a file as binary: no diffs, skipped by text searches): write `'\0'`. Third-party code (`third_party/`) keeps its own notices; each language's marker module keeps it in a `third_party/`
    folder of its own (`sdk/cpp/marker/reference/third_party/`, `sdk/python/mb_framepacing/marker/third_party/`; the Python one has its
    license text next to it, the C++ one's is in the files' headers and in `licenses/`).
  - A new file belongs to the license of its path. Moving code across that line (for example from `measure/` into `sdk/`)
    changes its license: only Mana Battery can decide that.
- **Licenses:** every third-party component (vendored, NuGet, FetchContent, test-only) needs its license text in `licenses/` and a
  row in `licenses/README.md`, in the same change.
- **Two counters:** the capture index (capture card) and the marker frame index (application) are unrelated; never compare them.
- **Photosensitivity:** the marker is a high-contrast pattern that changes every frame, so every guide that tells someone to draw it
  carries the warning (the README's workflow step 1, `integrating.md`, `unity.md`, every SDK README: `sdk/`, `cpp`, `csharp/marker`,
  `python`, `unity`, `shaders`; a pointer in `marker-format.md` "Sizing"), and `integrating.md` "Photosensitivity" has the numbers.
  `FlashGuideline` (MarkerDecoding) is WCAG 2.3.1's estimate: a quarter of a 10° field may flash (2.8 % of the screen), a fine
  pattern with squares under 0.1° (1/300 of the width) is exempt. The library default (6 px on 1080p) is at its edge; `marker-size`
  prints the numbers and says when a setup is outside. Never describe the marker as safe: the guideline lowers the risk. A new
  guide or sample that draws the marker gets the warning too.
- **GUI log:** `GuiLogging` configures NLog in code: a file per day (`gui-yyyy-MM-dd.log`, the last 7 days kept) in `logs` next to
  the GUI settings, or under the output root for `--output-root` and DocImages runs (DocImages checks it). It also logs the exceptions
  nothing else handles (UI thread, tasks, app domain); catch blocks that show an error also log it.
- **Settings files** (configuration, GUI settings, saved cameras) are written and deleted through `SettingsFile`: a unique temporary
  file, flushed to the disk, then renamed over the target. Before that, the `backup` folder next to it gets `<file>.bak` (the version
  replaced, or the deleted file) and, when the format version changes, `<file>.v<N>.bak` (the last file in format N). Only those; no
  history. Never `File.WriteAllText` or `File.Delete` a settings file directly. Deleting from the GUI asks first.
  - Every settings file has a `formatVersion` (`CurrentFormatVersion` on its type). Loaders refuse a newer format ("update the
    tools"); a file without the field counts as 1. Raise the version when the format changes incompatibly, and migrate older
    formats in the loader.
  - Errors about unreadable settings files include `SettingsFile.BackupHint(path)`.
- **ffmpeg** is an external executable, found via `--ffmpeg` / `MB_FFMPEG` / `mb-framepacing.json` / PATH / install folders
  (`FfmpegLocator`). It is never linked or bundled.
