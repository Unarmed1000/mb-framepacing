# MB Frame Marker for Unity

Draws the [mb-framepacing](https://github.com/Unarmed1000/mb-framepacing) frame marker into every frame of your game: a small QR code
that carries the frame index and the animation time. A capture of the display output, analysed with the mb-framepacing tools, then
shows the **animation error**: how far what the game animated is from what was actually shown on screen.

## Contents

- **`MB.FrameMarker`** (`Runtime/Core`): the general C# marker library (no Unity dependency). It is the same code as the .NET library.
- **`MB.FrameMarker.Unity`** (`Runtime/Unity`):
  - `FrameMarkerOverlay`: add it to a GameObject and the marker is drawn at the end of every frame, on top of everything.
  - `BeginRun` / `EndRun` / `RunFor`: bracket the part to measure with start and end markers.
  - `FrameMarkerMesh` and `PixelSpace`: draw the marker from your own render pipeline code.
  - `FrameMarkerTexture`: the marker as a `Texture2D` at module resolution, for UI or anything that shows an image.
  - `FrameMarkerQuad`: the marker as one quad with a dedicated shader (`Hidden/MB/FrameMarkerQuad`, shader model 3.5).
  - The overlay's **Render Mode** picks one of them: Geometry (the default), Bitmap or Shader; all draw the same pixels.
- **Samples:** Benchmark (a camera pan measured as one run).

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
