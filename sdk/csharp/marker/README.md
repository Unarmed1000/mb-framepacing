# MB Frame Pacing Marker for C#

Draws the [mb-framepacing](https://github.com/Unarmed1000/mb-framepacing) frame marker into every frame of an application: a small
QR code that carries the frame index and the animation time. A capture of the display output, analysed with the mb-framepacing
tools, then shows the **animation error**: how far what the application animated is from what was actually shown on screen.

`MB.FramePacing.Marker`, the SDK's C# marker module: .NET Standard 2.1, C# 9, no dependencies besides the SDK's core
([`MB.FramePacing`](../core/README.md)). It is the same API as the C++ marker
module and draws exactly the same pixels (the tests check it against the golden images in [`test-data/markers`](../../test-data/markers)). The format is specified in
[marker-format.md](../../doc/marker-format.md); what to write in each field is in [Filling the marker fields](../../doc/marker-fields.md).

## Add it

It is not published as a NuGet package. Use the source at an `sdk-v*` tag, as a project reference to
`MB.FramePacing.Marker.csproj` (it references `../core`) or as a copy of `source/` and `../core/source/`. **Unity:** use the [Unity package](../../unity/README.md) instead; it contains
this module plus an overlay component.

## Quick start

```csharp
using MB.FramePacing;
using MB.FramePacing.Marker;

// Once: output 1920x1080, capture stored at 960x540 (2:1)
var options = Options.Recommended(1080, 540);               // 6 px modules, the recommended quiet zone
Point origin = options.RecommendedOrigin(MarkerKind.Frame, 1080, alignPx: 2);
var grid = new Vertex[FrameMarker.MaxGridVertexCount];
int gridCount = FrameMarker.GridVertices(MarkerKind.Frame, options, origin, grid);
UploadVertices(grid.AsSpan(0, gridCount));                  // your renderer: a static vertex buffer, (X, Y) in pixels, color (Luma, Luma, Luma)
var generator = new MarkerGenerator();                      // owns the QR encoder's buffers; reuse it
var modules = new byte[ModuleMatrix.MaxPackedModuleByteCount];   // the encoded marker (or a stackalloc)
var indices = new int[FrameMarker.MaxIndexCount];

// Every frame, last (after post effects and UI), without blending:
var payload = new Payload(MarkerKind.Frame, 1, frameIndex, MarkerFlags.None, TimeSpanUtil.FromSeconds(animationSeconds));
generator.TryGenerateModules(payload, modules, out var matrix);   // encode once
int count = FrameMarker.ModulesToGridIndices(matrix, indices);         // only the indices change
DrawIndexed(indices.AsSpan(0, count));                            // triangles over the static vertices
```

This is the most efficient way without a dedicated shader; the shaders (1 and 2 in [the options](#ways-to-draw-it-most-efficient-first)) are faster still.

- **Frame index:** the frame's own index, the same for every refresh the frame stays on screen.
- **Flags:** `MarkerFlags.None`, or `MarkerFlags.StaticAfter` on a frame when nothing animates while it is on screen, or `MarkerFlags.StaticBefore`
  on the next frame when that is only known then (the analysis does not judge the step out of the static frame).
- **Animation time:** the moment the frame shows, as the application animated it: a `TimeSpan` (`TimeSpanUtil.FromSeconds` converts
  seconds to the tick on every runtime; Unity's `TimeSpan.FromSeconds` rounds to a millisecond).
- **Frame pacing (optional):** `preferredFrameTime` (a `TimeSpan32`: the interval the application wants to run at; it differs from the
  target only while the pacer runs slower than wanted), `targetFrameTime` (a `TimeSpan32`: the interval the pacer aims for, `166_667`
  ticks for 60 fps) and `intendedDisplayTime` (a `TickCount64`: when the pacer intends the frame to be shown on its steady clock, any
  epoch; `TickCount64.FromCounter(Stopwatch.GetTimestamp(), Stopwatch.Frequency)` converts yours). `0` = unknown,
  `Payload.OnDemandFrameTime` = frames only when something changes.
- **CPU start time and CPU busy (optional):** `cpuStartTime` (a `TickCount64`: when the CPU started working on the frame, on the same
  clock, PresentMon's `CPUStartTime`) and `cpuBusy` (a `TimeSpan32`: how long until Present, PresentMon's `MsCPUBusy`). `0` = unknown.
- **Start and end:** bracket the part to measure with a payload of kind `MarkerKind.SequenceStart`, encoded with its metadata
  (`TryGenerateModules(payload, StartMetadata.Create(DateTime.UtcNow, sequenceId), modules, out var matrix)`), and a payload of kind `MarkerKind.SequenceEnd`, each shown for a few frames. The sequence id is 16 opaque bytes
  unique to the run: `SequenceId.FromGuid(Guid.NewGuid())` or a text tag of up to 16 printable ASCII characters
  (`SequenceId.TryFromText("run-42", out var id)`).
- **Sync marker (optional; required for camera capture):** a small second marker with only the run id and frame index, drawn bottom-left
  (`options.RecommendedOrigin(MarkerKind.Sync, …)`) with a payload of kind `MarkerKind.Sync`.
- **Size:** every main marker (frame, start, end) is QR version 6, 41×41 modules, so it never changes size:
  `options.MarkerSizePx()`. The sync marker is QR version 2, 25×25 modules.

## Ways to draw it, most efficient first

| #   | Option                                                                            | Per frame (41×41 main marker, about 440 dark runs)           | Needs                                                                         |
| --- | --------------------------------------------------------------------------------- | ------------------------------------------------------------ | ----------------------------------------------------------------------------- |
| 1   | **Shader, packed bits**: one quad; the shader reads each module's bit from `Bits` | 211 bytes (the packed bits as constants), 4 vertices         | A shader of ours: HLSL, GLSL for OpenGL 3.3 / ES 3.0, OpenGL ES 2.0 or Vulkan |
| 2   | **Shader, a texel per module**: one quad and a 41×41 texture                      | 1,681 bytes (the texture), 4 vertices                        | The same, and a single channel texture                                        |
| 3   | **Static grid**: `GridVertices` once, then `ModulesToGridIndices`                 | About 2,600 indices (10 KB as 32 bit, 5 KB as 16 bit)        | Index buffers and vertex colours; the 1,768 vertices stay                     |
| 4   | **Module texture scaled up**: `ModulesToBitmap` at 1 px per module                | 1,681 pixels                                                 | A texture drawn scaled by a whole number with point filtering, pixel exact    |
| 5   | **Triangles**: `ModulesToIndexed` or `ModulesToTriangles`                         | About 1,750 vertices and 2,600 indices, or 2,600 vertices    | Only vertex colours: the simplest to add to a renderer                        |
| 6   | **Rectangles**: `ModulesToQuads`                                                  | About 440 filled rectangles (`MarkerQuad`)                   | A 2D fill-rectangle API                                                       |
| 7   | **Full-size bitmap**: `ModulesToBitmap`                                           | The marker's pixels (294×294 at 6 px per module: 86 KB grey) | A CPU pixel buffer: software rendering, video frames, images                  |

Every option draws exactly the same pixels, from one encode per frame (the 211 byte module matrix). The shaders for 1 and 2
(HLSL for Direct3D, GLSL for OpenGL 3.3 and OpenGL ES 3.0, OpenGL ES 2.0 and Vulkan) and how to draw them are in
[`sdk/shaders`](../../shaders/README.md). On OpenGL ES 2.0 they need `highp` floats in the fragment shader; without it, draw 3 or 5. In Unity, the [Unity package](../../unity/README.md)'s overlay does it for you.

## API

Buffers are spans: `ReadOnlySpan<T>` in, `Span<T>` out; an array or a `stackalloc` passes straight in. Nothing allocates per frame.

| Type or member                                                                                                                          | What it does                                                                          |
| --------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| `Payload` (`WithKind`, `MaxEncodedByteCount`, `OnDemandFrameTime`), `StartMetadata`, `SequenceId`, `MarkerKind`, `MarkerFlags`          | What a marker carries; a kind that is not a `MarkerKind` throws                       |
| `Options` (`Recommended`, `Minimum`, `MarkerSizePx`, `QuietZonePx`, `RecommendedOrigin`, its limits and defaults); `Point` (the core's) | Size and place: always valid (a value outside its range is clamped)                   |
| `MarkerGenerator.TryGenerateModules`, `ModuleMatrix` (`Size`, `IsDark`, `Bits`, `MainSize`, `SyncSize`, `SizeFor`)                      | Encode the marker into your bytes: its QR symbol, 1 bit per module                    |
| `FrameMarker.GridVertices`, `GridVertexCount`, `MaxGridVertexCount`, `ModulesToGridIndices`                                             | A static grid uploaded once, and per frame only the indices                           |
| `FrameMarker.ModulesToBitmap`, `PixelFormat`, `PixelFormatUtil.BytesPerPixel`                                                           | Draw it into a pixel buffer (`[L]`, `[R, G, B]` or `[R, G, B, A]` bytes; any stride)  |
| `FrameMarker.ModulesToIndexed`, `ModulesToTriangles`, `ModulesToQuads` (`MarkerQuad`: `Rect`, a `Rectangle`, and `Dark`)                | Draw it as indexed triangles, a triangle list or rectangles                           |
| `FrameMarker.MaxTriangleVertexCount`, `MaxIndexedVertexCount`, `MaxIndexCount`, `MaxQuadCount`, `ModuleMatrix.MaxPackedModuleByteCount` | Buffer sizes that fit every marker kind                                               |
| `FrameMarker.EncodePayload`, `TryDecodePayload`                                                                                         | The wire format (its layout is internal: `sdk/doc/marker-format.md` is the reference) |

`ModuleMatrix` is a `ref struct` view over the bytes you give `TryGenerateModules`: keep the bytes, not the view, in a field. One encode
can feed several outputs. The drawing methods return 0 (an empty `IndexedCount`, false) when the matrix is empty or a buffer is too small.

## Tests

```sh
dotnet test sdk/csharp/marker/UnitTest
```

NUnit, including the golden images, the cross-language module digest and zero-allocation tests. The golden data is found by walking
up from the test directory to `test-data/markers`.

The benchmarks (`Benchmarks`, BenchmarkDotNet) time every per-frame step, as the C++ module's do: encoding (`EncodePayload`,
`TryGenerateModules` for a frame, start and sync marker), every way to draw a matrix, bitmaps in every pixel format, and a whole
frame (encode and draw the main and the sync marker):

```sh
dotnet run -c Release --project sdk/csharp/marker/Benchmarks/MB.FramePacing.Marker.Benchmarks.csproj -- --filter "*"
```

## License

BSD 3-Clause ([LICENSE](../../LICENSE)). The QR encoder (`source/QrEncoder.cs`) is a port of the QR Code generator library by Project
Nayuki, MIT; its notice is in the file.
