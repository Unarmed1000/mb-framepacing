# MB Frame Marker for Unity

Draws the [mb-framepacing](https://github.com/Unarmed1000/mb-framepacing) frame marker into every frame of your game: a small QR code
that carries the frame index and the animation time. A capture of the display output, analysed with the mb-framepacing tools, then
shows the **animation error**: how far what the game animated is from what was actually shown on screen.

## Contents

- **`MB.FrameMarker`** (`Runtime/Core`): the general C# marker library (no Unity dependency). It is the same code as the .NET library.
- **`MB.FrameMarker.Unity`** (`Runtime/Unity`):
  - `FrameMarkerOverlay`: add it to a GameObject and the marker is drawn at the end of every frame, on top of everything.
  - `BeginRun` / `EndRun` / `RunFor`: bracket the part to measure with start and end markers.
  - `FrameMarkerQuad`: the marker as one quad with a dedicated shader, from the packed bits (`Hidden/MB/FrameMarkerQuadPacked`) or a
    texel per module (`Hidden/MB/FrameMarkerQuad`); shader model 3.5.
  - `FrameMarkerMesh` and `PixelSpace`: the marker as a static grid with per-frame indices, drawn from your own render pipeline code.
  - `FrameMarkerTexture`: the marker as a `Texture2D` at module resolution, for UI or anything that shows an image.
- **Samples:** Benchmark (a camera pan measured as one run).

## Ways to draw it, most efficient first

All draw exactly the same pixels; per frame, for the 41×41 main marker:

| #   | Way                                                       | Per frame                                           | Needs                                                        |
| --- | --------------------------------------------------------- | --------------------------------------------------- | ------------------------------------------------------------ |
| 1   | Overlay **Render Mode: Shader Packed Bits** (the default) | The 211 packed bytes (a 211×1 texture) and one quad | Shader model 3.5 (it falls back to Geometry)                 |
| 2   | Overlay **Render Mode: Shader**                           | A 1,681 byte module texture and one quad            | Shader model 3.5 (it falls back to Geometry)                 |
| 3   | `FrameMarkerMesh` in your command buffer                  | About 2,600 indices; the grid's vertices stay       | Your own render pipeline code                                |
| 4   | Overlay **Render Mode: Bitmap** (`FrameMarkerTexture`)    | A 41×41 RGBA texture drawn scaled up                | Nothing                                                      |
| 5   | Overlay **Render Mode: Geometry**                         | About 440 rectangles in GL immediate mode           | Nothing: works on every graphics API, OpenGL ES 2.0 included |

The shaders' module lookup is the reference shaders' (`FrameMarker.hlsl`, from `marker/shaders` in the repository).

## Quick start

1. Add a GameObject with **Frame Marker Overlay** (menu **Add Component → MB → Frame Marker Overlay**).
2. Set **Stored Height** to the height the capture tool stores (for example 540 for `--scale 960x540`).
3. Turn HDR output off while capturing.
4. Record with `mb-framepacing capture --wait-for-start --stop-at-end --analyze`, and call `BeginRun()` / `EndRun()` (or
   `StartCoroutine(overlay.RunFor(10))`) around the part to measure. Each run gets a new UUID as its sequence id; pass your own
   `SequenceId` (a UUID, or a text tag of at most 16 ASCII characters) to match captures to your own records.

The full guide is in [doc/unity.md](https://github.com/Unarmed1000/mb-framepacing/blob/master/doc/unity.md).

## License

BSD 3-Clause (LICENSE.md). The QR encoder is based on the QR Code generator library by Project Nayuki, MIT (Third Party Notices.md).
