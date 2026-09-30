# Test clips

Eighteen 60 Hz clips (1280×720, lossless H.264 4:4:4, 8 s plus 3 refreshes of start and end marker) with the frame marker baked in,
one folder per scenario, each with its `video.mp4` and the generator's `manifest.json`.

**Per rendered frame, the manifest gives:**

- the refresh it was first shown on (`null`: dropped, never shown);
- the animation time and its step;
- the animation error (PresentMon's definition, the raw value);
- how many refreshes late it was (negative: early, shown out of order);
- the target frame rate and the preferred frame rate (`targetFps`, `preferredFps`; `null` on demand);
- the frame's CPU start time and CPU busy (`cpuStartTicks`, `cpuBusyTicks`);
- in the idle clips, its static flags (`staticAfter`: nothing animates while it is on screen; `staticBefore`: nothing animated while
  the frame before it was, said one frame later), as its marker carries them.

**Every marker carries:**

- the pacer's intended display time: the refresh the frame was rendered for, 0 = the clip's first refresh;
- the target and preferred frame time. `60-busy-swappy` keeps preferring 60 fps while its busy stretch aims for 30; `30` and the two
  half-rate clips prefer 30; the on-demand clips write `0xFFFFFFFF` for both;
- the flags.

The start marker also carries a sequence id: the mode's name, or a UUID made from it when the name is longer than 16 characters
(`sequenceId`). The clips have no sync marker: they can not tear.

**The fault clips** (`…-dropped-frames`, `…-out-of-order`, `…-dropped-before-wake`) also give, per box:

- `screen`: the frame on screen in every refresh;
- `presented`: the frames the analysis counts. A frame is presented when it first appears with an index above every frame shown
  before it;
- `fault`: its kind and blocks;
- `expected`: the frame indices never presented and the refreshes that showed a frame out of order.

**The idle clips:**

- `60-naive-5ms-static-rests`: frames at rest are static.
- `60-on-demand`: nothing is rendered at rest.
- `60-on-demand-paused-clock`: as `60-on-demand`, but the animation clock pauses while idle, so only the StaticAfter flag keeps the step
  after a rest from being judged.
- `60-on-demand-paused-clock-hindsight`: the same frames and clock, but the rest is said in hindsight: no flag on the rest's frame,
  StaticBefore on the frame that wakes. It analyses as `60-on-demand-paused-clock` (only the last frame differs: its StaticBefore would
  come with the frame after the capture).
- `60-on-demand-paused-clock-hindsight-dropped-before-wake`: as the hindsight clip, but the first rest's frame is rendered and never
  shown, so the wake frame's StaticBefore marks nothing and that step is judged (−100 ms).
- `60-static-rests-paused-clock`: every frame rendered, the clock standing still through each rest: the frame that reaches the rest
  pose carries StaticAfter, the frames inside the rest both flags.
- `60-idle-1fps`: one frame per second while idle, preferring 1 s.

`measure/libs/MB.FramePacing.Analysis/UnitTest/source/VideoClipTests.cs` imports every clip through ffmpeg and checks the analysis
against its manifest, frame by frame (skipped without ffmpeg); `ClipManifest` reads the manifests.

**Made by** [mb-framepacing-explained](https://github.com/Unarmed1000/mb-framepacing-explained):
`tools/frame_pacing_video/export_test_clips.py --output-dir <this folder>`, with its copy of the Python marker library
(`sdk/marker/python`). Regenerate them there when the marker format changes, and copy the folders here unchanged.

**License:** PolyForm Perimeter License 1.0.1, (c) 2026 Mana Battery ApS, like the rest of the tools' test data (the root
[`LICENSE`](../../../LICENSE)). mb-framepacing-explained publishes its own copies under CC BY-NC-SA 4.0; the copies here are licensed
for this repository. The manifests' `license` field says so.
