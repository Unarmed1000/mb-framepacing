# Frame marker format (version 1)

The frame marker is a QR code that the application under test draws into every frame. It carries the application's **frame
index**, the **animation time** the frame was rendered for and a **run id**. `mb-framepacing` captures the display output with an
HDMI/DP capture card, decodes the marker in every captured frame, and compares the animation timeline with the capture timeline.
Special **start** and **end** markers bracket a test run so the analyzer can cut the capture to exactly the measured window.

The C++20 library in [`cpp/`](../cpp) generates the marker geometry. The C# library `MB.FramePacing.Marker` decodes it.
Both implement this document; if they disagree, this document is the reference.

> **Two counters, never mixed.** The marker's _frame index_ is the application's own rendered-frame counter. The capture tool
> keeps a separate _capture index_, one per frame the capture card delivers. The two run at different rates (for example a
> 144 Hz game captured at 240 fps), and each can have gaps or restart, so they are never compared with each other.

## Payload

Every marker starts with the same 24 byte header, little endian:

| Offset | Size | Field          | Notes                                                                                                               |
| ------ | ---- | -------------- | ------------------------------------------------------------------------------------------------------------------- |
| 0      | 2    | Magic          | ASCII `"MF"` (`0x4D 0x46`)                                                                                          |
| 2      | 1    | Format version | `1`                                                                                                                 |
| 3      | 1    | Kind           | `0` = Frame, `1` = SequenceStart, `2` = SequenceEnd                                                                 |
| 4      | 8    | Frame index    | `u64`. Increments by 1 for every frame the application renders, including frames that show a start/end marker.      |
| 12     | 8    | Animation time | `i64` two's complement, C# `TimeSpan` ticks (100 ns). The time the frame's animation was evaluated for.             |
| 20     | 4    | Run id         | `u32`. Identifies one test run; the start marker, every frame marker and the end marker of a run carry the same id. |

Frame and end markers are exactly these 24 bytes. A **start marker** appends its metadata:

| Offset | Size | Field       | Notes                                                                                                                                                     |
| ------ | ---- | ----------- | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 24     | 8    | Start time  | `i64` C# `DateTime` UTC ticks (100 ns since 0001-01-01), `0` = unknown. `MB::FrameMarker::ToDateTimeTicks(std::chrono::system_clock::now())` produces it. |
| 32     | 1    | Name length | `0`..`64`                                                                                                                                                 |
| 33     | n    | Name        | UTF-8 test name, at most 64 bytes, no terminator                                                                                                          |

Decoders reject a payload with the wrong length, magic, format version, an unknown kind or (for start markers) invalid UTF-8.

`AnimationTicks` must come from the same clock the application's animation uses (its "game time"), not from a separate
wall clock. Examples: `TimeSpan.FromSeconds(t).Ticks` in C#, `static_cast<int64_t>(t * 10'000'000.0)` in C++, or
`std::chrono::duration_cast<std::chrono::duration<int64_t, std::ratio<1, 10'000'000>>>(d).count()`.

## Symbol

- QR code, **ECC level M**, **byte mode**, mask chosen automatically.
- **Frame and end markers are fixed to version 2** (25×25 modules), so they never change size between frames. Version 2-M
  holds 26 bytes; the 24 byte payload fits.
- **Start markers** use the smallest version from 2 to 6 that fits the metadata: version 3 (29 modules) with an empty name, up to
  version 6 (41 modules) with a 64 byte name. They are drawn at the **same origin** as the frame marker and are larger, so keep
  `MaxMarkerSizePx(options)` (49 modules with the default quiet zone) clear around the origin while a start marker is shown.
- The Reed-Solomon error correction is the integrity check. A capture that mixes two frames (tearing, or a capture taken
  while the display changed frame) either fails ECC or decodes one of the two frames. The analyzer reports what it saw and never
  guesses.
- **Quiet zone:** 4 modules of white around the symbol, as the QR standard requires. It is part of the marker geometry, so
  the application does not need to clear the area first.

