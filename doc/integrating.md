# Integrating the marker

This guide puts the marker into an application. There are three ways in:

| Your application                 | Use                                                                                                           |
| -------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| C++ (any engine or graphics API) | The C++20 library [`marker/cpp`](../marker/cpp), this guide                                                   |
| Unity                            | The Unity package, see **[Unity](unity.md)**                                                                  |
| Other C# / .NET                  | The general C# library [`marker/csharp`](../marker/csharp): the same API as C++ (`MarkerGenerator`, `Marker`) |

All three produce exactly the same pixels. The libraries are renderer independent: they give you pixel aligned geometry to draw with
whatever you already use (Direct3D, Vulkan, Metal, OpenGL, a 2D API). The precise format is in [marker-format.md](marker-format.md).

## 1. Add the C++ library

CMake 4.0+ and a C++20 compiler; the library itself has no dependencies. Pick one of four ways:

**a) The release archive (recommended):** a small download, no git, and pinned by its hash. The release page lists the hash for
each version (`SHA256SUMS`):

```cmake
include(FetchContent)
FetchContent_Declare(mb_framemarker
  URL https://github.com/Unarmed1000/mb-framepacing/releases/download/marker-v0.1.0/mb-framemarker-cpp-0.1.0.tar.gz
  URL_HASH SHA256=<from SHA256SUMS>
  FIND_PACKAGE_ARGS 0.1 CONFIG)      # an installed copy of a compatible version wins
FetchContent_MakeAvailable(mb_framemarker)
target_link_libraries(my_game PRIVATE mb::framemarker)
```

**b) Git:** the same, fetched from the repository (it clones more than the library):

```cmake
FetchContent_Declare(mb_framemarker
  GIT_REPOSITORY https://github.com/Unarmed1000/mb-framepacing.git
  GIT_TAG marker-v0.1.0
  GIT_SHALLOW TRUE
  SOURCE_SUBDIR marker/cpp)
```

**c) A submodule or a copy in your tree:** `add_subdirectory(third_party/mb-framepacing/marker/cpp)`, or the unpacked release
archive.

**d) An installed copy:** build and install it once, then find it:

```sh
cmake -S marker/cpp -B build -DMB_FRAMEMARKER_BUILD_TESTS=OFF -DMB_FRAMEMARKER_BUILD_TOOLS=OFF
cmake --build build --config Release
cmake --install build --config Release --prefix <prefix>
```

```cmake
find_package(mb_framemarker 0.1 CONFIG REQUIRED)   # with CMAKE_PREFIX_PATH=<prefix>
target_link_libraries(my_game PRIVATE mb::framemarker)
```

What your project gets:

- One static library target, **`mb::framemarker`**, in every way above.
- When the library is not the top-level project, its tests, tools and warnings-as-errors are off, so GoogleTest is never downloaded.
  The options, if you want to change them:

  | Option                              | Default                                  |
  | ----------------------------------- | ---------------------------------------- |
  | `MB_FRAMEMARKER_BUILD_TESTS`        | on only when top-level                   |
  | `MB_FRAMEMARKER_BUILD_TOOLS`        | on only when top-level (`marker-render`) |
  | `MB_FRAMEMARKER_WARNINGS_AS_ERRORS` | on only when top-level                   |

- Versions follow semantic versioning. While the version is 0.x, a new minor version may change the API, so `find_package` only
  accepts the same minor version; from 1.0 it accepts any newer version with the same major version.

CI builds and runs a consumer project (`marker/cpp/tests/consumer`) with FetchContent, `add_subdirectory` and `find_package`, and
every release archive is consumed through its URL and hash before it is published. The git way uses the same source tree.

## 2. Choose the size and place once

The marker must survive the capture's downscale: aim for at least 3 stored pixels per QR module. The library computes it:

```cpp
#include <mb/framemarker/FrameMarker.hpp>
namespace FM = MB::FrameMarker;

// Output 1920x1080, capture stored at 960x540 (2:1)
const FM::Options options{FM::RecommendModuleSizePx(1080, 540), FM::RecommendedQuietZoneModules};       // 6 px modules
const FM::Point origin = FM::RecommendedOrigin(FM::MarkerSlot::TopLeft, 1920, 1080, options, /*alignPx*/ 2); // (32, 32)
```

