# Integrating the marker

This guide puts the marker into an application. The library is renderer independent: it returns pixel aligned rectangles that
you draw with whatever you already use (Direct3D, Vulkan, Metal, OpenGL, a 2D API). The precise format is in
[marker-format.md](marker-format.md).

## 1. Add the library

CMake 4.0+ and C++20. The library has no dependencies.

```cmake
include(FetchContent)
FetchContent_Declare(mb_framemarker
  GIT_REPOSITORY https://github.com/Unarmed1000/mb-framepacing.git
  GIT_TAG master        # pin a release tag or commit hash for reproducible builds
  SOURCE_SUBDIR cpp)
FetchContent_MakeAvailable(mb_framemarker)
target_link_libraries(my_game PRIVATE mb::framemarker)
```

Alternatives: `add_subdirectory(path/to/mb-framepacing/cpp)`, or `cmake --install` the library and use
`find_package(mb_framemarker CONFIG REQUIRED)`.

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

```cpp
std::array<FM::Quad, FM::MaxQuadCount()> quads;   // no allocations per frame

void DrawFrameMarker(uint64_t frameIndex, double animationSeconds, uint32_t runId)
{
  const auto ticks = static_cast<int64_t>(animationSeconds * FM::TicksPerSecond); // the time your animation used
  const std::size_t count = FM::GenerateQuads({frameIndex, ticks, runId, FM::MarkerKind::Frame}, options, origin, quads);
  for (std::size_t i = 0; i < count; ++i)
  {
    const FM::Quad& q = quads[i];                 // pixels [Left, Right) x [Top, Bottom)
    FillRect(q.Left, q.Top, q.Right - q.Left, q.Bottom - q.Top, q.Dark ? Black : White);
  }
}
```

Prefer triangles or an index buffer? `QuadsToTriangles` / `QuadsToIndexed` turn the quads into vertices.

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
  const FM::Payload payload{frameIndex, static_cast<int64_t>(animationSeconds * FM::TicksPerSecond), /*runId*/ 7};
  std::size_t count = 0;
  switch (phase)
  {
  case Phase::Start:   // show for at least 250 ms
    count = FM::GenerateStartQuads(payload, {startUtc, "camera pan benchmark"}, options, origin, quads);
    break;
  case Phase::Measure:
    count = FM::GenerateQuads(payload, options, origin, quads);
    break;
  case Phase::End:     // show for at least 250 ms
    count = FM::GenerateQuads({payload.FrameIndex, payload.AnimationTicks, payload.RunId, FM::MarkerKind::SequenceEnd}, options, origin, quads);
    break;
  case Phase::Done:
    break;
  }
  DrawQuads(quads, count);
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

- `cpp/tools/marker-render` writes marker images (PGM) for any payload, so you can compare your renderer's output pixel by pixel.
- The GUI's live preview shows the decoded marker while capturing; "No marker seen yet" means the marker does not reach the
  capture unmodified (drawn too early, blended, scaled, too small).
- A capture whose analysis warns about the module size needs a bigger `ModuleSizePx` or a smaller downscale.