Marker size in source pixels = `(moduleCount + 2 × QuietZoneModules) × ModuleSizePx`: `33 × ModuleSizePx` for frame and end
markers with the default quiet zone, at most `49 × ModuleSizePx` for a start marker.

## Geometry

- Pixel coordinates with the **origin at the top-left**, **+x right**, **+y down**.
- Every vertex lies on an integer **pixel edge**. A quad covers exactly the pixels `[Left, Right) × [Top, Bottom)`.
- `GenerateQuads` (frame and end markers) and `GenerateStartQuads` (start marker with metadata) return the light background
  quad first (symbol plus quiet zone), then one dark quad per horizontal run of dark modules. Draw them in that order.
  Frame and end markers produce at most 326 quads (`MaxFrameQuadCount()`), start markers at most 862 (`MaxQuadCount()`).
  Size caller buffers with `MaxQuadCount()` / `MaxTriangleVertexCount()` to handle every kind.
- `QuadsToTriangles` produces 6 vertices per quad. `QuadsToIndexed` produces 4 vertices and 6 indices per quad. Both wind clockwise on screen
  (+y down). Disable back-face culling for the marker draw, or pick the cull mode that matches.

### Renderer rules

The capture pipeline only works if the marker reaches the display output unmodified:

1. Draw the marker **last**, after tonemapping, TAA, upscalers (DLSS/FSR), film grain, UI and HUD.
2. **No blending, no MSAA**, depth test off. Output pure black `(0,0,0)` and white `(255,255,255)`.
3. Use an orthographic projection that maps pixel edges to vertex coordinates, so that vertex `(x, y)` is the top-left corner
   of pixel `(x, y)`. For a `W×H` render target: `clipX = 2x/W − 1`, `clipY = 1 − 2y/H` (flip Y for APIs whose clip-space Y
   points down). Do **not** add the old D3D9 half-pixel offset.
4. Render at the swap chain's resolution. If the application renders at a lower resolution and upscales, draw the marker
   after the upscale.
5. Keep the marker at a **fixed position** every frame. The analyzer locks onto the region after the first detection.
6. Update the payload every frame, including frames that repeat the same animation time.

## Test sequences

A test run is bracketed by a start and an end marker:

```
... frame markers | START (run R, name, UTC time) | frame markers (run R) | END (run R) | ...
                  |<------- show >= 250 ms ------>|<-- measured window -->|<- >= 250 ms ->|
```

1. Pick a run id for the run (a counter or a random `u32`). Every marker of the run carries it.
2. Show the **start marker** for at least **250 ms** before the measured part, so several captured frames contain it even at low
   capture rates. Put the test name and the wall clock start time in its metadata.
3. Show **frame markers** for the measured part.
4. Show the **end marker** for at least **250 ms** afterwards.
5. `FrameIndex` and `AnimationTicks` keep counting while the start and end markers are shown; they are real rendered frames.

The analyzer measures the frames between the last captured start marker and the first captured end marker with the same run id.
A capture may contain several runs; each one is reported separately. Without start/end markers the whole capture is analysed as
one run (with a warning). A backwards `FrameIndex` jump or a new run id (for example the application restarted) starts a new segment
and is never counted as an error.

`mb-framepacing capture --wait-for-start --stop-at-end` uses the same markers to start and stop the recording automatically.

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
`MB::FrameMarker::RecommendModuleSizePx(sourceHeight, storedHeight, mjpeg)` implement these formulas.

| Source → stored             | s     | Minimum module px | Recommended module px   | Marker size at recommended |
| --------------------------- | ----- | ----------------- | ----------------------- | -------------------------- |
| 1:1 (or `--roi`)            | 1     | 2                 | 3 (4 if MJPEG)          | 99 px (132 px)             |
| 1440p → 1080p               | 0.75  | 3                 | 4                       | 132 px                     |
| 1080p → 540p, 2160p → 1080p | 0.5   | 4                 | **6 (library default)** | 198 px                     |
| 1080p → 360p                | 0.333 | 6                 | 9                       | 297 px                     |
| 2160p → 540p                | 0.25  | 8                 | 12                      | 396 px                     |

