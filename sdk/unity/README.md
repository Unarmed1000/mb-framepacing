# MB Frame Pacing for Unity

Draws the [mb-framepacing](https://github.com/Unarmed1000/mb-framepacing) frame marker into every frame of your game: a small QR code
that carries the frame index and the animation time. A capture of the display output, analysed with the mb-framepacing tools, then
shows the **animation error**: how far what the game animated is from what was actually shown on screen.

## Contents

- **`MB.FramePacing`** (`Runtime/Core`) and **`MB.FramePacing.Marker`** (`Runtime/Marker`): the SDK's C# core and marker modules (no
  Unity dependency). They are the same code as the .NET modules.
- **`MB.FramePacing.Marker.Unity`** (`Runtime/Unity`):
  - `FrameMarkerOverlay`: add it to a GameObject and the marker is drawn at the end of every frame, on top of everything.
  - `BeginRun` / `EndRun` / `RunFor`: bracket the part to measure with start and end markers.
  - `FrameMarkerQuad`: the marker as one quad with a dedicated shader, from the packed bits (`Hidden/MB/FrameMarkerQuadPacked`) or a
    texel per module (`Hidden/MB/FrameMarkerQuad`); shader model 3.5.
  - `FrameMarkerMesh` and `PixelSpace`: the marker as a static grid with per-frame indices, drawn from your own render pipeline code.
  - `FrameMarkerTexture`: the marker as a `Texture2D` at module resolution, for UI or anything that shows an image.
- **Samples:** Benchmark (a camera pan measured as one run).

## Ways to draw it, most efficient first

All draw exactly the same pixels; per frame, for the 41×41 main marker:

| #   | Way                                                       | Per frame                                                         | Needs                                        |
| --- | --------------------------------------------------------- | ----------------------------------------------------------------- | -------------------------------------------- |
| 1   | Overlay **Render Mode: Shader Packed Bits** (the default) | The 211 packed bytes (a 211×1 texture) and one quad               | Shader model 3.5 (it falls back to Geometry) |
| 2   | Overlay **Render Mode: Shader**                           | A 1,681 byte module texture and one quad                          | Shader model 3.5 (it falls back to Geometry) |
| 3   | `FrameMarkerMesh` in your command buffer                  | About 2,600 indices; the grid's vertices stay                     | Your own render pipeline code                |
| 4   | Overlay **Render Mode: Bitmap** (`FrameMarkerTexture`)    | A module texture with its quiet zone (49×49 RGBA) drawn scaled up | Nothing                                      |
| 5   | Overlay **Render Mode: Geometry**                         | About 440 rectangles in GL immediate mode                         | Nothing: works on every graphics API         |

The shaders' module lookup is the reference shaders' (`FrameMarker.hlsl`, from `sdk/shaders/hlsl` in the repository).

## Quick start

1. Add a GameObject with **Frame Marker Overlay** (menu **Add Component → MB → Frame Marker Overlay**).
2. Set **Stored Height** to the height the capture tool stores (for example 540 for `--scale 960x540`).
3. Turn HDR output off while capturing.
4. Record with `mb-framepacing capture --wait-for-start --stop-at-end --analyze`, and call `BeginRun()` / `EndRun()` (or
   `StartCoroutine(overlay.RunFor(10))`) around the part to measure. Each run gets a new UUID as its sequence id; pass your own
   `SequenceId` (a UUID, or a text tag of at most 16 ASCII characters) to match captures to your own records.

The full guide is in [doc/unity.md](https://github.com/Unarmed1000/mb-framepacing/blob/master/sdk/doc/unity.md); what to write in each field (for a game with its own frame pacer) is in
[Filling the marker fields](https://github.com/Unarmed1000/mb-framepacing/blob/master/sdk/doc/marker-fields.md).

## License

BSD 3-Clause (LICENSE.md). The QR encoder is based on the QR Code generator library by Project Nayuki, MIT (Third Party Notices.md).
