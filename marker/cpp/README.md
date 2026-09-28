# MB Frame Marker for C++

Draws the [mb-framepacing](https://github.com/Unarmed1000/mb-framepacing) frame marker into every frame of an application: a small
QR code that carries the frame index and the animation time. A capture of the display output, analysed with the mb-framepacing
tools, then shows the **animation error**: how far what the application animated is from what was actually shown on screen.

C++20, CMake 4.0+, no dependencies. It gives you pixel aligned geometry for any renderer and never allocates. The format is specified
in [marker-format.md](https://github.com/Unarmed1000/mb-framepacing/blob/master/doc/marker-format.md); the full guide is
[Integrating the marker](https://github.com/Unarmed1000/mb-framepacing/blob/master/doc/integrating.md) (both are also in a release
archive's `doc/` folder).

## Add it

The release archive, pinned by its hash (the release page lists it in `SHA256SUMS`):

```cmake
include(FetchContent)
FetchContent_Declare(mb_framemarker
  URL https://github.com/Unarmed1000/mb-framepacing/releases/download/marker-v0.1.0/mb-framemarker-cpp-0.1.0.tar.gz
  URL_HASH SHA256=<from SHA256SUMS>
  FIND_PACKAGE_ARGS 0.1 CONFIG)      # an installed copy of a compatible version wins
FetchContent_MakeAvailable(mb_framemarker)
target_link_libraries(my_game PRIVATE mb::framemarker)
```

Or git (`GIT_TAG marker-v0.1.0`, `SOURCE_SUBDIR marker/cpp`), `add_subdirectory` of this folder, or an installed copy with
`find_package(mb_framemarker 0.1 CONFIG REQUIRED)`. Every way gives one static library target, `mb::framemarker`. When the
library is not the top-level project its tests, tools and warnings-as-errors are off (`MB_FRAMEMARKER_BUILD_TESTS`,
`MB_FRAMEMARKER_BUILD_TOOLS`, `MB_FRAMEMARKER_WARNINGS_AS_ERRORS`).

## Quick start

```cpp
#include <mb/framemarker/FrameMarker.hpp>
namespace FM = MB::FrameMarker;

// Once: output 1920x1080, capture stored at 960x540 (2:1)
const FM::Options options{FM::RecommendModuleSizePx(1080, 540), FM::RecommendedQuietZoneModules};
const FM::Point origin = FM::RecommendedOrigin(FM::MarkerKind::Frame, 1920, 1080, options, /*alignPx*/ 2);
std::array<FM::Vertex, FM::MaxTriangleVertexCount()> vertices;

// Every frame, last (after post effects and UI), without blending:
const FM::Payload payload{frameIndex, animationTicks, /*runId*/ 1};
const std::size_t count = FM::GenerateTriangles(payload, options, origin, vertices);
DrawTriangles(vertices.data(), count);   // your renderer: (X, Y) in pixels, color (Luma, Luma, Luma)
```

- **Frame index:** the frame's own index, the same for every refresh the frame stays on screen.
- **Animation time:** the moment the frame shows, as the application animated it, in 100 ns ticks (`FM::TicksPerSecond`).
- **Frame pacing (optional):** `IntendedDisplayTicks` (when the pacer intends the frame to be shown, 100 ns ticks on its steady
  clock, any epoch) and `TargetFrameTicks` (the interval it aims for: `166'667` for 60 fps). `0` = unknown.
- **CPU start time and CPU busy (optional):** `CpuStartTicks` (when the CPU started working on the frame, on the same clock,
  PresentMon's `CPUStartTime`) and `CpuBusyTicks` (how long until Present, PresentMon's `MsCPUBusy`). `0` = unknown.
- **Start and end:** bracket the part to measure with `GenerateStartTriangles(payload, {utcTicks, sequenceId}, …)` and a payload
  of kind `MarkerKind::SequenceEnd`, each shown for a few frames. The sequence id is 16 opaque bytes unique to the run: a UUID's
  bytes, or a text tag of up to 16 printable ASCII characters (`SequenceId::TryFromText`).
- **Sync marker (optional; required for camera capture):** a small second marker with only the frame index, drawn bottom-left
  (`RecommendedOrigin(MarkerKind::Sync, …)`) with a payload of kind `MarkerKind::Sync`.
- **Size:** every main marker (frame, start, end) is QR version 6, 41×41 modules, so it never changes size:
  `MarkerSizePx(options)`. The sync marker is QR version 2, 25×25 modules.

## API

Everything is declared by `<mb/framemarker/FrameMarker.hpp>` in `MB::FrameMarker`, one header per type.

| Function or type                                                                   | What it does                                                                 |
| ---------------------------------------------------------------------------------- | ---------------------------------------------------------------------------- |
| `Payload`, `StartMetadata`, `SequenceId`, `MarkerKind`                             | What a marker carries                                                        |
| `Options`, `Point`                                                                 | Size and place                                                               |
| `GenerateTriangles`, `GenerateIndexed`, `GenerateQuads` (+ `GenerateStart…`)       | The marker as a triangle list, indexed triangles or quads, into your buffers |
| `MaxTriangleVertexCount`, `MaxIndexedVertexCount`, `MaxIndexCount`, `MaxQuadCount` | Buffer sizes that fit every marker kind                                      |
| `QuadsToTriangles`, `QuadsToIndexed`                                               | Convert quads                                                                |
| `MarkerSizePx`, `QrModuleCountFor`, `RecommendedOrigin`                            | Sizing and placement                                                         |
| `MinimumModuleSizePx`, `RecommendModuleSizePx`                                     | Module size for a capture's scaling                                          |
| `EncodePayload`, `TryDecodePayload`, `ToDateTimeTicks`                             | The wire format and its time units                                           |
| `GenerateModules`, `ModuleMatrix`                                                  | The QR module matrix                                                         |

Every function is `noexcept` and returns 0 (or `{0, 0}`) when the options are invalid or a buffer is too small.

## Build and test

```sh
cmake --preset windows && cmake --build --preset windows && ctest --preset windows   # or linux, linux-clang, macos
```

The tests fetch GoogleTest (an installed or Conan GTest wins). `marker-render` (`tools/marker-render`) writes marker images (PGM)
for any payload, to compare your renderer's output pixel by pixel; `marker-render --golden <dir>` writes the golden set the other
libraries are tested against.

## License

BSD 3-Clause (`LICENSE`). The QR encoder (`third_party/qrcodegen`) is the QR Code generator library by Project Nayuki, MIT; the
tests use GoogleTest (BSD 3-Clause), which is not part of the library. Their license texts are in a release archive's `licenses/`
folder (the repository's root `licenses/`).