### Alignment

- Prefer **integer downscale ratios** (2:1, 3:1, 4:1). With an integer ratio `k`, make `ModuleSizePx` and the marker origin
  multiples of `k`. Every module edge then lands on a stored-pixel edge and the downscaled marker stays perfectly sharp.
  `RecommendModuleSizePx` already returns a multiple of `k`. Pass `k` as `alignPx` to `RecommendedOrigin`.
- **At the hard minimum (2 stored px per module) alignment is required, not optional.** The test suite shows that aligned
  2 px markers decode reliably, while the same markers shifted off the scaling grid do not decode at all.
- A non-integer ratio (for example 1440p → 1080p) still works, but use at least the recommended size, not the minimum.
- With `capture --roi`, the marker region is stored at native resolution (`s = 1`) whatever `--scale` is.

### Checks in the tools

- `mb-framepacing capture --module-px <n>` logs the stored pixels per module it expects for the chosen mode and scale.
  It warns below 3 and errors below 2.
- `mb-framepacing analyze` measures the module size of the first detected marker. It warns if the size is below 3 stored px per
  module and errors if it is below 2.

## Location

**Primary marker: top-left, inset 32 px from both edges.** That is origin `(32, 32)` in source pixels, rounded up to a
multiple of the downscale ratio.

- The top of the frame is scanned out first. With vsync off, the top marker therefore belongs to the frame whose scan-out
  started in that captured frame, which keeps the analyzer's "first seen" time consistent.
- The inset keeps the marker away from scaler edge artefacts and capture-card cropping, and clear of TV overscan if the
  signal is mirrored to a TV.

**Optional tearing markers:** at the same X, vertically centred (`MiddleLeft`) and at the bottom (`BottomLeft`,
`y = sourceHeight − 32 − markerSize`). Encode the same payload in all of them. When they decode to different frames, the
analyzer flags the capture as _torn_ and uses the top marker for timing.

`MB::FrameMarker::RecommendedOrigin(slot, sourceWidth, sourceHeight, options, alignPx)` returns these positions.

## Example (C++)

```cpp
#include <mb/framemarker/FrameMarker.hpp>
namespace FM = MB::FrameMarker;

// Once: 1080p output captured and stored at 540p (2:1)
const FM::Options options{FM::RecommendModuleSizePx(1080, 540), FM::RecommendedQuietZoneModules};   // 6 px
const FM::Point origin = FM::RecommendedOrigin(FM::MarkerSlot::TopLeft, 1920, 1080, options, 2);

std::array<FM::Quad, FM::MaxQuadCount()> quads;
std::array<FM::Vertex, FM::MaxTriangleVertexCount()> vertices;

// Every frame, after all post-processing and UI
std::size_t quadCount = 0;
const FM::Payload payload{frameIndex, animationTicks, runId, kind};
if (kind == FM::MarkerKind::SequenceStart)
{
  // startUtcTicks captured once when the run started: FM::ToDateTimeTicks(std::chrono::system_clock::now())
  quadCount = FM::GenerateStartQuads(payload, {startUtcTicks, "menu-scroll benchmark"}, options, origin, quads);
}
else
{
  quadCount = FM::GenerateQuads(payload, options, origin, quads);
}
const std::size_t vertexCount = FM::QuadsToTriangles(std::span(quads).first(quadCount), vertices);
// upload vertices[0..vertexCount) and draw them as a triangle list with color (Luma, Luma, Luma)
```

## Requirements

- C++: CMake 4.0 or newer and a C++20 compiler (MSVC 19.4x / Visual Studio 2026, GCC 12+, Clang 16+, AppleClang 15+).
  The library has no external dependencies; the unit tests fetch GoogleTest unless an installed one is found.
- C#: .NET 10 SDK.
