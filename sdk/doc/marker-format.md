# Frame marker format (version 1)

The frame marker is a QR code that the application under test draws into every frame. It carries the application's **frame
index**, the **animation time** the frame was rendered for, a **run id**, when the application paces its frames **when it intends
the frame to be shown**, its **target frame time** and the **preferred frame time** it wants to run at, and optionally the frame's
**CPU start time** and **CPU busy**: when the CPU started working on the frame and how long it worked on it before presenting it. A
**flags** byte says when nothing animates: **static after** while this frame is on screen, **static before** while the frame before it was. `mb-framepacing` captures the display output with an
HDMI/DP capture card, decodes the marker in every captured frame, and compares the animation timeline with the capture timeline.
Special **start** and **end** markers bracket a test run so the analyzer can cut the capture to exactly the measured window.

The C++20 library in [`sdk/marker/cpp/`](../marker/cpp) generates the marker geometry, and so do the C# library `MB.FrameMarker`
([`sdk/marker/csharp/`](../marker/csharp)) and the Python library `mb_framemarker` ([`sdk/marker/python/`](../marker/python)). The C# library
`MB.FramePacing.Marker` decodes it.
Both implement this document; if they disagree, this document is the reference. [Integrating the marker](integrating.md) builds it
into an application, and [Filling the marker fields](marker-fields.md) says where each field's value comes from, when it changes and
what the analysis does with it, with examples for typical frame pacers.

> **Two counters, never mixed.** The marker's _frame index_ is the application's own rendered-frame counter. The capture tool
> keeps a separate _capture index_, one per frame the capture card delivers. The two run at different rates (for example a game
> rendering 144 frames per second on a 240 Hz display captured at 240 fps), and each can have gaps or restart, so they are never
> compared with each other.

## Payload

Frame, start and end markers start with the same 53 byte header, little endian. Its fields are grouped: the format, which run and
which frame, what the frame shows, the frame pacing (what the application wants, what the pacer aims for now, when this frame should
show) and the CPU's work:

| Offset | Size | Field                 | Notes                                                                                                                                                                                                |
| ------ | ---- | --------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 0      | 2    | Magic                 | ASCII `"MF"` (`0x4D 0x46`)                                                                                                                                                                           |
| 2      | 1    | Format version        | `1`                                                                                                                                                                                                  |
| 3      | 1    | Kind                  | `0` = Frame, `1` = SequenceStart, `2` = SequenceEnd (`3` = Sync is the small sync marker, see below)                                                                                                 |
| 4      | 4    | Run id                | `u32`. Identifies one test run; the start marker, every frame marker and the end marker of a run carry the same id.                                                                                  |
| 8      | 8    | Frame index           | `u64`. Increments by 1 for every frame the application renders, including frames that show a start/end marker.                                                                                       |
| 16     | 1    | Flags                 | Bit 0 = **static after**, bit 1 = **static before**: nothing animates while this frame, or the frame before it, is on screen (see below). Bits 2 to 7 are reserved: write `0`; decoders ignore them. |
| 17     | 8    | Animation time        | `i64` two's complement, C# `TimeSpan` ticks (100 ns). The time the frame's animation was evaluated for.                                                                                              |
| 25     | 4    | Preferred frame time  | `u32` ticks (100 ns), `0` = unknown, `0xFFFFFFFF` = on demand. The interval the application wants to run at: `166'667` for 60 fps, also while the pacer runs slower (see below).                     |
| 29     | 4    | Target frame time     | `u32` ticks (100 ns), `0` = unknown. The interval the pacer aims for between the previous frame and this one: `166'667` for 60 fps, `333'333` for 30 fps.                                            |
| 33     | 8    | Intended display time | `i64` ticks (100 ns) on the frame pacer's steady clock (any epoch, the same clock for the whole run), `0` = unknown. When the pacer intends this frame to become visible (see below).                |
| 41     | 8    | CPU start time        | `i64` ticks (100 ns) on the same steady clock as the intended display time, `0` = unknown. When the CPU started working on this frame (see below).                                                   |
| 49     | 4    | CPU busy              | `u32` ticks (100 ns), `0` = unknown. How long the CPU worked on this frame before presenting it: from the CPU start time until Present is called.                                                    |

Start and end markers carry the values of the frame that shows them: they are frames too, and a sync marker drawn next to them
carries the same frame index. Frame and end markers are exactly these 53 bytes. A **start marker** appends its metadata, 77 bytes
in all:

