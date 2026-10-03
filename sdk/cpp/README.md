# MB Frame Pacing for C++

The C++20 library of the [mb-framepacing](https://github.com/Unarmed1000/mb-framepacing) SDK: one CMake project of modules, each its
own static library target, like Boost's and Poco's.

| Module   | Target                   | Headers                     | What it does                                                                                                           |
| -------- | ------------------------ | --------------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| `core`   | `mb_framepacing::core`   | `<mb/framepacing/core/…>`   | What every module shares: the library version, the time types, `Point`, `Rectangle`                                    |
| `marker` | `mb_framepacing::marker` | `<mb/framepacing/marker/…>` | Draws the frame marker into every frame of an application; no dependencies, no allocations                             |
| `data`   | `mb_framepacing::data`   | `<mb/framepacing/data/…>`   | Reads the tools' capture data and analysis output; uses the marker module and nlohmann/json                            |
| `pacer`  | `mb_framepacing::pacer`  | `<mb/framepacing/pacer/…>`  | **Experimental**, off by default (`MB_FRAMEPACING_BUILD_PACER`): paces a frame loop with a steady clock and vsync only |

No header includes a whole module: include the header of each type you use (one type per header) and the header of the functions
(`marker/FrameMarker.hpp`, `data/analysis/FramesCsv.hpp`, ...).

The headers compile after X11's (`Xlib.h` defines `None`, `Status`, `Bool` and more as macros, and Vulkan's and EGL's platform
headers pull it in on Linux): no name in them is one of those words, which is why the flags' zero value is `MarkerFlags::NoFlags`.

