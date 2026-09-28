# Test clips

Nine 60 Hz clips (1280×720, lossless H.264 4:4:4, 8 s plus 3 refreshes of start and end marker) with the frame marker baked in,
one folder per scenario, each with its `video.mp4` and the generator's `manifest.json`: per presented frame the refresh it was
flipped on, the animation time step, the animation error (PresentMon's definition), how many refreshes late it was and the target
frame rate. Every marker carries the pacer's intended display time (the refresh the frame was rendered for, 0 = the clip's first
refresh) and target frame time; only `60-busy-swappy` changes its target, through its busy stretch. Every marker also carries the
frame's CPU start time and CPU busy (the manifest's `cpuStartTicks`, `cpuBusyTicks`), and the start marker a
sequence id: the mode's name, or a UUID made from it when the name is longer than 16 characters (`sequenceId`). The clips have no
sync marker: they can not tear.

`measure/libs/MB.FramePacing.Analysis/UnitTest/source/VideoClipTests.cs` imports every clip through ffmpeg and checks the analysis
against its manifest, frame by frame (skipped without ffmpeg).

**Made by** [mb-framepacing-explained](https://github.com/Unarmed1000/mb-framepacing-explained):
`tools/frame_pacing_video/generate_videos.py --single <mode> --marker --speed fast`, with its Python marker library in
`marker/python`. Regenerate them there when the marker format changes, and copy the folders here unchanged.

**License:** PolyForm Perimeter License 1.0.1, (c) 2026 Mana Battery ApS, like the rest of the tools' test data (the root
[`LICENSE`](../../LICENSE)). mb-framepacing-explained publishes its own copies under CC BY-NC-SA 4.0; the copies here are licensed
for this repository. The manifests' `license` field says so.
