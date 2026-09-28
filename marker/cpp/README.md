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
std::array<FM::Vertex, FM::MaxGridVertexCount()> grid;
const std::size_t gridCount = FM::GridVertices(FM::MarkerKind::Frame, options, origin, grid);
UploadVertices(grid.data(), gridCount);   // your renderer: a static vertex buffer, (X, Y) in pixels, color (Luma, Luma, Luma)
FM::ModuleMatrix matrix;
std::array<uint32_t, FM::MaxIndexCount()> indices;

// Every frame, last (after post effects and UI), without blending:
const FM::Payload payload{frameIndex, animationTicks, /*runId*/ 1};
FM::GenerateModules(payload, matrix);                                      // encode once
const std::size_t count = FM::ModulesToGridIndices(matrix, indices);       // only the indices change
DrawIndexed(indices.data(), count);      // triangles over the static vertices
```

This is the most efficient way that needs no shader of your own; see [the options](#ways-to-draw-it-most-efficient-first).

- **Frame index:** the frame's own index, the same for every refresh the frame stays on screen.
- **Animation time:** the moment the frame shows, as the application animated it, in 100 ns ticks (`FM::TicksPerSecond`).
- **Frame pacing (optional):** `IntendedDisplayTicks` (when the pacer intends the frame to be shown, 100 ns ticks on its steady
  clock, any epoch) and `TargetFrameTicks` (the interval it aims for: `166'667` for 60 fps). `0` = unknown.
- **CPU start time and CPU busy (optional):** `CpuStartTicks` (when the CPU started working on the frame, on the same clock,
  PresentMon's `CPUStartTime`) and `CpuBusyTicks` (how long until Present, PresentMon's `MsCPUBusy`). `0` = unknown.
- **Start and end:** bracket the part to measure with a payload of kind `MarkerKind::SequenceStart`, encoded with its metadata
  (`GenerateModules(payload, matrix, {utcTicks, sequenceId})`), and a payload of kind `MarkerKind::SequenceEnd`, each shown for a few frames. The sequence id is 16 opaque bytes unique to the run: a UUID's
  bytes, or a text tag of up to 16 printable ASCII characters (`SequenceId::TryFromText`).
- **Sync marker (optional; required for camera capture):** a small second marker with only the frame index, drawn bottom-left
  (`RecommendedOrigin(MarkerKind::Sync, …)`) with a payload of kind `MarkerKind::Sync`.
- **Size:** every main marker (frame, start, end) is QR version 6, 41×41 modules, so it never changes size:
  `MarkerSizePx(options)`. The sync marker is QR version 2, 25×25 modules.

## Ways to draw it, most efficient first

| #   | Option                                                             | Per frame (41×41 main marker, about 440 dark runs)           | Needs                                                                      |
| --- | ------------------------------------------------------------------ | ------------------------------------------------------------ | -------------------------------------------------------------------------- |
| 1   | **Dedicated shader**: one quad and a 41×41 module texture          | 1,681 bytes (the texture), 2 triangles                       | A fragment shader that reads whole texels (HLSL `Load`, GLSL `texelFetch`) |
| 2   | **Static grid**: `GridVertices` once, then `ModulesToGridIndices`  | About 2,600 indices (10 KB as 32 bit, 5 KB as 16 bit)        | Index buffers and vertex colours; the 1,768 vertices stay                  |
| 3   | **Module texture scaled up**: `ModulesToBitmap` at 1 px per module | 1,681 pixels                                                 | A texture drawn scaled by a whole number with point filtering, pixel exact |
| 4   | **Triangles**: `ModulesToIndexed` or `ModulesToTriangles`          | About 1,750 vertices and 2,600 indices, or 2,600 vertices    | Only vertex colours: the simplest to add to a renderer                     |
| 5   | **Rectangles**: `ModulesToQuads`                                   | About 440 filled rectangles                                  | A 2D fill-rectangle API                                                    |
| 6   | **Full-size bitmap**: `ModulesToBitmap`                            | The marker's pixels (294×294 at 6 px per module: 86 KB grey) | A CPU pixel buffer: software rendering, video frames, images               |

Every option draws exactly the same pixels, from one encode per frame (the 211 byte module matrix). The shader's code (HLSL and
GLSL) and the exact rules are in [Integrating the marker](https://github.com/Unarmed1000/mb-framepacing/blob/master/doc/integrating.md#3-draw-it-every-frame).

## API

Everything is declared by `<mb/framemarker/FrameMarker.hpp>` in `MB::FrameMarker`, one header per type.

| Function or type                                                                                               | What it does                                                                         |
| -------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------ |
| `Payload`, `StartMetadata`, `SequenceId`, `MarkerKind`                                                         | What a marker carries                                                                |
| `Options`, `Point`                                                                                             | Size and place                                                                       |
| `GenerateModules`, `ModuleMatrix` (`Size`, `IsDark`, `Bits`)                                                   | Encode the marker: its QR symbol, 1 bit per module (211 bytes), a plain value        |
| `GridVertices`, `GridVertexCount`, `MaxGridVertexCount`, `ModulesToGridIndices`                                | A static grid uploaded once, and per frame only the indices                          |
| `ModulesToBitmap`, `PixelFormat`, `BytesPerPixel`                                                              | Draw it into a pixel buffer (`[L]`, `[R, G, B]` or `[R, G, B, A]` bytes; any stride) |
| `ModulesToIndexed`, `ModulesToTriangles`, `ModulesToQuads`                                                     | Draw it as indexed triangles, a triangle list or quads, into your buffers            |
| `MaxTriangleVertexCount`, `MaxIndexedVertexCount`, `MaxIndexCount`, `MaxQuadCount`, `MaxPackedModuleByteCount` | Buffer sizes that fit every marker kind                                              |
| `MarkerSizePx`, `QrModuleCountFor`, `RecommendedOrigin`                                                        | Sizing and placement                                                                 |
| `MinimumModuleSizePx`, `RecommendModuleSizePx`                                                                 | Module size for a capture's scaling                                                  |
| `EncodePayload`, `TryDecodePayload`, `ToDateTimeTicks`                                                         | The wire format and its time units                                                   |

Every function is `noexcept` and never allocates; it returns 0 (`{0, 0}`, false) when the options are invalid, the matrix is empty or a
buffer is too small. One encode can feed several outputs (a mesh for the game, a bitmap for a UI).

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
