# Integrating the marker

This guide puts the marker into an application. There are four ways in:

| Your application                 | Use                                                                                                                     |
| -------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| C++ (any engine or graphics API) | The C++20 library [`marker/cpp`](../marker/cpp/README.md), this guide                                                   |
| Unity                            | The Unity package, see **[Unity](unity.md)**                                                                            |
| Other C# / .NET                  | The general C# library [`marker/csharp`](../marker/csharp/README.md): the same API as C++ (`MarkerGenerator`, `Marker`) |
| Python                           | The Python library [`marker/python`](../marker/python/README.md) (`mb_framemarker`)                                     |

All four produce exactly the same pixels. The libraries are renderer independent: they give you pixel aligned geometry to draw with
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

The marker must survive the capture's downscale: aim for at least 3 stored pixels per QR module. The library computes it, and
`mb-framepacing marker-size --source <output> --stored <capture>` prints it for a setup:

```cpp
#include <mb/framemarker/FrameMarker.hpp>
namespace FM = MB::FrameMarker;

// Output 1920x1080, capture stored at 960x540 (2:1)
const FM::Options options{FM::RecommendModuleSizePx(1080, 540), FM::RecommendedQuietZoneModules};       // 6 px modules
const FM::Point origin = FM::RecommendedOrigin(FM::MarkerKind::Frame, 1920, 1080, options, /*alignPx*/ 2); // (32, 32)
```

## 3. Draw it every frame

Encode the marker once (`GenerateModules`: its QR symbol as a packed module matrix), then draw it straight into your vertex buffer.
Nothing is allocated, and every vertex lies on a pixel corner (top-left origin, +y down):

```cpp
FM::ModuleMatrix matrix;                                          // once
std::array<FM::Vertex, FM::MaxTriangleVertexCount()> vertices;   // once

void DrawFrameMarker(uint64_t frameIndex, double animationSeconds, uint32_t runId)
{
  const auto ticks = static_cast<int64_t>(animationSeconds * FM::TicksPerSecond); // the time your animation used
  FM::GenerateModules({frameIndex, ticks, runId, FM::MarkerKind::Frame}, matrix);
  const std::size_t count = FM::ModulesToTriangles(matrix, options, origin, vertices);
  DrawTriangles(vertices.data(), count);   // your renderer: (X, Y) in pixels, color (Luma, Luma, Luma)
}
```

The same matrix draws in other forms; one encode can feed several:

- **`ModulesToIndexed`:** 4 vertices and 6 indices per quad, for index buffers.
- **`ModulesToQuads`:** rectangles covering `[Left, Right) x [Top, Bottom)`, for 2D fill-rect APIs.
- **`ModulesToBitmap`:** the pixels themselves, into a `Gray8`, `Rgb24` or `Rgba32` buffer (any stride; BGR and BGRA buffers take
  the same bytes, since the marker is black and white). With `ModuleSizePx` 1 and origin (0, 0) it is a module-resolution image: a
  texture to draw scaled up by a whole number with point filtering.
- **`ModuleMatrix::Bits()`:** the packed bits themselves (1 bit per module, row-major, most significant bit first).

Triangles are `(TL, TR, BL) (BL, TR, BR)`, clockwise on screen. Size your buffers with the `Max…Count()` functions; they fit every
marker kind.

**Frame pacing (recommended).** If your game paces its frames, put what the pacer aims for into the payload: the time it intends the
frame to become visible (steady clock ticks, any epoch) and its target frame time. The analysis then measures every frame against
your plan, separates pacing errors from animation timing errors, and does not count a rate you chose (30 fps for a busy stretch) as
late.

**CPU start time and CPU busy (optional).** Add when the CPU started working on the frame (on the same clock) and how long it has
worked on it when you draw the marker (you draw it last, just before Present). The capture sees only the display side; these show the
application side, including frames that took several refreshes or overlap the next one:

```cpp
const FM::Payload payload{frameIndex, ticks, runId, FM::MarkerKind::Frame, intendedDisplayTicks, targetFrameTicks,
                          cpuStartTicks, cpuBusyTicks};
```

**The sync marker (optional; required for camera capture).** Draw the small sync marker bottom-left as well, with the same frame
index. The analysis flags tearing when the two disagree, and a camera filming the screen times the frames by it:

```cpp
const FM::Point syncOrigin = FM::RecommendedOrigin(FM::MarkerKind::Sync, 1920, 1080, options, 2);
FM::ModuleMatrix sync;
FM::GenerateModules({frameIndex, 0, 0u, FM::MarkerKind::Sync}, sync);
const std::size_t syncCount = FM::ModulesToTriangles(sync, options, syncOrigin, vertices);
```

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
  static const FM::SequenceId sequenceId = NewUuidBytes();   // any 16 bytes unique to this run, or FM::SequenceId::TryFromText("camera pan", id)
  static std::array<FM::Vertex, FM::MaxTriangleVertexCount()> vertices;
  static FM::ModuleMatrix matrix;
  if (phase == Phase::Done)
  {
    return;
  }
  // Start: one captured frame is enough; ~3 capture frames (e.g. 100 ms) for slack. End: the same.
  const FM::MarkerKind kind = phase == Phase::Start ? FM::MarkerKind::SequenceStart
                              : phase == Phase::End ? FM::MarkerKind::SequenceEnd
                                                    : FM::MarkerKind::Frame;
  const FM::Payload payload{frameIndex, static_cast<int64_t>(animationSeconds * FM::TicksPerSecond), /*runId*/ 7, kind};
  FM::GenerateModules(payload, matrix, {startUtc, sequenceId});   // the metadata only goes into the start marker
  DrawTriangles(vertices.data(), FM::ModulesToTriangles(matrix, options, origin, vertices));
}
```

Every main marker kind has the same size, so the start and end markers cover exactly the frame marker's area.

## 5. Capture and analyse

```sh
mb-framepacing capture -d "<your capture card>" --scale 960x540 --wait-for-start --stop-at-end --analyze
```

or record with any other tool (a lossless video, a high speed camera's image sequence) and use `mb-framepacing import`. To film
the screen with a high speed camera, draw the sync marker as well and see the very experimental
[camera capture](camera.md).

## Checking your integration

- `marker/cpp/tools/marker-render` writes marker images (PGM) for any payload, so you can compare your renderer's output pixel by pixel.
- The GUI's live preview shows the decoded marker while capturing; "No marker seen yet" means the marker does not reach the
  capture unmodified (drawn too early, blended, scaled, too small).
- A capture whose analysis warns about the module size needs a bigger `ModuleSizePx` or a smaller downscale.
