# Capture: notes for Claude

Part of the notes for Claude; `CLAUDE.md` holds the rules every task needs.

- **Capture data (the default):** a capture decodes every frame's markers live and stores only them, with the timestamps, in
  `captures.mbcd` (`sdk/doc/capture-data-format.md`); the frames themselves (`frames.mbfc`) only with `--keep-frames` / the GUI's "Store
  video frames".
  - The recorder's inspection thread runs `LiveFrameDecoder` (search until `MarkerLocator` locks, then `FrameMarkerDecoder`); the
    start/end triggers (`SequenceMonitor`) use that decode instead of decoding again.
  - The analysis starts from `captures.mbcd` (`CaptureDecoder.FromData`). A capture with only `frames.mbfc` is decoded with the same
    steps (`CaptureDecoder.DecodeFrames`) and gets `captures.mbcd`; `analyze --redecode` redoes that. `VideoClipTests` checks that
    live and afterwards give identical records, so keep the two paths on the shared decoder.
- **The suggested way to measure today** is OBS recording a capture card and `import` of the recording (`measure/doc/usage.md` section 2):
  the display's refresh rate must be the capture source's FPS and OBS's video FPS (the rate the file is saved at), with no scaling and
  little or no compression. Its OBS settings are proposed, not yet verified with a recording: say so until they are. The README
  opens with the sister repository's page (the slides), then how it works and this workflow.
- **Live capture is EXPERIMENTAL** (`measure/doc/live-capture.md`, agreed with the user): recording a capture card or a network stream
  with the tools themselves. It is out of the workflow documents (README, usage.md, the install guides); live-capture.md has it,
  with the per-platform details and the caution that on some platforms, macOS among them, it is probably too slow to be usable.
  - **GUI:** the capture cards and "Network stream (URL, experimental)..." are offered only with **Settings → Experimental features**
    (`CaptureViewModel.ListSources`, `IsExperimental`). The default source is a video file, and the start button says
    **Analyze recording** for a video file or an image folder, **Start capture** for a live source (`StartText`).
  - **CLI:** `--experimental` (`CommonOptions.Experimental`, recursive) shows and allows the experimental parts: `capture`, `locate`,
    `devices`, `camera-rig` and `frame-stages` (`CommonOptions.ExperimentalCommand`), `--camera`, `--recorded-fps`,
    and `selftest`'s `--camera`, `--refresh`, `--tear-every` (`ExperimentalOption`), and a stream URL
    for `import`. Without it they are left out of `--help`
    (read from the arguments before the commands are made: `ExperimentalRequested`) and refused with a one-line hint. Live capture
    prints `CommonOptions.LiveCaptureNotice` first. Every documented command for them carries `--experimental`.
  - **No synthetic test game in the GUI** (agreed with the user): no source, no `--demo`. `selftest` keeps it (the CLI's check
    without hardware), and the synthetic camera (very experimental) stays behind the experimental switch.