| Offset | Size | Field       | Notes                                                                                                                                                     |
| ------ | ---- | ----------- | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 53     | 8    | Start time  | `i64` C# `DateTime` UTC ticks (100 ns since 0001-01-01), `0` = unknown. `MB::FrameMarker::ToDateTimeTicks(std::chrono::system_clock::now())` produces it. |
| 61     | 16   | Sequence id | 16 opaque bytes that identify the capture sequence: any content, as long as it is unique to it (a UUID's bytes, or a short text tag padded with zeros).   |

The tools show a sequence id as text when it is printable ASCII (its trailing zero bytes left out), otherwise as 32 hex digits in the
8-4-4-4-12 form of a UUID.

A **sync marker** (kind `3`) is a small second marker for tearing checks and camera timing. It carries only what those need, which
identifies the frame: the header's first 16 bytes.

| Offset | Size | Field          | Notes                                      |
| ------ | ---- | -------------- | ------------------------------------------ |
| 0      | 2    | Magic          | ASCII `"MF"`                               |
| 2      | 1    | Format version | `1`                                        |
| 3      | 1    | Kind           | `3` = Sync                                 |
| 4      | 4    | Run id         | `u32`, the same as the frame's main marker |
| 8      | 8    | Frame index    | `u64`, the same as the frame's main marker |

A sync marker belongs to the main marker with the same run id and frame index.

Decoders reject a payload with the wrong length for its kind, the wrong magic or format version, or an unknown kind.

`AnimationTicks` must come from the same clock the application's animation uses (its "game time"), not from a separate
wall clock. Examples: `TimeSpan.FromSeconds(t).Ticks` in C#, `std::llround(t * 10'000'000.0)` in C++, or
`std::chrono::duration_cast<std::chrono::duration<int64_t, std::ratio<1, 10'000'000>>>(d).count()`.

### Frame pacing: intended display time, target frame time and preferred frame time

Only the application's frame pacer knows what it is aiming for. A capture cannot tell a pacer that deliberately runs at 30 fps
(Swappy dropping to 30 for a busy stretch, a 30 fps cap) from a game that fails to hold 60, it cannot tell a game that wants 30 fps from
one that was forced down to it, and it cannot see that every frame after a hitch stays a refresh late in a full frame queue. The
pacing fields tell the analysis:

- **Intended display time:** the time the pacer aims for this frame to become visible: the vsync it targets, the present time it
  requests from a present timing API (`desiredPresentTime` in `VK_GOOGLE_display_timing`, the target present time of
  `VK_EXT_present_timing`, `EGL_ANDROID_presentation_time`, Swappy), or the **predicted display time** the platform gives it and it
  adopts (OpenXR `predictedDisplayTime`, Android Choreographer's expected presentation time, `CADisplayLink.targetTimestamp`). It is the
  pacer's plan; what the game animated the frame for is its animation time, and in a well paced game the two agree. Use a steady clock
  (`std::chrono::steady_clock`, `QueryPerformanceCounter`, `Stopwatch`) converted to 100 ns ticks; its epoch does not matter, only the
  differences between frames. The analysis lines the clock up with the capture clock itself. A frame shown half a refresh or more
  after its intended time is **late**; that also finds frames that stay late after a hitch.
- **Target frame time:** the interval the pacer aims for before this frame (`1 / target frame rate`). Fill it even without an
  intended display time, for example from a frame limiter. It changes on the frame where the pacer changes its rate.
- **Preferred frame time:** the interval the application wants to run at: what it would aim for if nothing held it back. It differs
  from the target frame time only while the pacer runs slower than it wants:
  - A game locked to 30 fps: preferred and target `333'333`. It runs as it wants.
  - Swappy or another adaptive pacer that drops from 60 to 30 fps for a busy stretch: preferred `166'667`, target `333'333` while
    lowered. The analysis shows that stretch as below its preferred rate.
  - A device that saves power while idle and presents 1 frame per second: preferred and target `10'000'000` while idle. Idle at the
    rate it wants is not a problem.
  - A renderer that presents only when something changes: `0xFFFFFFFF` (**on demand**) in both. There is no interval to aim for, so
    no wait for the next frame is late. The value is allowed in the target frame time too.
  - With variable refresh (VRR) the rates need not be whole refreshes: a pacer on a 144 Hz display can drop from 60 to 48 fps
    (`208'333`) or cap at 117 fps. The fields are intervals, so nothing changes.
- Without a target frame time the analysis measures each frame against the preferred frame time: a game that writes only that it
  wants 30 fps on a 60 Hz display is measured against two refreshes, not one.
- Leave the fields `0` when the application does not pace its frames. The analysis then measures against a target frame rate given
  to the tools, or against the display's native refresh rate (one frame per refresh), and takes the target as the preferred rate.
- With intended display times the analysis splits every frame's animation error exactly: **pacing error** (the display time step
  minus the intended step: the frame was shown off the plan) and **prediction error** (the animation time step minus the intended step:
  the frame was animated for another moment than planned). The animation error is prediction minus pacing error.

### Flags

Static describes a frame's **time on screen**: nothing animates from the moment it is shown until the next frame is (an idle screen,
a paused menu with nothing moving). The frame itself may still have moved: it is often the one that reaches the rest pose. Two bits
say the same fact, for applications that know it at different moments:

- **Bit 0, static after:** nothing animates while this frame is on screen. For an application that knows it while rendering the
  frame (it has no pending work after it). Set it on every such frame.
- **Bit 1, static before:** nothing animated while the frame before this one was on screen. For an application that only knows it
  once it renders the next frame (it woke up on input after waiting); set it on that next frame. It speaks for the frame index before
  it: when that frame was never shown, it marks nothing.
- Either bit, or both, makes the step from that frame to the next a **static step**. The analysis does not judge its animation
  error, because there is no motion to be off: an animation clock that pauses while nothing animates would otherwise make the first
  frame after an idle stretch look as far off as the stretch was long. The step into the static frame is judged as usual. A static
  step can still be late; the static frame's time on screen is left out of the frame rates, which describe the frames that animate.
- The two bits are independent flags, not one value: every combination is valid (inside a static stretch rendered every refresh,
  a frame carries both).
- **Bits 2 to 7** are reserved for future flags. Write `0`. Decoders accept any value and ignore the bits they do not know.

### CPU start time and CPU busy

A capture sees only the display side: when frames appear, not how the application made them. The two optional fields are the
application side, named as in [PresentMon](https://github.com/GameTechDev/PresentMon) (`CPUStartTime`, `MsCPUBusy`) and read from
the same steady clock as the intended display time:

- **CPU start time:** when the CPU started working on this frame (input, simulation, building the render commands). It can be
  anywhere inside a refresh.
- **CPU busy:** how long the CPU worked on this frame before presenting it, from the CPU start time until Present is called. The
  marker is drawn last, just before Present, so the application measures it as it draws the marker. It can span several refreshes.
  It does not include the GPU's work, which usually finishes after Present and is only known later, nor the time spent blocked
  inside Present when the queue is full.

A frame's work does not follow the refresh grid. With more than one frame in flight (triple buffering, a deeper present queue, or
an engine that pipelines across threads) the next frame can start before this one is presented, and two frames can start within one
refresh. The CPU start time places the frame on the timeline, CPU busy gives its length. The step from one frame's CPU start time to
the next frame's is the **frametime** (PresentMon's `MsBetweenAppStart`), which the analysis derives from the markers; it is known
where the next frame index was captured too. Leave the fields `0` when the application does not measure them.

## Symbol

- QR code, **ECC level M**, **byte mode**, mask chosen automatically.
- **Every main marker is version 6** (41×41 modules): frame, start and end markers have the same size, so the marker never changes
  size between frames. Version 6-M holds 106 bytes: a frame or end marker uses 53 of them and a start marker 77, which leaves room
  for future fields.
- **Sync markers are version 2** (25×25 modules). Version 2-M holds 26 bytes; the sync payload uses 16.
- The Reed-Solomon error correction is the integrity check. A capture that mixes two frames (tearing, or a capture taken
  while the display changed frame) either fails ECC or decodes one of the two frames. The analyzer reports what it saw and never
  guesses.
- **Quiet zone:** 4 modules of white around the symbol, as the QR standard requires. It is part of the marker geometry, so
  the application does not need to clear the area first.

Marker size in source pixels = `(modules + 2 × QuietZoneModules) × ModuleSizePx`: `49 × ModuleSizePx` for the main marker and
`33 × ModuleSizePx` for the sync marker with the default quiet zone (`MarkerSizePx(options, kind)`).

