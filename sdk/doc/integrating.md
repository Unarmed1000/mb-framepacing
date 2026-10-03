# Integrating the marker

This guide puts the marker into an application. There are four ways in:

| Your application                 | Use                                                                                                                            |
| -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| C++ (any engine or graphics API) | The marker module of the C++20 library [`sdk/cpp`](../cpp/README.md) (`mb_framepacing::marker`), this guide                    |
| Unity                            | The Unity package, see **[Unity](unity.md)**                                                                                   |
| Other C# / .NET                  | The C# marker module [`sdk/csharp/marker`](../csharp/marker/README.md): the same API as C++ (`MarkerGenerator`, `FrameMarker`) |
| Python                           | The Python package [`sdk/python`](../python/README.md) (`mb_framepacing.marker`)                                               |

All four produce exactly the same pixels. The libraries are renderer independent: they give you pixel aligned geometry to draw with
whatever you already use (Direct3D, Vulkan, Metal, OpenGL, a 2D API). The precise format is in [marker-format.md](marker-format.md);
[Filling the marker fields](marker-fields.md) says where each field's value comes from and what the analysis does with it.

> **Photosensitivity warning.** The marker is a high-contrast pattern that changes every frame. Flickering patterns can trigger
> seizures in people with photosensitive epilepsy. Draw it in test builds only, never in what players see, and read
> [Photosensitivity](#photosensitivity) before you show it to anyone.

## 1. Add the C++ library

CMake 4.0+ and a C++20 compiler. The library is one CMake project of modules (`mb_framepacing::core`, `::marker`, `::data`; `::pacer` is experimental and off by default); the marker
module needs only the core and has no dependencies ([what each module adds to your executable](../cpp/README.md#what-it-adds-to-your-executable)).
Pick one of five ways:

**a) The release archive (recommended):** a small download, no git, and pinned by its hash. The release page lists the hash for
each version (`SHA256SUMS`):

```cmake
include(FetchContent)
FetchContent_Declare(mb_framepacing
  URL https://github.com/Unarmed1000/mb-framepacing/releases/download/sdk-v0.1.0/mb-framepacing-cpp-0.1.0.tar.gz
  URL_HASH SHA256=<from SHA256SUMS>
  FIND_PACKAGE_ARGS 0.1 CONFIG COMPONENTS marker)      # an installed copy of a compatible version wins
set(MB_FRAMEPACING_BUILD_DATA OFF)   # optional: leave the data module (and nlohmann/json) out
FetchContent_MakeAvailable(mb_framepacing)
target_link_libraries(my_game PRIVATE mb_framepacing::marker)
```

**b) Git:** the same, fetched from the repository (it clones more than the library):

```cmake
FetchContent_Declare(mb_framepacing
  GIT_REPOSITORY https://github.com/Unarmed1000/mb-framepacing.git
  GIT_TAG sdk-v0.1.0
  GIT_SHALLOW TRUE
  SOURCE_SUBDIR sdk/cpp)
```

**c) A submodule or a copy in your tree:** `add_subdirectory(third_party/mb-framepacing/sdk/cpp)`, or the unpacked release
archive.

**d) An installed copy:** build and install it once, then find it:

```sh
cmake -S sdk/cpp -B build -DMB_FRAMEPACING_BUILD_TESTS=OFF -DMB_FRAMEPACING_BUILD_TOOLS=OFF
cmake --build build --config Release
cmake --install build --config Release --prefix <prefix>
```

```cmake
find_package(mb_framepacing 0.1 CONFIG REQUIRED COMPONENTS marker)   # with CMAKE_PREFIX_PATH=<prefix>
target_link_libraries(my_game PRIVATE mb_framepacing::marker)
```

**e) Conan 2:** the recipe is in the repository ([`sdk/cpp/conan`](../cpp/conan), laid out as conan-center-index is). Add a checkout
as a remote, then require the package; Conan builds it from the release archive (`-o "mb-framepacing/*:with_data=False"` leaves the
data module out):

```sh
conan remote add mb-framepacing <checkout>/sdk/cpp/conan --type local-recipes-index
conan install --requires mb-framepacing/0.1.0 --build=missing -s compiler.cppstd=20
```