- **Playback reports** (`MB.FramePacing.Charts/source/Playback`, `measure/doc/usage.md` "The playback page"; agreed with the user):
  - **100 % opt-in:** `--playback` on `import`/`analyze`/`render`, the GUI's **Save playback page**; never by `--charts` or Save charts.
  - **One HTML page that runs from the disk** (`PlaybackPage.html`, an embedded resource: markup, CSS, vanilla JS; no network): the
    report card inline (its title and tiles in the page's header), the data inline (`PlaybackData`: the card's `CardPlot`s, the
    section's frames as columns of whole nanoseconds; `PlaybackData.FormatVersion` 4: `periodNs`, `originNs`, `firstNs`, `lastNs`, and `videoFrames`, what every video frame's QR says, from captures.csv: the readout's frame index is always that, never the frame the analysis holds on screen), a
    player bar, a playhead on every panel. The frame columns are written small, in whole
    numbers the page's `readFrames` turns back exactly (the file's header comment has the form: steps from the frame before, the
    time as what is left after its captures' whole periods, runs of equal values as value and count): about 5 bytes a frame on a
    test clip, and exact in the page's JavaScript for runs under 104 days (2^53 ns). `PlaybackFrameColumns` (the unit tests) reads them the same way: change the three together. Video time = the capture's time: an import keeps the
    file's own pts (`-copyts`), so only video-file imports with the Device time source qualify (`PlaybackCapture.Problem`: no camera,
    no `--recorded-fps`, not images). capture.json `inputPath` names the recording (`--video` for older imports).
  - **Zoom steps** (`PlaybackZoom`: whole, 60, 10, 2 s per screen): a zoomed card is `ReportCard.Build(..., visible:)` with its
    `ScrollShape` layers kept (`SvgCardWriter.Write(..., scrollLayers:)`, clipped groups the page translates, as the GUI's Timeline);
    the steps that fit a 64 MB estimate (300 bytes per frame or pixel column drawn, measured), each kept as inert text until shown.
  - **Speed and memory:** the cards build at once (`Parallel.For`, each also builds its panels at once), then their SVG; the page is
    streamed to its file (`PlaybackPage.Write`: the template's pieces, each SVG, the data straight from its `Utf8JsonWriter`), never held as
    one string. `PlaybackBenchmarks` (`measure/tools/Benchmarks`, `--long-running`): a whole page of 10 min or an hour at 60 or 240 Hz takes about 0.2 s and
    allocates about 100 MB; the video's copy is ffmpeg's time. What keeps it small, shared with every card: numbers are written in place
    (`SvgNumber` is `ISpanFormattable`, `SvgMarkup.TryFormatFixed`: interpolate `N(...)`, not `Fixed(...)`, into paths), `SvgCardWriter`
    writes into one `StringBuilder`, and the per-column searches (`PixelColumns.Walk`, `RunEvents`) make no closure.
  - **A folder per report** (`analysis/playback/<prefix>[-<from>s-<to>s]/`: `index.html`, its own video, `playback.json`). An export
    touches only the folders it writes; saving the same report again replaces that folder only. Never share or delete another
    report's files. **Nothing in it names anything outside it** (agreed with the user): no link to the recording, no local path
    (`playback.json` keeps the recording's file name, size and time; the page's command says `<capture folder>`).
  - **The video is always a copy** (`PlaybackExport`, shared by CLI and GUI, which differ only in `decide` and progress): a playable
    recording is copied without a question; one browsers cannot play (`VideoCodecInfo`) gets a playable copy (`PlayableCopy`: remux,
    else H.264, every frame and pts kept) only after a yes, else the report has no video. Answer in advance: `--playback-transcode`,
    else the configuration's `playbackTranscode`, else ask; redirected stdin makes no copy. The GUI's dialog can remember the answer.
  - **The one exception: a video the user names** (`--playback-video-url`, CLI only; asked for by mb-framepacing-explained, whose
    slides serve their own web encode next to the report): written into the page verbatim (`PlaybackVideoKind.External`), nothing
    copied, probed or asked, the recording not needed; only a local file that is there and shorter than the run is a warning
    (`PlaybackResult.Warnings`), never an error.
- **Media sources**
  - Sources other than capture cards (`mb-framepacing import`, and the GUI's "Video file... / Image folder... / Network stream") go through
    `MediaInput` -> ffmpeg.
  - Image sequences get their exact times from `--fps` or the timestamp CSV (`FrameTimestamps`), not from ffmpeg: its concat
    timestamps are 40 ms coarse. The CSV (`ImageSequence.ReadTimestamps`) needs its header line, `fileName,timeNs` found by name,
    and holds whole nanoseconds: a headerless file, or one with `timeTicks` or `timeMs`, is refused, since ticks or whole milliseconds
    would read as nanoseconds.
  - Non-live sources make the recorder wait instead of dropping frames (`IsLive`).
- **Only the markers are read** (an import's default; `--roi auto`, `locate`, the GUI's "Locate marker" for live capture, which is
  experimental; agreed with the user):
  - **The rule is `RegionRule`** (Capture, shared by the CLI and the GUI): a recording (a video file or an image folder:
    `InputPath` set) with no `--roi`, no `--scale` and no `--keep-frames` stores only its markers' regions; `--roi full` (the GUI's
    region box: `full`) is the whole frame; `auto` asks for the regions anywhere; a rectangle is one rectangle, and two joined by `+`
    (`RegionRule.ParseRegions`, agreed with the user) are the main marker's region and the sync marker's, stacked, with `--scale` the
    size of the two stacked (a whole-number downscale of both, `StackedDownscale`). That is how a located pair is written down:
    `MarkerLocateResult.RegionText` and `Scale` (what `locate` prints and the GUI's "Locate marker" fills in) give back exactly
    `Apply`, the same ffmpeg command line (`RegionRuleTests`). Live sources keep the
    whole frame unless asked. It goes in `CaptureCommand.ApplyRegion` / `CaptureViewModel.ApplyRegion`, never in
    `MediaInput.ToCaptureOptions` (the camera-rig tools and the ffmpeg tests call that for whole frames).
  - `FfmpegMarkerLocator` runs ffmpeg uncropped into `MarkerProbe` (nothing is recorded), which returns the main marker's lock and
    the sync marker's when the frames show one (`MarkerProbeResult`). A live source gets 10 s; a recording is read until its first
    marker, to its end (it may start before the application): none at all is `MarkerNotFoundException` and the import stops. A marker
    that moved while located or does not fit the frame is `MarkerRegionException`: the default falls back to the whole frame and
    says so, an explicit `auto` fails.
  - `MarkerCrop` picks a region per marker and one integer downscale; each crop starts a whole number of downscale steps before its
    marker's origin (otherwise module edges fall between stored pixels and dense start markers stop decoding). Regions that would
    overlap become one. Two regions are stored as one frame, the main marker's on top (`FfmpegCommandBuilder.BuildStackedFilter`:
    split, crop, scale, pad the narrower with white, vstack; `FfmpegCaptureOptions.SyncRoi` / `RoiDownscale`), so the tearing check
    stays. `capture.json` has `roi` and `syncRoi`, the `captures.mbcd` header the second region at offset 168 (`SyncRegion`, all
    three languages), and `frames.mbfc`'s header at offset 64 (`CaptureFileHeader.SyncRoi`), so frames decoded again give the same
    capture data.
  - **`frames.mbfc`'s header says how long it is** (128 bytes as written; the records start after it) and its first 64 bytes are what
    every file has: a field after them is there when the header is long enough, and "none" otherwise (`CaptureFileHeader.Read`,
    `MinHeaderSize`). Add a field in the reserved bytes the same way: optional, never a reason to refuse a file.
  - The analysis needs no change: locks are in stored pixels. It warns when a region capture has many undecodable captures, counted
    from the first capture with a marker to the end, or to the end marker when that is the last marker seen (footage before and
    after the run is not a marker that moved).
  - The golden data stays a whole-frame capture: `update_test_data.py` imports with `--roi full`.
  - Measured (60 fps H.264, both markers): 720p 2000 fps against 1400 for whole frames, 1080p 1600 against 700, 2160p 540 against
    200, where it is as fast as ffmpeg decodes the file.