## Geometry

- Pixel coordinates with the **origin at the top-left**, **+x right**, **+y down**.
- Every vertex lies on an integer **pixel edge**. A quad covers exactly the pixels `[Left, Right) × [Top, Bottom)`.
- A marker is **encoded once** (`GenerateModules`: the payload's QR symbol as a module matrix, 1 bit per module, packed row-major,
  most significant bit first, 211 bytes for 41×41, 79 for the sync marker's 25×25) and **drawn from the matrix**, in any of these forms:
  - `ModulesToQuads`: the light background quad first (symbol plus quiet zone), then one dark quad per horizontal run of dark modules.
    Draw them in that order. Every marker produces at most 862 quads (`MaxQuadCount()`).
  - `ModulesToTriangles`: 6 vertices per quad, (TL, TR, BL) (BL, TR, BR). `ModulesToIndexed`: 4 vertices (TL, TR, BR, BL) and 6
    indices (0,1,3)(3,1,2) per quad. Both wind clockwise on screen (+y down): disable back-face culling for the marker draw, or pick the
    cull mode that matches. Size caller buffers with `MaxTriangleVertexCount()`, `MaxIndexedVertexCount()` and `MaxIndexCount()`.
  - `GridVertices` and `ModulesToGridIndices`: a static grid of every module corner (vertex 4 + row × (N + 1) + column, after the 4
    background corners) and, per frame, only the indices: the background (0,1,3)(3,1,2), then (TL, TR, BL) (BL, TR, BR) of each dark
    run's corners.
  - `ModulesToBitmap`: the same pixels, drawn into a grey, RGB or RGBA pixel buffer (0 or 255 in every colour channel, alpha 255).

### Renderer rules

The capture pipeline only works if the marker reaches the display output unmodified:

1. Draw the marker **last**, after tonemapping, TAA, upscalers (DLSS/FSR), film grain, UI and HUD.
2. **No blending, no MSAA**, depth test off. Output pure black `(0,0,0)` and white `(255,255,255)`.
3. Use an orthographic projection that maps pixel edges to vertex coordinates, so that vertex `(x, y)` is the top-left corner
   of pixel `(x, y)`. For a `W×H` render target: `clipX = 2x/W − 1`, `clipY = 1 − 2y/H` (flip Y for APIs whose clip-space Y
   points down). Do **not** add the old D3D9 half-pixel offset.
4. Render at the swap chain's resolution. If the application renders at a lower resolution and upscales, draw the marker
   after the upscale.
5. Keep the marker at a **fixed position** every frame. The analyzer locks onto the region after the first detection, and a fast
   capture (`--roi auto`) stores only that region, so a marker that moves is lost.
6. Update the payload every frame, including frames that repeat the same animation time.

## Test sequences

A test run is bracketed by a start and an end marker:

```
... frame markers | START (run R, sequence id, UTC time) | frame markers (run R) | END (run R) | ...
                  |<-- >= 1 captured frame ------>|<-- measured window -->|<-- >= 1 ---->|
                  |    (guidance: ~3 capture frames = 6 ms at 500 fps, 50 ms at 60 fps, 100 ms at 30 fps)
```

1. Pick a run id for the run (a counter or a random `u32`). Every marker of the run carries it.
2. Show the **start marker** before the measured part. Put a sequence id unique to this run (a new UUID, or a text tag) and the wall
   clock start time in its metadata.
   The tools check every captured frame, so **one complete captured frame** of the marker is enough. With vsync and a capture card
   that records every refresh, one rendered frame gives exactly that. As guidance, so that a dropped capture or a
   capture that skips refreshes (a 30 fps screen recording) cannot lose it, show it for about **three frames of the slowest
   capture**: 100 ms covers a 30 fps recording, 50 ms 60 fps, and a few milliseconds a 500 fps capture card.
3. Show **frame markers** for the measured part.
4. Show the **end marker** afterwards, the same way (one complete captured frame is enough, three capture frames recommended).
5. `FrameIndex` and `AnimationTicks` keep counting while the start and end markers are shown; they are real rendered frames.

The analyzer measures the frames between the last captured start marker and the first captured end marker with the same run id.
A capture may contain several runs; each one is reported separately. Without start/end markers the whole capture is analysed as
one run (with a warning). A `FrameIndex` that goes back by more than 1000 (the application restarted) starts a new segment and is never
counted as an error; one that goes back less is an older frame shown again (out of order). An application that restarts gives the new
run a new run id.

`mb-framepacing capture --wait-for-start --stop-at-end` uses the same markers to start and stop the recording automatically. It
checks every captured frame (a live capture as it arrives, a video file frame by frame), so one captured frame of each marker is enough.

## Sizing

What matters is how many **stored pixels** one QR module covers after all scaling: the GPU output resolution, the capture
card's mode and `mb-framepacing capture --scale`.

Let `s = storedHeight / sourceHeight`. For example, a 2160p source stored at 540p gives `s = 0.25`.

| Rule                                                                                                                     | Module size in source px | Stored px per module |
| ------------------------------------------------------------------------------------------------------------------------ | ------------------------ | -------------------- |
| **Hard minimum.** Below this, decoding is unreliable.                                                                    | `ceil(2 / s)`            | 2                    |
| **Recommended.** Leaves margin for scaler blur and limited-range (16–235) video.                                         | `ceil(3 / s)`            | 3                    |
| **MJPEG capture.** Many USB capture cards only reach high frame rates with MJPEG; the 8×8 DCT blocks smear module edges. | `ceil(4 / s)`            | 4                    |

`MB::FrameMarker::MinimumModuleSizePx(sourceHeight, storedHeight)` and
`MB::FrameMarker::RecommendModuleSizePx(sourceHeight, storedHeight, mjpeg)` implement these formulas (C# and Unity: `Marker.*`; Python: `minimum_module_size_px`, `recommend_module_size_px`).
`mb-framepacing marker-size --source 3840x2160 --stored 960x540 [--mjpeg]` prints the result for a setup, with the marker sizes, the
origin and the settings for each library.

| Source → stored             | s     | Minimum module px | Recommended module px   | Marker size at recommended |
| --------------------------- | ----- | ----------------- | ----------------------- | -------------------------- |
| 1:1                         | 1     | 2                 | 3 (4 if MJPEG)          | 147 px (196 px)            |
| 1440p → 1080p               | 0.75  | 3                 | 4                       | 196 px                     |
| 1080p → 540p, 2160p → 1080p | 0.5   | 4                 | **6 (library default)** | 294 px                     |
| 1080p → 360p                | 0.333 | 6                 | 9                       | 441 px                     |
| 2160p → 540p                | 0.25  | 8                 | 12                      | 588 px                     |

### Alignment

- Prefer **integer downscale ratios** (2:1, 3:1, 4:1). With an integer ratio `k`, make `ModuleSizePx` and the marker origin
  multiples of `k`. Every module edge then lands on a stored-pixel edge and the downscaled marker stays perfectly sharp.
  `RecommendModuleSizePx` already returns a multiple of `k`. Pass `k` as `alignPx` to `RecommendedOrigin`.
- **At the hard minimum (2 stored px per module) alignment is required, not optional.** The test suite shows that aligned
  2 px markers decode reliably, while the same markers shifted off the scaling grid do not decode at all.
- A non-integer ratio (for example 1440p → 1080p) still works, but use at least the recommended size, not the minimum.
- `--roi` crops first and `--scale` then scales the crop, so `s` is the `--scale` height divided by the `--roi` height (1 without
  `--scale`).
- `--roi auto` (fast capture) crops the region around the marker's origin that holds the marker, starting a whole
  number of downscale steps before the origin, and downscales it by the largest integer ratio that keeps the recommended stored
  size (3 px per module, 4 with MJPEG). Only the top marker is stored, so tearing is not checked.

### Checks in the tools

- `mb-framepacing capture --module-px <n>` logs the stored pixels per module it expects for the chosen mode and scale.
  It warns below 3 and errors below 2.
- `mb-framepacing analyze` measures the module size of the first detected marker. It warns if the size is below 3 stored px per
  module and errors if it is below 2.

## Location

**Capture at the display's refresh rate** (a 240 Hz output in the card's 240 fps mode), so that every refresh is one captured frame;
a slower capture never sees some displayed frames.

**Measure with vsync at a fixed refresh rate.** Then every displayed frame is whole, and the marker describes the frame the viewer
sees.

**Variable refresh (G-Sync/FreeSync) is not supported through a capture card.** Cards pass variable refresh through to the monitor
but record at a constant frame rate, taking the newest frame at each tick, so the capture does not show when the display showed
each frame. Turn variable refresh off for a capture card measurement. A high speed camera filming the screen does see the real
display timing, variable refresh included. That is **very experimental**: see [camera capture](../../measure/doc/camera.md).

**Camera capture (very experimental)** needs the **sync marker** as well, at a fixed position, with at least 3 camera pixels per
module. The camera sees the two markers at different times while the scanout rolls down the screen. The analysis times every frame
by the small sync marker, which the scanout crosses quickly, reads the frame's data from the main marker, measures the scanout
between the two, and finds tears instead of treating different markers in one camera frame as tearing.

**Vsync off** is not recommended. With vsync off, one refresh shows slices of several frames; the marker then only
reports the frame at the top of the screen, frames shown only lower down are never seen, and the numbers are easy to misread.
The rules below keep vsync-off captures consistent, but they are not what the tool is meant for.

**Primary marker: top-left, inset 32 px from both edges.** That is origin `(32, 32)` in source pixels, rounded up to a
multiple of the downscale ratio.

- The top of the frame is scanned out first. With vsync off, the top marker therefore belongs to the frame whose scan-out
  started in that captured frame, which keeps the analyzer's "first seen" time consistent.
- The inset keeps the marker away from scaler edge artefacts and capture-card cropping, and clear of TV overscan if the
  signal is mirrored to a TV.

**Sync marker: bottom-left**, at the same X, `y = sourceHeight − 32 − syncMarkerSize`. It carries the same run id and frame index as
the main marker. Optional for a capture card, where it checks tearing: when the two markers show different frames, the analyzer flags the
capture as _torn_ and uses the main marker for timing. Required for camera capture.

`MB::FrameMarker::RecommendedOrigin(kind, sourceWidth, sourceHeight, options, alignPx)` returns these positions: bottom-left for
`MarkerKind::Sync`, top-left for every other kind.

## Example (C++)

```cpp
#include <mb/framemarker/FrameMarker.hpp>
namespace FM = MB::FrameMarker;

// Once: 1080p output captured and stored at 540p (2:1)
const FM::Options options{FM::RecommendModuleSizePx(1080, 540), FM::RecommendedQuietZoneModules};   // 6 px
const FM::Point origin = FM::RecommendedOrigin(FM::MarkerKind::Frame, 1920, 1080, options, 2);

FM::ModuleMatrix matrix;
std::array<FM::Vertex, FM::MaxTriangleVertexCount()> vertices;

// Every frame, after all post-processing and UI
// Pacing: when the pacer intends this frame to be shown (steady clock ticks), its target frame time and the frame time the
// application wants to run at. When the CPU started this frame (the same clock) and how long it has worked on it until now (the
// marker is drawn last, just before Present). 0 = unknown. MarkerFlags::StaticAfter when nothing animates while this frame is
// on screen, MarkerFlags::StaticBefore when the application only now knows nothing animated while the previous frame was
const FM::Payload payload{.FrameIndex = frameIndex,
                          .AnimationTicks = animationTicks,
                          .RunId = runId,
                          .Kind = kind,
                          .IntendedDisplayTicks = intendedDisplayTicks,
                          .TargetFrameTicks = targetFrameTicks,
                          .CpuStartTicks = cpuStartTicks,
                          .CpuBusyTicks = cpuBusyTicks,
                          .PreferredFrameTicks = preferredFrameTicks,
                          .Flags = FM::MarkerFlags::None};
// A start marker carries the run's metadata, captured once when the run started: startUtcTicks =
// FM::ToDateTimeTicks(std::chrono::system_clock::now()), and a sequence id unique to the run (a UUID's 16 bytes, or a text tag:
// FM::SequenceId::TryFromText("menu-scroll", sequenceId)). Other kinds ignore it.
FM::GenerateModules(payload, matrix, {startUtcTicks, sequenceId});                       // encode once
const std::size_t vertexCount = FM::ModulesToTriangles(matrix, options, origin, vertices); // draw it
// upload vertices[0..vertexCount) and draw them as a triangle list with color (Luma, Luma, Luma)
```

## Requirements

- C++: CMake 4.0 or newer and a C++20 compiler (MSVC 19.4x / Visual Studio 2026, GCC 12+, Clang 16+, AppleClang 15+).
  The library has no external dependencies; the unit tests fetch GoogleTest unless an installed one is found.
- C#: .NET 10 SDK.
