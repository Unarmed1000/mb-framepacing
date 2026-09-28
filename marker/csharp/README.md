# MB Frame Marker for C#

Draws the [mb-framepacing](https://github.com/Unarmed1000/mb-framepacing) frame marker into every frame of an application: a small
QR code that carries the frame index and the animation time. A capture of the display output, analysed with the mb-framepacing
tools, then shows the **animation error**: how far what the application animated is from what was actually shown on screen.

`MB.FrameMarker`: .NET Standard 2.1, C# 9, no dependencies. It is the same API as the C++ library and draws exactly the same pixels
(the tests check it against the golden images in [`test-data/markers`](../../test-data/markers)). The format is specified in
[marker-format.md](../../doc/marker-format.md).

## Add it

It is not published as a NuGet package. Use the source at a `marker-v*` tag, as a project reference to
`MB.FrameMarker.csproj` or as a copy of `source/`. **Unity:** use the [Unity package](../unity/README.md) instead; it contains this
library plus an overlay component.

## Quick start

```csharp
using MB.FrameMarker;

// Once: output 1920x1080, capture stored at 960x540 (2:1)
var options = new Options(Marker.RecommendModuleSizePx(1080, 540));
Point origin = Marker.RecommendedOrigin(MarkerKind.Frame, 1920, 1080, options, alignPx: 2);
var generator = new MarkerGenerator();                      // owns the QR encoder's buffers; reuse it
var modules = new byte[Marker.MaxPackedModuleByteCount];    // the encoded marker (or a stackalloc)
var vertices = new Vertex[Marker.MaxTriangleVertexCount];

// Every frame, last (after post effects and UI), without blending:
var payload = new Payload(frameIndex, Marker.SecondsToTicks(animationSeconds), runId: 1);
generator.TryGenerateModules(payload, modules, out var matrix);            // encode once
int count = Marker.ModulesToTriangles(matrix, options, origin, vertices);   // draw it
DrawTriangles(vertices.AsSpan(0, count));                   // your renderer: (X, Y) in pixels, color (Luma, Luma, Luma)
```

- **Frame index:** the frame's own index, the same for every refresh the frame stays on screen.
- **Animation time:** the moment the frame shows, as the application animated it, in 100 ns ticks (`Marker.SecondsToTicks`).
- **Frame pacing (optional):** `intendedDisplayTicks` (when the pacer intends the frame to be shown, 100 ns ticks on its steady
  clock, any epoch) and `targetFrameTicks` (the interval it aims for: `166_667` for 60 fps). `0` = unknown.
- **CPU start time and CPU busy (optional):** `cpuStartTicks` (when the CPU started working on the frame, on the same clock,
  PresentMon's `CPUStartTime`) and `cpuBusyTicks` (how long until Present, PresentMon's `MsCPUBusy`). `0` = unknown.
- **Start and end:** bracket the part to measure with a payload of kind `MarkerKind.SequenceStart`, encoded with its metadata
  (`TryGenerateModules(payload, StartMetadata.Create(DateTime.UtcNow, sequenceId), modules, out var matrix)`), and a payload of kind `MarkerKind.SequenceEnd`, each shown for a few frames. The sequence id is 16 opaque bytes
  unique to the run: `SequenceId.FromGuid(Guid.NewGuid())` or a text tag of up to 16 printable ASCII characters
  (`SequenceId.TryFromText("run-42", out var id)`).
- **Sync marker (optional; required for camera capture):** a small second marker with only the frame index, drawn bottom-left
  (`RecommendedOrigin(MarkerKind.Sync, …)`) with a payload of kind `MarkerKind.Sync`.
- **Size:** every main marker (frame, start, end) is QR version 6, 41×41 modules, so it never changes size:
  `Marker.MarkerSizePx(options)`. The sync marker is QR version 2, 25×25 modules.

## API

Buffers are spans: `ReadOnlySpan<T>` in, `Span<T>` out; an array or a `stackalloc` passes straight in. Nothing allocates per frame.

| Type or member                                                                                                        | What it does                                                                         |
| --------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------ |
| `Payload`, `StartMetadata`, `SequenceId`, `MarkerKind`                                                                | What a marker carries                                                                |
| `Options`, `Point`                                                                                                    | Size and place                                                                       |
| `MarkerGenerator.TryGenerateModules`, `ModuleMatrix` (`Size`, `IsDark`, `Bits`)                                       | Encode the marker into your bytes: its QR symbol, 1 bit per module                   |
| `Marker.ModulesToTriangles`, `ModulesToIndexed`, `ModulesToQuads`                                                     | Draw it as a triangle list, indexed triangles or quads                               |
| `Marker.ModulesToBitmap`, `PixelFormat`, `BytesPerPixel`                                                              | Draw it into a pixel buffer (`[L]`, `[R, G, B]` or `[R, G, B, A]` bytes; any stride) |
| `Marker.MaxTriangleVertexCount`, `MaxIndexedVertexCount`, `MaxIndexCount`, `MaxQuadCount`, `MaxPackedModuleByteCount` | Buffer sizes that fit every marker kind                                              |
| `Marker.MarkerSizePx`, `QrModuleCountFor`, `RecommendedOrigin`                                                        | Sizing and placement                                                                 |
| `Marker.MinimumModuleSizePx`, `RecommendModuleSizePx`                                                                 | Module size for a capture's scaling                                                  |
| `Marker.EncodePayload`, `TryDecodePayload`, `SecondsToTicks`, `ToDateTimeTicks`                                       | The wire format and its time units                                                   |

`ModuleMatrix` is a `ref struct` view over the bytes you give `TryGenerateModules`: keep the bytes, not the view, in a field. One encode
can feed several outputs. The drawing methods return 0 (an empty `IndexedCount`, false) when the options are invalid, the matrix is empty
or a buffer is too small.

## Tests

```sh
dotnet test marker/csharp/UnitTest
```

NUnit, including the golden images, the cross-language module digest and zero-allocation tests. The golden data is found by walking
up from the test directory to `test-data/markers`.

## License

BSD 3-Clause ([LICENSE](../LICENSE)). The QR encoder (`source/QrEncoder.cs`) is a port of the QR Code generator library by Project
Nayuki, MIT; its notice is in the file.
