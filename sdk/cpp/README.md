# MB Frame Pacing for C++

The C++20 library of the [mb-framepacing](https://github.com/Unarmed1000/mb-framepacing) SDK: one CMake project of modules, each its
own static library target, like Boost's and Poco's.

| Module   | Target                   | Headers                     | What it does                                                                                |
| -------- | ------------------------ | --------------------------- | ------------------------------------------------------------------------------------------- |
| `core`   | `mb_framepacing::core`   | `<mb/framepacing/core/…>`   | What every module shares: the library version, the time types, `Point`, `Rectangle`         |
| `marker` | `mb_framepacing::marker` | `<mb/framepacing/marker/…>` | Draws the frame marker into every frame of an application; no dependencies, no allocations  |
| `data`   | `mb_framepacing::data`   | `<mb/framepacing/data/…>`   | Reads the tools' capture data and analysis output; uses the marker module and nlohmann/json |
| `pacer`  | `mb_framepacing::pacer`  | `<mb/framepacing/pacer/…>`  | Off until it is reworked (`MB_FRAMEPACING_BUILD_PACER`): plans frames on the refreshes      |

No header includes a whole module: include the header of each type you use (one type per header) and the header of the functions
(`marker/FrameMarker.hpp`, `data/FramesCsv.hpp`, ...).

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

| Option                              | Default                                                                         |
| ----------------------------------- | ------------------------------------------------------------------------------- |
| `MB_FRAMEPACING_BUILD_MARKER`       | on                                                                              |
| `MB_FRAMEPACING_BUILD_DATA`         | on; off leaves the data module out, and nlohmann/json is never fetched          |
| `MB_FRAMEPACING_BUILD_PACER`        | off (the pacer is off until it is reworked)                                     |
| `MB_FRAMEPACING_BUILD_TESTS`        | on only when the library is the top-level project                               |
| `MB_FRAMEPACING_BUILD_TOOLS`        | on only when top-level (`marker-render`)                                        |
| `MB_FRAMEPACING_BUILD_BENCHMARKS`   | off (the marker's benchmarks; fetches Google Benchmark unless one is installed) |
| `MB_FRAMEPACING_WARNINGS_AS_ERRORS` | on only when top-level                                                          |

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
const FM::Payload payload(FM::MarkerKind::Frame, 1u, frameIndex, FM::MarkerFlags::None, animationTime);   // an FP::TimeSpan
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

| Function or type                                                                                                             | What it does                                                                         |
| ---------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------ |
| `Payload`, `StartMetadata`, `SequenceId`, `MarkerKind`                                                                       | What a marker carries                                                                |
| `Options` (`Recommended`, `Minimum`, `MarkerSizePx`, `QuietZonePx`, `RecommendedOrigin`)                                     | Size and place: always valid (a value outside its range asserts, else is clamped)    |
| `GenerateModules`, `ModuleMatrix` (`Size`, `IsDark`, `Bits`)                                                                 | Encode the marker: its QR symbol, 1 bit per module (211 bytes), a plain value        |
| `GridVertices`, `GridVertexCount`, `MaxGridVertexCount`, `ModulesToGridIndices`                                              | A static grid uploaded once, and per frame only the indices                          |
| `ModulesToBitmap`, `PixelFormat`, `PixelFormatUtil::BytesPerPixel`                                                           | Draw it into a pixel buffer (`[L]`, `[R, G, B]` or `[R, G, B, A]` bytes; any stride) |
| `ModulesToIndexed`, `ModulesToTriangles`, `ModulesToQuads` (`MarkerQuad`: a `Rectangle` and whether it is dark)              | Draw it as indexed triangles, a triangle list or rectangles, into your buffers       |
| `MaxTriangleVertexCount`, `MaxIndexedVertexCount`, `MaxIndexCount`, `MaxQuadCount`, `ModuleMatrix::MaxPackedModuleByteCount` | Buffer sizes that fit every marker kind                                              |
| `ModuleMatrix::SizeFor`, `MainSize`, `SyncSize`                                                                              | Modules per side of a kind's symbol                                                  |
| `EncodePayload`, `TryDecodePayload`                                                                                          | The wire format                                                                      |

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
const FD::AnalysisSummary summary = FD::ReadSummary(*analysis / FD::SummaryFileName);
for (const FD::SummaryRun& run : summary.Runs)
{
  for (const FD::FrameRow& frame : FD::ReadFrames(*analysis / run.FramesFile))
  {
    // Times are 100 ns ticks; an empty cell is an empty std::optional
    if (frame.AnimationErrorTicks)
    {
      std::printf("%llu: %.4f ms\n", static_cast<unsigned long long>(frame.FrameIndex), *frame.AnimationErrorTicks / 10'000.0);
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

Reading allocates and throws: `FD::DataFormatError` for a file it cannot read (another kind of file, damaged content, or a newer format
version, whose message says to update), `std::runtime_error` for a file it cannot open. In `MB::FramePacing::Data`, each type and
each group of functions in its own header (`<mb/framepacing/data/…>`: `AnalysisSummary.hpp` has `ReadSummary`, `FramesCsv.hpp`
`ReadFrames`, `CapturesCsv.hpp` `ReadCaptures`, `AnalysisFiles.hpp` the file names, `Milliseconds.hpp` `ParseTicks`):

| Function or type                                                            | What it does                                                       |
| --------------------------------------------------------------------------- | ------------------------------------------------------------------ |
| `CaptureDataReader`, `CaptureDataHeader`, `CaptureDataRecord`               | `captures.mbcd`: the header and the records (`TryDecodeMain`, ...) |
| `CaptureDataStatus`, `MarkerLocation`, `CaptureDataRecord::UnknownTicks`    | A record's status, where the markers are, a missing device time    |
| `ReadSummary`, `ParseSummary`, `AnalysisSummary` and the `Summary…` structs | `summary.json` (capture.json inside it as JSON text)               |
| `ReadFrames`, `FrameRow`                                                    | A run's frames CSV, by column name                                 |
| `ReadCaptures`, `CaptureCsvRow`                                             | `captures.csv`, by column name                                     |
| `FindAnalysis`, `FramesFileName`, `ParseTicks`, the file name constants     | The analysis folder, the file names, the CSV time format           |

## The pacer

The pacer module is off (`MB_FRAMEPACING_BUILD_PACER` defaults to `OFF`, Conan's `with_pacer` to `False`) until it is reworked.

```cpp
#include <mb/framepacing/pacer/animation/AnimationClock.hpp>
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
namespace PC = MB::FramePacing::Pacer;

const PC::PacerSettings settings(PC::RefreshPeriod::FromRate(60));   // required: the display's refresh period
PC::FramePacer pacer(settings);                                       // allocates its window, once
PC::AnimationClock clock(settings.Refresh());

// Every frame: what the platform knows (NowTicks required; the rest 0 = unknown), then what to apply and the marker's values
PC::FrameInput input;
input.NowTicks = nowTicks;
const PC::FrameSchedule schedule = pacer.BeginFrame(input);   // SwapInterval, IntendedDisplayTicks, EarliestPresentTicks, ...
const PC::AnimationTime animation = clock.Advance(schedule);  // the frame's animation time
const uint32_t cpuBusy = pacer.EndFrame({presentTicks});      // as you draw the marker, just before Present
```

The pacer is values in, values out: it calls no platform API and never reads a clock. `BeginFrame`, `EndFrame` and the
`AnimationClock` never allocate. [The frame pacer](https://github.com/Unarmed1000/mb-framepacing/blob/master/sdk/doc/pacer.md) (a
release archive's `doc/pacer.md`) has the frame loop for every way of presenting, the platform values, the rule and every setting.
In `MB::FramePacing::Pacer`, each type in its own header (`<mb/framepacing/pacer/…>`):

| Type                                                    | What it is                                                                                                   |
| ------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ |
| `FramePacer`                                            | Plans every frame (`BeginFrame`, `EndFrame`, `SetRefreshPeriod`, `Reset`, `Window`)                          |
| `PacerSettings`, `SlowDownRule`                         | The refresh period (required) and the rule's settings; always valid                                          |
| `RefreshPeriod`                                         | The refresh period exact to 2⁻³² tick (`FromRate`, `FromNanoseconds`, `FromTicks`); always valid, no default |
| `FrameInput`, `FrameSchedule`, `FrameEnd`               | What goes in and comes out every frame                                                                       |
| `SwapIntervalRule`, `SwapIntervalChange`, `WindowState` | The adaptive swap interval rule on its own, for a frame loop of your own                                     |
| `AnimationClock`, `AnimationTime`                       | The animation time in whole refreshes: with the pacer (`Advance`) or measured (`AdvanceMeasured`)            |

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

## Build and test

```sh
cmake --preset windows && cmake --build --preset windows && ctest --preset windows   # or linux, linux-clang, linux-sanitize, macos
```

Every module has its tests (`<module>/tests`); they fetch GoogleTest (an installed or Conan GTest wins). `marker-render`
(`marker/tools/marker-render`) writes marker images (PGM) for any payload, to compare your renderer's output pixel by pixel;
`marker-render --golden <dir>` writes the golden set the other libraries are tested against. The pacer's tests hold a simulation of a
frame loop (`pacer/tests/simulation`, test code, not part of the library) and `pacer-sim` (`pacer/tests/pacer-sim`, built with the
tests), which paces a scenario with it; `pacer-sim --golden <dir>` writes the pacer's golden results. `tests/consumer` is a project that uses
the library every documented way (`tests/consumer/check_consumers.py`).

The marker's benchmarks (`marker/benchmarks`, Google Benchmark; `-DMB_FRAMEPACING_BUILD_BENCHMARKS=ON`, on in the `windows` and
`linux-sanitize` presets) time every per-frame step: encoding (`EncodePayload`, `GenerateModules` for a frame, start and sync marker),
every way to draw a matrix (quads, triangles, indexed triangles, the static grid and its indices, bitmaps in every pixel format) and a
whole frame (encode and draw the main and the sync marker). Run a Release build:

```sh
build/windows/marker/Release/mb_framepacing_marker_benchmarks   # --benchmark_filter=GenerateModules for one group
```

## License

BSD 3-Clause (`LICENSE`). The QR encoder (`marker/third_party/qrcodegen`) is the QR Code generator library by Project Nayuki, MIT;
the data module parses JSON with nlohmann/json, MIT; the tests use GoogleTest (BSD 3-Clause), which is not part of the library. Their
license texts are in a release archive's `licenses/` folder (the repository's root `licenses/`).