```cmake
find_package(mb_framepacing CONFIG REQUIRED COMPONENTS marker)   # from Conan's CMakeDeps
target_link_libraries(my_game PRIVATE mb_framepacing::marker)
```

The remote holds recipes only, no prebuilt binaries, and the library needs C++20: the profile's `compiler.cppstd` must be 20 or newer.

What your project gets:

- A static library target per module in every way above: **`mb_framepacing::marker`** for the marker (it links
  `mb_framepacing::core`), and `mb_framepacing::data` for reading the tools' data. Each type has its own header (`<mb/framepacing/marker/payload/Payload.hpp>`,
  `<mb/framepacing/core/time/TimeSpan.hpp>`, ...) and the marker's functions are in `<mb/framepacing/marker/FrameMarker.hpp>`.
- When the library is not the top-level project, its tests, tools, install rules and warnings-as-errors are off, so GoogleTest is
  never downloaded and your project's `cmake --install` installs your files only.
  The options, if you want to change them:

  | Option                              | Default                                                                         |
  | ----------------------------------- | ------------------------------------------------------------------------------- |
  | `MB_FRAMEPACING_BUILD_MARKER`       | on                                                                              |
  | `MB_FRAMEPACING_BUILD_DATA`         | on (off: no data module and no nlohmann/json download)                          |
  | `MB_FRAMEPACING_BUILD_PACER`        | off: the pacer is experimental; on builds `mb_framepacing::pacer`               |
  | `MB_FRAMEPACING_BUILD_TESTS`        | on only when top-level                                                          |
  | `MB_FRAMEPACING_BUILD_TOOLS`        | on only when top-level (`marker-render`)                                        |
  | `MB_FRAMEPACING_BUILD_BENCHMARKS`   | off (the marker's benchmarks; fetches Google Benchmark unless one is installed) |
  | `MB_FRAMEPACING_BUILD_FUZZERS`      | off (the libFuzzer target of the marker's QR encoder; needs Clang)              |
  | `MB_FRAMEPACING_INSTALL`            | on only when top-level (the install rules and the CMake package)                |
  | `MB_FRAMEPACING_WARNINGS_AS_ERRORS` | on only when top-level                                                          |

- Versions follow semantic versioning. While the version is 0.x, a new minor version may change the API, so `find_package` only
  accepts the same minor version; from 1.0 it accepts any newer version with the same major version.

CI builds and runs a consumer project (`sdk/cpp/tests/consumer`) with FetchContent, `add_subdirectory` and `find_package` with
components, and every release archive is consumed through its URL and hash before it is published. The Conan recipe is built and tested on
Windows, Ubuntu and macOS from every push, and from every release archive once it is published. The git way uses the same source tree.

## 2. Choose the size and place once

The marker must survive the capture's downscale: aim for at least 3 stored pixels per QR module. The library computes it, and
`mb-framepacing marker-size --source <output> --stored <capture>` prints it for a setup:

```cpp
#include <mb/framepacing/core/Point.hpp>
#include <mb/framepacing/marker/FrameMarker.hpp>
#include <mb/framepacing/marker/Options.hpp>
namespace FP = MB::FramePacing;
namespace FM = MB::FramePacing::Marker;

// Output 1920x1080, capture stored at 960x540 (2:1)
const auto options = FM::Options::Recommended(1080, 540);                                  // 6 px modules
const FP::Point origin = options.RecommendedOrigin(FM::MarkerKind::Frame, 1080, /*alignPx*/ 2); // (32, 32)
```

### Photosensitivity

The marker's modules change every frame: black and white, far more than three times a second. That is the kind of content the
guidelines on flashing limit ([WCAG 2.3.1](https://www.w3.org/WAI/WCAG22/Understanding/three-flashes-or-below-threshold.html);
broadcasting has ITU-R BT.1702). It is inside WCAG's only while it is small:

- **The area that may flash** is a quarter of a 10° field of view: by WCAG's estimate for a screen at a usual distance, about 2.8 %
  of the screen.
- **A fine, balanced pattern is exempt** while its squares are smaller than 0.1° of view: by the same estimate, a module of at
  most 1/300 of the screen's width.

| Output stored as             | Module size | The symbol's share of the screen | Module in view | Within the guideline              |
| ---------------------------- | ----------- | -------------------------------- | -------------- | --------------------------------- |
| 1080p at 1080p (1:1)         | 3 px        | 0.7 %                            | 0.05°          | yes                               |
| 1080p at 540p, 2160p at 540p | 6 px, 12 px | 2.9 %                            | 0.09°          | only as a fine pattern, by little |
| 1080p at 360p, 720p at 360p  | 9 px, 6 px  | 6.6 %                            | 0.14°          | **no**                            |

So the library's default is at the edge of the guideline, and a capture that stores fewer lines needs a marker beyond it.
`mb-framepacing marker-size` prints these numbers for a setup and says when it is outside. The estimate assumes a screen that
fills about 30° of the view: a very large or close display, or a headset, makes the marker larger than it says. And the guideline
lowers the risk; it does not remove it. Therefore:

- **Test builds only.** Never draw the marker in a build players use, and make the switch that turns it on hard to hit by accident.
- **No larger than the capture needs:** the recommended module size, and a capture that stores as many lines as it can.
- **Tell the people at the screen** before a run, and keep anyone who is photosensitive away from it.
- **Nobody has to look at it.** A capture card records the signal, not the screen: cover the marker's corner of the display, or
  turn the display away, and the measurement is the same.

## 3. Draw it every frame

Encode the marker once (`GenerateModules`: its QR symbol as a packed module matrix), then draw it straight into your vertex buffer.
Nothing is allocated, and every vertex lies on a pixel corner (top-left origin, +y down):

```cpp
FM::ModuleMatrix matrix;                                          // once
std::array<FM::Vertex, FM::MaxTriangleVertexCount()> vertices;   // once

void DrawFrameMarker(uint64_t frameIndex, double animationSeconds, uint32_t runId)
{
  const FP::TimeSpan animationTime = FP::TimeSpan::FromSeconds(animationSeconds); // the time your animation used
  FM::GenerateModules({FM::MarkerKind::Frame, runId, frameIndex, FM::MarkerFlags::NoFlags, animationTime}, matrix);
  const std::size_t count = FM::ModulesToTriangles(matrix, options, origin, vertices);
  DrawTriangles(vertices.data(), count);   // your renderer: (X, Y) in pixels, color (Luma, Luma, Luma)
}
```

That is the simplest to add. The same matrix draws in other forms, and one encode can feed several; **most efficient first**, per
frame for the 41×41 main marker (about 440 dark runs):

1. **A dedicated shader reading the packed bits:** one opaque quad whose fragment shader takes each module's bit from `Bits()`, set as
   211 bytes of constants; below.
2. **A dedicated shader reading a texel per module:** the same quad, the modules in a 41×41 texture (1,681 bytes per frame); below.
3. **`GridVertices` + `ModulesToGridIndices`:** a static grid of vertices (every module corner; 1768 for the main marker, 680 for the
   sync marker) that you upload once, and per frame only the indices of the dark runs: about 2,600 (10 KB as 32 bit, 5 KB as 16 bit;
   the grid fits 16 bit indices). The grid stays valid while the kind's symbol size, the options and the origin do.
4. **`ModulesToBitmap` at module resolution:** with a module size of 1 and origin (0, 0), a 41×41 image to draw as a texture scaled up
   by a whole number with point filtering (1,681 pixels per frame).
5. **`ModulesToIndexed`** (4 vertices and 6 indices per quad, for index buffers) or **`ModulesToTriangles`** (above): about 1,750
   vertices and 2,600 indices, or 2,600 vertices, rebuilt every frame.
6. **`ModulesToQuads`:** rectangles covering `[Left, Right) x [Top, Bottom)`, for 2D fill-rect APIs (about 440).
7. **`ModulesToBitmap` at full size:** the pixels themselves, into an `R8`, `R8G8B8` or `R8G8B8A8` buffer (any stride; B8G8R8 and
   B8G8R8A8 buffers take the same bytes, since the marker is black and white), for software rendering, video frames and images.

`ModuleMatrix::Bits()` gives the packed bits themselves (1 bit per module, row-major, most significant bit first).

### A dedicated shader

[`sdk/shaders`](../shaders/README.md) has ready-made shaders for Direct3D (HLSL), OpenGL 3.3 and OpenGL ES 3.0, OpenGL ES 2.0 and
Vulkan (GLSL), each reading the packed bits or a texel per module; its README says how to draw them with each API. In short: the vertex
shader makes one quad from the vertex index (draw 4 vertices as a triangle strip, no vertex buffer; OpenGL ES 2.0 needs a buffer of the
4 corners) and hands on the marker-local pixel coordinate, (0, 0) top-left, +y down. Every fragment then gets `(px + 0.5, py + 0.5)`,
`floor(uv / ModuleSizePx)` is exact on every platform, and the fragment shader outputs pure black or white:

- **Packed bits:** module `i = row × size + column` is bit `7 − (i mod 8)` of byte `i / 8` of `Bits()`; copy the 211 bytes (79 for the
  sync marker) as they are into 14 `uint4` of constants. OpenGL ES 2.0 has no integers: there they come in a 211×1 texture and the
  bit comes from float arithmetic.
- **A texel per module:** `ModulesToBitmap(matrix, FM::Options(1, 0), {0, 0}, texels, 41, 41, PixelFormat::R8)` uploaded as a 41×41 single
  channel texture (no filtering, no mip maps), row 0 the symbol's top row; a sync marker uses its top-left 25×25.

Draw it opaque, without blending, depth test or culling, last. On OpenGL ES 2.0 the shaders need `highp` floats in the fragment shader
(they do not compile without, rather than draw a wrong marker); draw geometry there instead. `tools/check_shaders.py --render` draws
every shader and compares every pixel with `ModulesToBitmap`. Unity's package draws with the same lookup (`Hidden/MB/FrameMarkerQuadPacked`,
[Unity](unity.md)).

Triangles are `(TL, TR, BL) (BL, TR, BR)`, clockwise on screen. Size your buffers with the `Max…Count()` functions; they fit every
marker kind.

**Static frames (optional).** Set `FM::MarkerFlags::StaticAfter` on a frame when nothing animates while it is on screen (no pending
work after it: an idle screen, a paused menu). An application that only knows it once it renders the next frame sets
`FM::MarkerFlags::StaticBefore` on that next frame instead. The analysis then does not judge the animation error of the step out of
the static frame, so an animation clock that pauses while idle does not look like a huge error ([the flags](marker-fields.md#flags-static-after-and-static-before)).

**Frame pacing (recommended).** If your game paces its frames, put what the pacer aims for into the payload: the **preferred frame time**, the rate
the game wants to run at, its target frame time, and the time it intends the frame to become visible (steady clock ticks, any epoch). The analysis then measures every frame against your plan, separates pacing errors from animation timing errors,
does not count a rate you chose (30 fps for a busy stretch) as late, and shows where the game ran slower than it wanted: a 30 fps
lock prefers 30 fps, a pacer that drops from 60 to 30 keeps preferring 60, and a device idle at 1 fps prefers 1 fps. A renderer that
presents only when something changes writes `FM::Payload::OnDemandFrameTime` for both frame times. The SDK's own
[frame pacer](pacer.md) (`mb_framepacing::pacer`), which paces every frame with only a steady clock and vsync and hands you these values
and an animation time in whole refreshes, is experimental and off by default.

**CPU start time and CPU busy (optional).** Add when the CPU started working on the frame (on the same clock) and how long it has
worked on it when you draw the marker (you draw it last, just before Present). The capture sees only the display side; these show the
application side, including frames that took several refreshes or overlap the next one. The constructor takes the payload's fields
in the order of the wire format: the kind, run id, frame index, flags and animation time, then the optional timing fields:

```cpp
// Kind, run id, frame index, flags, animation time; then preferred and target frame time, intended display time, CPU start and busy
// The animation time is an FP::TimeSpan, the frame times and CPU busy FP::TimeSpan32, the intended display and CPU start time
// FP::TickCount64 (all in 100 ns ticks)
const FM::Payload payload(FM::MarkerKind::Frame, runId, frameIndex, nothingPending ? FM::MarkerFlags::StaticAfter : FM::MarkerFlags::NoFlags,
                          animationTime, preferredFrameTime, targetFrameTime, intendedDisplayTime, cpuStartTime, cpuBusy);
```

What to write in each field, for typical frame pacers, is in [Filling the marker fields](marker-fields.md).

**The sync marker (optional; required for camera capture).** Draw the small sync marker bottom-left as well, with the same run id
and frame index. The analysis flags tearing when the two disagree, and a camera filming the screen times the frames by it:

```cpp
const FP::Point syncOrigin = options.RecommendedOrigin(FM::MarkerKind::Sync, 1080, 2);
FM::ModuleMatrix sync;
FM::GenerateModules(payload.WithKind(FM::MarkerKind::Sync), sync);   // the main marker's run id and frame index
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
  static const int64_t startUtc = MB::FramePacing::ToDateTimeTicks(std::chrono::system_clock::now());   // core/time/ChronoConversion.hpp
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
  const FM::Payload payload(kind, 7u, frameIndex, FM::MarkerFlags::NoFlags, FP::TimeSpan::FromSeconds(animationSeconds));
  FM::GenerateModules(payload, matrix, {startUtc, sequenceId});   // the metadata only goes into the start marker
  DrawTriangles(vertices.data(), FM::ModulesToTriangles(matrix, options, origin, vertices));
}
```

Every main marker kind has the same size, so the start and end markers cover exactly the frame marker's area.

## 5. Record and analyse

Record the capture card with OBS Studio at the display's refresh rate, then import the recording
([Measure with OBS and a capture card](../../measure/doc/usage.md#2-measure-with-obs-and-a-capture-card)):

```sh
mb-framepacing import recording.mkv --display-hz 60 --scale 960x540 --wait-for-start --stop-at-end --analyze
```

Any other lossless recording works too (a video, a high speed camera's image sequence). Recording the card live with
mb-framepacing itself is experimental ([Live capture](../../measure/doc/live-capture.md)). To film
the screen with a high speed camera, draw the sync marker as well and see the very experimental
[camera capture](../../measure/doc/camera.md).

## A complete integration to look at

This project's author keeps an **unofficial** version of the gtec-demo-framework,
[Unarmed1000/gtec-demo-framework](https://github.com/Unarmed1000/gtec-demo-framework), with the marker built into the framework. Its
[Doc/FramePacing.md](https://github.com/Unarmed1000/gtec-demo-framework/blob/master/Doc/FramePacing.md) describes it; in short:

- **Every OpenGL ES 2, OpenGL ES 3 and Vulkan sample can show the marker**, without a change to the sample: the framework draws it
  as the last thing of the frame. It is off by default. `--FramePacing` switches it on, `--FramePacing.SyncMarker` adds the sync
  marker, `--FramePacing.Run <name>` with `--FramePacing.Duration <seconds>` brackets a measured run with the start and end markers,
  and `--FramePacing.CaptureHeight <px>` sizes the modules for the height the capture is stored at.
- **Three samples also pace their frames** with the SDK's experimental [frame pacer](pacer.md): FramePacing for
  [Vulkan](https://github.com/Unarmed1000/gtec-demo-framework/tree/master/DemoApps/Vulkan/FramePacing), [OpenGL ES 3](https://github.com/Unarmed1000/gtec-demo-framework/tree/master/DemoApps/GLES3/FramePacing) and [OpenGL ES 2](https://github.com/Unarmed1000/gtec-demo-framework/tree/master/DemoApps/GLES2/FramePacing). The pacer is in those three samples only, not in the
  framework.

Its example, any sample measured for 30 seconds (it records the card live, which is experimental; a recording imported with
`--scale 960x540` measures the same):

```sh
GLES3.Stats --FramePacing.Run "Stats 30s" --FramePacing.Duration 30 --FramePacing.CaptureHeight 540
mb-framepacing capture --experimental -d "<capture card>" --scale 960x540 --wait-for-start --stop-at-end --analyze
```

## Checking your integration

- `sdk/cpp/marker/tools/marker-render` writes marker images (PGM) for any payload, so you can compare your renderer's output pixel by pixel.
- The GUI's live preview shows the decoded marker while capturing; "No marker seen yet" means the marker does not reach the
  capture unmodified (drawn too early, blended, scaled, too small).
- A capture whose analysis warns about the module size needs a bigger `ModuleSizePx` or a smaller downscale.