The **marker** draws a small QR code into every frame that carries the frame index and the animation time. A capture of the display
output, analysed with the mb-framepacing tools, then shows the **animation error**: how far what the application animated is from
what was actually shown on screen. The format is specified in
[marker-format.md](https://github.com/Unarmed1000/mb-framepacing/blob/master/sdk/doc/marker-format.md); the full guide is
[Integrating the marker](https://github.com/Unarmed1000/mb-framepacing/blob/master/sdk/doc/integrating.md), and what to write in each
field is in [Filling the marker fields](https://github.com/Unarmed1000/mb-framepacing/blob/master/sdk/doc/marker-fields.md) (all three
are also in a release archive's `doc/` folder), and the shaders that draw it fastest in
[`sdk/shaders`](https://github.com/Unarmed1000/mb-framepacing/tree/master/sdk/shaders) (a release archive's `shaders/`).

The **data** module reads the capture data (`captures.mbcd`,
[format](https://github.com/Unarmed1000/mb-framepacing/blob/master/sdk/doc/capture-data-format.md)) and the analysis output
(`summary.json`, `captures.csv`, `run-<id>-frames.csv`,
[format](https://github.com/Unarmed1000/mb-framepacing/blob/master/sdk/doc/analysis-output-format.md)). It reads what the C# module
reads: the tests check it against the same golden data (`test-data/data`). summary.json is parsed with
[nlohmann/json](https://github.com/nlohmann/json), inside the module only: it is not part of the API.

> **Photosensitivity warning.** The marker is a high-contrast pattern that changes every frame, and flickering patterns can
> trigger seizures in people with photosensitive epilepsy. Draw it in test builds only; see
> [Photosensitivity](https://github.com/Unarmed1000/mb-framepacing/blob/master/sdk/doc/integrating.md#photosensitivity).

## Add it

The release archive, pinned by its hash (the release page lists it in `SHA256SUMS`):

```cmake
include(FetchContent)
FetchContent_Declare(mb_framepacing
  URL https://github.com/Unarmed1000/mb-framepacing/releases/download/sdk-v0.1.0/mb-framepacing-cpp-0.1.0.tar.gz
  URL_HASH SHA256=<from SHA256SUMS>
  FIND_PACKAGE_ARGS 0.1 CONFIG COMPONENTS marker)      # an installed copy of a compatible version wins
FetchContent_MakeAvailable(mb_framepacing)
target_link_libraries(my_game PRIVATE mb_framepacing::marker)
```

Or git (`GIT_TAG sdk-v0.1.0`, `SOURCE_SUBDIR sdk/cpp`), `add_subdirectory` of this folder, an installed copy with
`find_package(mb_framepacing 0.1 CONFIG REQUIRED COMPONENTS marker data)`, or Conan 2 (`mb-framepacing/0.1.0` from the recipe in
[`conan/`](conan), see the guide). Every way gives the same targets: link the modules you use.

| Option                              | Default                                                                                                                   |
| ----------------------------------- | ------------------------------------------------------------------------------------------------------------------------- |
| `MB_FRAMEPACING_BUILD_MARKER`       | on                                                                                                                        |
| `MB_FRAMEPACING_BUILD_DATA`         | on; off leaves the data module out, and nlohmann/json is never fetched                                                    |
| `MB_FRAMEPACING_BUILD_PACER`        | off: the pacer is experimental; on builds `mb_framepacing::pacer`                                                         |
| `MB_FRAMEPACING_BUILD_TESTS`        | on only when the library is the top-level project                                                                         |
| `MB_FRAMEPACING_BUILD_TOOLS`        | on only when top-level (`marker-render`)                                                                                  |
| `MB_FRAMEPACING_INSTALL`            | on only when top-level: the install rules and the CMake package. Off, your own `cmake --install` installs your files only |
| `MB_FRAMEPACING_BUILD_BENCHMARKS`   | off (the marker's benchmarks; fetches Google Benchmark unless one is installed)                                           |
| `MB_FRAMEPACING_BUILD_FUZZERS`      | off (the libFuzzer target of the marker's QR encoder; needs Clang)                                                        |
| `MB_FRAMEPACING_WARNINGS_AS_ERRORS` | on only when top-level                                                                                                    |

nlohmann/json is found with `find_package(nlohmann_json 3.12)` when installed, and downloaded (a pinned release) otherwise; GoogleTest
likewise, only for the tests.

## The marker

```cpp
#include <mb/framepacing/core/Point.hpp>
#include <mb/framepacing/marker/FrameMarker.hpp>
#include <mb/framepacing/marker/geometry/ModuleMatrix.hpp>
#include <mb/framepacing/marker/Options.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
#include <mb/framepacing/marker/geometry/Vertex.hpp>
namespace FP = MB::FramePacing;
namespace FM = MB::FramePacing::Marker;

// Once: output 1920x1080, capture stored at 960x540 (2:1)
const auto options = FM::Options::Recommended(1080, 540);   // 6 px modules, the recommended quiet zone
const FP::Point origin = options.RecommendedOrigin(FM::MarkerKind::Frame, 1080, /*alignPx*/ 2);
std::array<FM::Vertex, FM::MaxGridVertexCount()> grid;
const std::size_t gridCount = FM::GridVertices(FM::MarkerKind::Frame, options, origin, grid);
UploadVertices(grid.data(), gridCount);   // your renderer: a static vertex buffer, (X, Y) in pixels, color (Luma, Luma, Luma)
FM::ModuleMatrix matrix;
std::array<uint32_t, FM::MaxIndexCount()> indices;

// Every frame, last (after post effects and UI), without blending:
const FM::Payload payload(FM::MarkerKind::Frame, 1u, frameIndex, FM::MarkerFlags::NoFlags, animationTime);   // an FP::TimeSpan
FM::GenerateModules(payload, matrix);                                      // encode once
const std::size_t count = FM::ModulesToGridIndices(matrix, indices);       // only the indices change
DrawIndexed(indices.data(), count);      // triangles over the static vertices
```

This is the most efficient way without a dedicated shader; the shaders (1 and 2 in the SDK's
[ways to draw it](https://github.com/Unarmed1000/mb-framepacing/blob/master/sdk/README.md#ways-to-draw-it-most-efficient-first)) are
faster still.

- **Frame index:** the frame's own index, the same for every refresh the frame stays on screen.
- **Flags (optional):** `MarkerFlags::StaticAfter` on a frame when nothing animates while it is on screen, or `MarkerFlags::StaticBefore`
  on the next frame when that is only known then (the analysis does not judge the step out of the static frame).
- **Animation time:** the moment the frame shows, as the application animated it, in 100 ns ticks (`MB::FramePacing::TimeSpan::TicksPerSecond`).
- **Frame pacing (optional):** `PreferredFrameTime` (a `TimeSpan32`: the interval the application wants to run at; it differs from the target only
  while the pacer runs slower than wanted), `TargetFrameTime` (a `TimeSpan32`: the interval the pacer aims for, `166'667` ticks for 60 fps) and
  `IntendedDisplayTime` (a `TickCount64`: when the pacer intends the frame to be shown on its steady clock, any epoch; the SDK never
  reads a clock: `TickCount64::FromNanoseconds`, `TickCount64::FromCounter` or `core/time/ChronoConversion.hpp` convert yours). `0` = unknown, `Payload::OnDemandFrameTime` = frames only when something changes.
- **CPU start time and CPU busy (optional):** `CpuStartTime` (a `TickCount64`: when the CPU started working on the frame, on the same clock,
  PresentMon's `CPUStartTime`) and `CpuBusy` (a `TimeSpan32`: how long until Present, PresentMon's `MsCPUBusy`). `0` = unknown.
- **Start and end:** bracket the part to measure with a payload of kind `MarkerKind::SequenceStart`, encoded with its metadata
  (`GenerateModules(payload, matrix, {utcTicks, sequenceId})`), and a payload of kind `MarkerKind::SequenceEnd`, each shown for a few
  frames. The sequence id is 16 opaque bytes unique to the run: a UUID's bytes, or a text tag of up to 16 printable ASCII characters
  (`SequenceId::TryFromText`).
- **Sync marker (optional; required for camera capture):** a small second marker with only the run id and frame index, drawn
  bottom-left (`options.RecommendedOrigin(MarkerKind::Sync, …)`) with a payload of kind `MarkerKind::Sync`.
- **Size:** every main marker (frame, start, end) is QR version 6, 41×41 modules, so it never changes size:
  `options.MarkerSizePx()`. The sync marker is QR version 2, 25×25 modules.

In `MB::FramePacing::Marker`: the functions in `<mb/framepacing/marker/FrameMarker.hpp>`, each type in its own header (`<mb/framepacing/marker/…>`):

| Function or type                                                                                                                                              | What it does                                                                                                                        |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| `Payload`, `StartMetadata`, `SequenceId`, `MarkerKind`, `MarkerFlags` (`HasFlag`)                                                                             | What a marker carries                                                                                                               |
| `Options` (`Recommended`, `Minimum`, `MarkerSizePx`, `QuietZonePx`, `RecommendedOrigin`)                                                                      | Size and place: always valid (a value outside its range asserts, else is clamped)                                                   |
| `GenerateModules`, `ModuleMatrix` (`Size`, `IsDark`, `Bits`, `TryFromBits`)                                                                                   | Encode the marker: its QR symbol, 1 bit per module (211 bytes of bits), a plain value; `TryFromBits` makes one from a marker's bits |
| `GridVertices`, `GridVertexCount`, `MaxGridVertexCount`, `ModulesToGridIndices`                                                                               | A static grid uploaded once, and per frame only the indices                                                                         |
| `ModulesToBitmap`, `PixelFormat`, `PixelFormatUtil::BytesPerPixel`                                                                                            | Draw it into a pixel buffer (`[L]`, `[R, G, B]` or `[R, G, B, A]` bytes; any stride)                                                |
| `ModulesToIndexed` (`IndexedCount`), `ModulesToTriangles` (`Vertex`: `X`, `Y`, `Luma`), `ModulesToQuads` (`MarkerQuad`: a `Rectangle` and whether it is dark) | Draw it as indexed triangles, a triangle list or rectangles, into your buffers                                                      |
| `MaxTriangleVertexCount`, `MaxIndexedVertexCount`, `MaxIndexCount`, `MaxQuadCount`, `ModuleMatrix::MaxPackedModuleByteCount`                                  | Buffer sizes that fit every marker kind                                                                                             |
| `ModuleMatrix::SizeFor`, `MainSize`, `SyncSize`                                                                                                               | Modules per side of a kind's symbol                                                                                                 |
| `EncodePayload`, `TryDecodePayload`                                                                                                                           | The wire format                                                                                                                     |

Every function is `noexcept` and never allocates; it returns 0 (`{0, 0}`, false) when the matrix is empty or a buffer is too small. One encode can feed several outputs (a mesh for the game, a bitmap for a UI).

## The data

```cpp
#include <mb/framepacing/data/analysis/AnalysisFiles.hpp>
#include <mb/framepacing/data/analysis/AnalysisSummary.hpp>
#include <mb/framepacing/data/capture/CaptureDataReader.hpp>
#include <mb/framepacing/data/analysis/FramesCsv.hpp>
#include <mb/framepacing/marker/payload/Payload.hpp>
namespace FD = MB::FramePacing::Data;

const auto analysis = FD::FindAnalysis(captureFolder);   // the capture folder's analysis folder (or the folder itself)
if (!analysis)
{
  return;   // neither holds a summary.json: analyse the capture first
}
const FD::AnalysisSummary summary = FD::ReadSummary(*analysis / FD::SummaryFileName);
for (const FD::SummaryRun& run : summary.Runs)
{
  for (const FD::FrameRow& frame : FD::ReadFrames(*analysis / run.FramesFile))
  {
    // Points in time are TickCount64s, spans TimeSpans; an empty cell is an empty std::optional
    if (frame.AnimationError)
    {
      std::printf("%llu: %.4f ms\n", static_cast<unsigned long long>(frame.FrameIndex), frame.AnimationError->TotalMilliseconds());
    }
  }
}

FD::CaptureDataReader reader(captureFolder / FD::CaptureDataReader::FileName);
for (const FD::CaptureDataRecord& record : reader.ReadAll())
{
  MB::FramePacing::Marker::Payload payload;
  if (record.TryDecodeMain(payload))
  {
    // payload.FrameIndex(), payload.AnimationTime(), ...
  }
}
```

The times are typed: points in time are `TickCount64`s, on the capture's clock (a frame's `FirstSeenTime`, a record's `HostTime`
and `DeviceTime`, empty when the device gave none) or the frame pacer's (`IntendedDisplayTime`, `CpuStartTime`); spans are
`TimeSpan`s (`DisplayDelta`, `AnimationError`, ...); the marker's own 32-bit values (`MarkerTargetFrameTime`, `CpuBusy`) are
`TimeSpan32`s.

Reading allocates and throws: `FD::DataFormatError` for a file it cannot read (another kind of file, damaged content, or a newer format
version, whose message says to update), `std::runtime_error` for a file it cannot open. In `MB::FramePacing::Data`, each type and
each group of functions in its own header (`<mb/framepacing/data/…>`: `AnalysisSummary.hpp` has `ReadSummary`, `FramesCsv.hpp`
`ReadFrames`, `CapturesCsv.hpp` `ReadCaptures`, `AnalysisFiles.hpp` the file names). The files hold every time as whole 100 ns
ticks, in the integer type it has, so a value is read exactly as it was written:

| Function or type                                                                                                                            | What it does                                                                                                       |
| ------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| `CaptureDataReader`, `CaptureDataHeader`, `CaptureDataRecord`                                                                               | `captures.mbcd`: the header and the records (`TryDecodeMain`, ...)                                                 |
| `CaptureDataStatus`, `MarkerLocation`                                                                                                       | A record's status, where the markers are                                                                           |
| `ReadSummary`, `ParseSummary`, `AnalysisSummary`, the `Summary…` structs and `ValueStatistics`                                              | `summary.json` (capture.json inside it as JSON text)                                                               |
| `ReadFrames`, `FrameRow`, `OlderFrame`                                                                                                      | A run's frames CSV, by column name                                                                                 |
| `ReadCaptures`, `CaptureCsvRow`                                                                                                             | `captures.csv`, by column name                                                                                     |
| `FindAnalysis`, `RunFilePrefix`, `FramesFileName`, the file name constants (`AnalysisDirectoryName`, `SummaryFileName`, `CapturesFileName`) | The analysis folder and the file names                                                                             |
| `DataFormatError`                                                                                                                           | Thrown for content that is not the format: another kind of file, a newer format version, a value outside its field |

## The pacer (experimental)

> **Experimental.** The pacer is checked against its own simulation only: no capture of it on a real swap chain has been analysed
> yet. It is off by default (`-DMB_FRAMEPACING_BUILD_PACER=ON` builds it, Conan's `with_pacer=True`), and its API may change in any
> release.

```cpp
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

PC::PacerSettings settings(PC::RefreshPeriod::FromRate(60));   // required: the display's refresh period
settings.SetPreferredFrameRate(30);                            // optional: a target frame rate
PC::FramePacer pacer(settings);                                // allocates its frame window, once

// Every frame: the time it starts on your steady clock (an FP::TickCount64) in, the plan out
const PC::FrameSchedule schedule = pacer.BeginFrame(now);      // SwapInterval, AnimationTime, IntendedDisplayTime, ...
const FP::TimeSpan32 cpuBusy = pacer.EndFrame(presentTime);    // as you draw the marker, just before Present
```

The pacer needs a steady clock and a `Present` that waits for vsync, nothing else: a baseline for any platform. Where the platform
reports when frames were shown, the application can pass that on (present feedback, optional). It is values in, values
out: it calls no platform API and never reads a clock, and `BeginFrame` and `EndFrame` never allocate.
[The frame pacer](https://github.com/Unarmed1000/mb-framepacing/blob/master/sdk/doc/pacer.md) (a release archive's `doc/pacer.md`) has
the frame loop, how to apply the schedule, the target frame rate, the rule, every setting, and what newer platforms offer that it does
not use yet. In `MB::FramePacing::Pacer`, each type in its own header (`<mb/framepacing/pacer/…>`):

| Type                                                         | What it is                                                                                                                                     |
| ------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| `FramePacer`                                                 | Paces every frame (`BeginFrame`, `EndFrame`, `AddPresentFeedback`, `SetRefreshPeriod`, `SetSettings`, `Reset`, `FrameWindow`, `FeedbackState`) |
| `PacerSettings`, `SlowDownRule`                              | The refresh period (required), the target frame rate and the rule's settings; always valid                                                     |
| `RefreshPeriod`, `RefreshTime`                               | The refresh period, exact (`FromRate`, `FromNanoseconds`, `FromTimeSpan`; always valid, no default), and a time counted in whole refreshes     |
| `FrameSchedule`                                              | What a frame gets: its id, its swap interval, its animation time and the marker's pacing values                                                |
| `PresentFeedback`, `PresentResult`, `PresentFeedbackState`   | Optional: what the platform measured for an earlier frame (its display time, or that it was not shown), and what became of the feedback        |
| `SwapIntervalRule`, `SwapIntervalChange`, `FrameWindowState` | The adaptive swap interval rule on its own, for a frame loop of your own                                                                       |
| `PacerRefreshClock`, `AnimationTime`, `FrameMeasurement`     | The part that measures the frame starts and counts refreshes, for an application that decides its swap interval itself                         |
| `FramesInFlight`, `MeasuredFrame`                            | The part that measures the frames by their display times (present feedback), for the same application                                          |

## The core

In `MB::FramePacing`, each in its own header (`<mb/framepacing/core/…>`, the time types in `core/time/`):

| Function or type                                                                  | What it does                                                                                         |
| --------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| `GetLibraryVersion`, `LibraryVersion`; `core/Version.hpp`                         | The linked library's version; at compile time, for `#if` and `static_assert`                         |
| `TimeSpan`                                                                        | An interval in 100 ns ticks (the SDK's unit), C#'s `System.TimeSpan`: out of range throws, as in C#  |
| `TickCount64`                                                                     | A point on your steady clock in ticks (`FromNanoseconds`, `FromCounter` for QueryPerformanceCounter) |
| `TickCount32`                                                                     | A point on a 32-bit clock of ticks that wraps every 429.5 s; compares correctly across the wrap      |
| `TimeSpan32`                                                                      | An unsigned 32-bit interval of 0 to 429.5 s: the form of the marker's 32-bit intervals               |
| `core/time/ChronoConversion.hpp` (optional): `TickDuration`, `ToDateTimeTicks`, … | `std::chrono` conversions, and the wall clock as C# `DateTime` ticks                                 |
| `Point`, `Rectangle`                                                              | A pixel position; an integer pixel rectangle, always valid (a negative size is 0)                    |
| `ByteSpanUtil`: `WriteLE`, `ReadLE<T>`                                            | Little-endian values in byte spans, the byte count from the type                                     |

## What it adds to your executable

The modules are static libraries: an application pays for what it calls, when its linker drops the rest (`/OPT:REF` with MSVC, on
in Release; `-Wl,--gc-sections` with GCC and Clang; `-Wl,-dead_strip` on macOS; the libraries are built with a section per function
for it). The size probes (`tests/size`) are small programs that each use one module more: the core's time types and `ByteSpanUtil`,
the marker's per-frame path (`GenerateModules` and `ModulesToTriangles`; every other drawing function adds a little), and the data
module as a reader uses it (`summary.json`, the frames CSVs and `captures.mbcd`, with nlohmann/json and `std::filesystem`).

The table gives what each probe loads from its file, minus the same program without the SDK: the sum of the executable's sections
(code, constants and initialised data). The file itself grows by more or by less, since its parts are padded to whole pages: 4 KiB
on Linux, 16 KiB on macOS arm64, 512 bytes on Windows.

<!-- sdk-size-table: generated by tools/measure_sdk_size.py -->

| Toolchain               | Compiler                   | Build      |    Core | Core + marker | Core + marker + data |
| ----------------------- | -------------------------- | ---------- | ------: | ------------: | -------------------: |
| MSVC, Windows x64       | MSVC 19.51.36260.0         | Release    | 4.1 KiB |      20.4 KiB |            220.5 KiB |
| MSVC, Windows x64       | MSVC 19.51.36260.0         | MinSizeRel | 4.3 KiB |      18.1 KiB |            192.6 KiB |
| GCC, Linux x64          | GNU 13.3.0                 | Release    | 1.9 KiB |      17.3 KiB |            214.0 KiB |
| GCC, Linux x64          | GNU 13.3.0                 | MinSizeRel | 2.0 KiB |      16.0 KiB |            141.5 KiB |
| Clang, Linux x64        | Clang 18.1.3               | Release    | 1.7 KiB |      19.8 KiB |            186.4 KiB |
| Clang, Linux x64        | Clang 18.1.3               | MinSizeRel | 1.8 KiB |      16.0 KiB |            152.9 KiB |
| AppleClang, macOS arm64 | AppleClang 21.0.0.21000101 | Release    | 0.7 KiB |      15.7 KiB |            147.0 KiB |
| AppleClang, macOS arm64 | AppleClang 21.0.0.21000101 | MinSizeRel | 1.0 KiB |      14.2 KiB |            127.8 KiB |

<!-- /sdk-size-table -->

`python tools/measure_sdk_size.py --toolchain <msvc-x64 | gcc-x64 | clang-x64 | appleclang-arm64> --update-doc` (run in the
repository's root: the script is in its `tools/`, not in a release archive) measures with the compiler CMake finds and rewrites that toolchain's rows. CI measures all four and fails when a number is off by more than 10 % (and at
least 4 KiB), so a change in size comes with this table updated (`--update-doc` also takes CI's measurement files).

## Build and test

```sh
cmake --preset windows && cmake --build --preset windows && ctest --preset windows   # or linux, linux-clang, linux-sanitize, macos
```

The `windows` and `linux-sanitize` presets also build and test the experimental pacer (`MB_FRAMEPACING_BUILD_PACER`, off by default).

Every module has its tests (`<module>/tests`); they fetch GoogleTest (an installed or Conan GTest wins). What they share, such as the
allocation counter of the zero-allocation tests, is in `testing` (test code, never installed). `marker-render`
(`marker/tools/marker-render`) writes marker images (PGM) for any payload, to compare your renderer's output pixel by pixel;
`marker-render --golden <dir>` writes the golden set the other libraries are tested against. The pacer's tests hold a simulation of a
frame loop (`pacer/tests/simulation`, test code, not part of the library) and `pacer-sim` (`pacer/tests/pacer-sim`, built with the
tests when the pacer is built: `-DMB_FRAMEPACING_BUILD_PACER=ON`), which paces a scenario with it; `pacer-sim --golden <dir>` writes the pacer's golden results. `tests/consumer` is a project that uses
the library every documented way (`tests/consumer/check_consumers.py`).

The marker's benchmarks (`marker/benchmarks`, Google Benchmark; `-DMB_FRAMEPACING_BUILD_BENCHMARKS=ON`, on in the `windows` and
`linux-sanitize` presets) time every per-frame step: encoding (`EncodePayload`, `GenerateModules` for a frame, start and sync marker),
every way to draw a matrix (quads, triangles, indexed triangles, the static grid and its indices, bitmaps in every pixel format) and a
whole frame (encode and draw the main and the sync marker). `QrcodegenEncode` is the QR encoder the module's own replaced, with the
same payloads: [Encoding performance](https://github.com/Unarmed1000/mb-framepacing/blob/master/sdk/doc/encoding-performance.md) (a
release archive's `doc/encoding-performance.md`) has the numbers and how they came about. Run a Release build:

```sh
build/windows/marker/Release/mb_framepacing_marker_benchmarks   # --benchmark_filter=GenerateModules for one group
```

## License

BSD 3-Clause (`LICENSE`). The QR encoder is a port of the QR Code generator library by Project Nayuki, MIT (vendored in
`marker/reference/third_party/qrcodegen`, which only the tests and benchmarks build, to compare it with);
the data module parses JSON with nlohmann/json, MIT; the tests use GoogleTest (BSD 3-Clause) and the benchmarks Google Benchmark
(Apache 2.0), which are not part of the library. Their
license texts are in a release archive's `licenses/` folder (the repository's root `licenses/`).
