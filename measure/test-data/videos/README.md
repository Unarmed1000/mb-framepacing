# Test clips

Thirty-three clips (1280×720, lossless H.264 4:4:4, plus 3 refreshes of start and end marker) with the frame marker baked in, one
folder per scenario, each with its `video.mp4` and the generator's `manifest.json`. The display runs at 60 Hz and a clip for 8 s, but
for the stress clips.

**Per rendered frame, the manifest gives:**

- the refresh it was first shown on (`null`: dropped, never shown);
- the animation time and its step (`animationNs` is the animation time as the frame's marker carries it);
- the animation error (PresentMon's definition, the raw value);
- how many refreshes late it was (negative: early, shown out of order);
- the target frame rate and the preferred frame rate (`targetFps`, `preferredFps`; `null` on demand);
- the frame's CPU start time and CPU busy (`cpuStartNs`, `cpuBusyNs`);
- in the idle clips, its static flags (`staticAfter`: nothing animates while it is on screen; `staticBefore`: nothing animated while
  the frame before it was, said one frame later), as its marker carries them.

The `…Ns` values are whole nanoseconds, as the markers carry them, each rounded once from the exact time.

**Every marker carries:**

- the pacer's intended display time: the refresh the frame was rendered for, 0 = the clip's first refresh;
- the target and preferred frame time. `60-busy-adaptive` keeps preferring 60 fps while its busy stretch aims for 30; `30` and the two
  half-rate clips prefer 30; the on-demand clips write `0xFFFFFFFF` for both;
- the flags.

The start marker also carries a sequence id: the mode's name, or a UUID made from it when the name is longer than 16 characters
(`sequenceId`). The clips have no sync marker: they can not tear.

**The fault clips** (`…-dropped-frames`, `…-out-of-order`, `…-dropped-before-wake`, `…-dropped-wake`, `…-dropped-after-stall`) also give, per box:

- `screen`: the frame on screen in every refresh;
- `presented`: the frames the analysis counts. A frame is presented when it first appears with an index above every frame shown
  before it;
- `fault`: its kind and blocks;
- `expected`: the frame indices never presented, the refreshes that showed a frame out of order and, in the idle fault clips,
  `staticSteps`: the frames whose step from the presented frame before them is static by the flags alone.

**The idle clips:**

- `60-naive-5ms-static-rests`: frames at rest are static.
- `60-on-demand`: nothing is rendered at rest.
- `60-on-demand-paused-clock`: as `60-on-demand`, but the animation clock pauses while idle, so only the StaticAfter flag keeps the step
  after a rest from being judged.
- `60-on-demand-paused-clock-hindsight`: the same frames and clock, but the rest is said in hindsight: no flag on the rest's frame,
  StaticBefore on the frame that wakes. It analyses as `60-on-demand-paused-clock` (only the last frame differs: its StaticBefore would
  come with the frame after the capture).
- `60-on-demand-paused-clock-hindsight-dropped-before-wake`: as the hindsight clip, but the first rest's frame is rendered and never
  shown, so the wake frame's StaticBefore speaks for a frame that was never on screen. The analysis assumes the frame shown instead
  held the rest (`StaticAssumed`); without the guess that step is judged (−100 ms).
- `60-on-demand-paused-clock-dropped-before-wake`: the same fault on `60-on-demand-paused-clock`: the rest's frame and its StaticAfter
  are never shown. Assumed static; −100 ms without the guess.
- `60-on-demand-paused-clock-hindsight-dropped-wake`: the frame that wakes is never shown, and its StaticBefore with it: no flag
  reaches the display. Assumed static (the clock stood still over the hold, in a run that uses the flags); −100 ms without the guess.
- `60-on-demand-paused-clock-hindsight-dropped-frames`: frames dropped in the middle of the motion (16 runs of 1 to 4), the clock
  running through each: ordinary dropped frames, nothing is assumed.
- `60-on-demand-paused-clock-hindsight-dropped-after-stall`: the game renders nothing for 6 refreshes mid-motion while its clock runs
  on, and the next frame is dropped: the index and hold pattern of `-dropped-wake`, but a stall, so nothing is assumed.
- `60-static-rests-paused-clock`: every frame rendered, the clock standing still through each rest: the frame that reaches the rest
  pose carries StaticAfter, the frames inside the rest both flags.
- `60-idle-1fps`: one frame per second while idle, preferring 1 s.

**The stress clips** are a fixed script, with no noise and no random numbers, of lost frames: frames dropped, late, out of order, a
failed present, next to a rest and next to a change of pace, and how a pacer recovers (it re-aligns, goes back to its grid, or flips to
a swap interval of 1). Unlike the fault clips, a frame's animation time can lose time here (a dropped frame the clock never counted, a
late frame whose step stays, a hitch with the clock stopped).

- `60-stress`, `30-stress`, `15-stress`: paced at 60, 30 and 15 Hz on a 60 Hz display; `…-edges`: out of order at the run's edges.
- `40-stress`: a whole run paced at 25 ms on 60 Hz, a target the display can not show; `60-stress-uneven`: 60 Hz pacing, with short
  stretches at 25 and 20 ms.
- `50-stress`, `120-stress`, `144-stress`: other displays, at a target of 1 and of 2 refreshes.

Their manifests have no animation error, and add per box:

- `scenarios`: a stretch of the script, with its `name`, its `firstFrame` (an index into the rendered frames, not a marker's frame
  index) and its `frameCount`;
- per rendered frame, `presentFailedBefore`: the marker says the present of the frame before failed.

`fault.kind` is `scripted`. What each scenario must give is not in the clips: `StressClipTests` holds it, in a table written by hand.

`measure/libs/MB.FramePacing.Analysis/UnitTest/source/VideoClipTests.cs` imports every clip through ffmpeg and checks the analysis
against its manifest, frame by frame (skipped without ffmpeg); `ClipManifest` reads the manifests.

**Made by** [mb-framepacing-explained](https://github.com/Unarmed1000/mb-framepacing-explained):
`tools/frame_pacing_video/export_test_clips.py`, with this repository's Python marker module (`sdk/python`,
`mb_framepacing.marker`), which it has as a submodule pinned to a commit. Regenerate them there when the marker format changes, from
the commit that changes it and before that commit is published, so the format and the clips arrive together; copy the folders here
unchanged.

**License:** PolyForm Perimeter License 1.0.1, (c) 2026 Mana Battery ApS, like the rest of the tools' test data (the root
[`LICENSE`](../../../LICENSE)). mb-framepacing-explained publishes its own copies under CC BY-NC-SA 4.0; the copies here are licensed
for this repository. The manifests' `license` field says so.