## 3. Draw it every frame

Generate the marker straight into your vertex buffer. Nothing is allocated, and every vertex lies on a pixel corner (top-left origin,
+y down):

```cpp
std::array<FM::Vertex, FM::MaxFrameTriangleVertexCount()> vertices;   // once

void DrawFrameMarker(uint64_t frameIndex, double animationSeconds, uint32_t runId)
{
  const auto ticks = static_cast<int64_t>(animationSeconds * FM::TicksPerSecond); // the time your animation used
  const std::size_t count = FM::GenerateTriangles({frameIndex, ticks, runId, FM::MarkerKind::Frame}, options, origin, vertices);
  DrawTriangles(vertices.data(), count);   // your renderer: (X, Y) in pixels, color (Luma, Luma, Luma)
}
```

The same geometry comes in other forms:

- **`GenerateIndexed`:** 4 vertices and 6 indices per quad, for index buffers.
- **`GenerateQuads`:** rectangles covering `[Left, Right) x [Top, Bottom)`, for 2D fill-rect APIs.

Triangles are `(TL, TR, BL) (BL, TR, BR)`, clockwise on screen. Size your buffers with the `Max…Count()` functions: the `MaxFrame…`
ones for frame and end markers, the others for start markers.

**Rules that matter** (the capture can only read an unmodified marker):

1. Draw it **last**: after tonemapping, TAA, upscaling, post effects and UI.
2. No blending, no MSAA, no depth test; pure black `(0,0,0)` and white `(255,255,255)`.
3. Map pixel edges to vertices exactly (no half pixel offset), at the swap chain resolution.
4. Keep it at the same place every frame.
5. `frameIndex` increments by one for every rendered frame; `animationSeconds` is the time the frame's animation was evaluated
   for, from the same clock your animation uses.

## 4. Mark the test run

Wrap the measured part with a start and an end marker so the analysis measures exactly that window:

```cpp
enum class Phase { Start, Measure, End, Done };

void OnFrame(Phase phase, uint64_t frameIndex, double animationSeconds)
{
  static const int64_t startUtc = FM::ToDateTimeTicks(std::chrono::system_clock::now());
  static std::array<FM::Vertex, FM::MaxTriangleVertexCount()> vertices;   // start markers are larger
  const FM::Payload payload{frameIndex, static_cast<int64_t>(animationSeconds * FM::TicksPerSecond), /*runId*/ 7};
  std::size_t count = 0;
  switch (phase)
  {
  case Phase::Start:   // show for at least 250 ms
    count = FM::GenerateStartTriangles(payload, {startUtc, "camera pan benchmark"}, options, origin, vertices);
    break;
  case Phase::Measure:
    count = FM::GenerateTriangles(payload, options, origin, vertices);
    break;
  case Phase::End:     // show for at least 250 ms
    count = FM::GenerateTriangles({payload.FrameIndex, payload.AnimationTicks, payload.RunId, FM::MarkerKind::SequenceEnd}, options, origin, vertices);
    break;
  case Phase::Done:
    break;
  }
  DrawTriangles(vertices.data(), count);
}
```

The start marker is larger than the frame marker (it carries the name); keep `FM::MaxMarkerSizePx(options)` free around the
origin while it is shown.

## 5. Capture and analyse

```sh
mb-framepacing capture -d "<your capture card>" --scale 960x540 --wait-for-start --stop-at-end --analyze
```

or record with any other tool (a lossless video, a high speed camera's image sequence) and use `mb-framepacing import`.

## Checking your integration

- `marker/cpp/tools/marker-render` writes marker images (PGM) for any payload, so you can compare your renderer's output pixel by pixel.
- The GUI's live preview shows the decoded marker while capturing; "No marker seen yet" means the marker does not reach the
  capture unmodified (drawn too early, blended, scaled, too small).
- A capture whose analysis warns about the module size needs a bigger `ModuleSizePx` or a smaller downscale.
