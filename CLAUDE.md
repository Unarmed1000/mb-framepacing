# mb-framepacing: notes for Claude

Frame pacing / animation error measurement.

- **`marker/`** holds what goes **into** the application: the marker libraries, which draw a QR marker into every frame.
  They are the C++20 library (`marker/cpp/`), the general C# library (`marker/csharp/`), the Unity package (`marker/unity/`) and
  the Python library (`marker/python/`).
- **`measure/`** holds the .NET tools that **measure**: they record a capture card through ffmpeg and analyse the markers.

See `README.md` for the overview and `doc/marker-format.md` for the marker specification. **The document is the reference**: C++
(`marker/cpp/src/Payload.cpp`) and C# (`marker/csharp/source/Marker.cs`) must match it byte for byte. The tools'
`MarkerPayload` delegates to the C# library; ZXing is only used for decoding.

## Layout

| Path                                              | Contents                                                                                                        |
| ------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| `marker/VERSION`                                  | Version of the marker libraries (released with `marker-v*` tags)                                                |
| `marker/cpp/`                                     | C++20 library, `marker-render` tool, GoogleTest tests, CMake presets                                            |
| `marker/csharp/`                                  | General C# library `MB.FrameMarker` (.NET Standard 2.1, C# 9, no dependencies) + NUnit tests                    |
| `marker/unity/`                                   | Unity package sources (helpers, samples), `build_upm.py`, `check_in_unity.py`                                   |
| `marker/python/`                                  | Python library `mb_framemarker` (standard library only, Python 3.11) + unittest tests                           |
| `marker/shaders/`                                 | Reference shaders that draw the marker as one quad: HLSL, GLSL for OpenGL 3.3/ES 3.0, OpenGL ES 2.0 and Vulkan  |
| `data/VERSION`                                    | Version of the data libraries (released with `data-v*` tags)                                                    |
| `data/python/`                                    | Python data library `mb_framepacing_data` (reads; standard library, Python 3.11) + unittest tests               |
| `data/cpp/`                                       | C++20 data library `mb_framepacingdata` (reads; nlohmann/json via FetchContent, inside only) + GoogleTest tests |
| `data/csharp/`                                    | C# data library `MB.FramePacing.Data` (.NET 10): reads and writes captures.mbcd and the analysis output         |
| `measure/VERSION`                                 | Version of the tools (released with `tools-v*` tags)                                                            |
| `measure/app/`, `measure/libs/`, `measure/tools/` | CLI, Avalonia GUI, Marker/Capture/Analysis/Charts libraries (+ `UnitTest/`), DocImages, Benchmarks              |
| root `Directory.*.props`, `UnitTest.props`        | Shared .NET build settings (C# projects only; see below), central package versions                              |
| `mb-framepacing.slnx`                             | IDE solution with every .NET project                                                                            |
| `doc/`, `test-data/markers/`, `licenses/`         | Docs and images, golden marker images from the C++ library, third-party licenses                                |
| `test-data/data/`                                 | The data libraries' golden data: a test clip imported and analysed, and `digest.json`                           |
| `test-data/videos/`                               | 60 Hz test clips with manifests from mb-framepacing-explained, `VideoClipTests`                                 |

## Build and test

```
mb-quality -r --all .                            # the standard check: dotnet format + CSharpier + build + tests + vulnerable packages
mb-quality -r --repair .                         # apply formatting, then build and test
dotnet build mb-framepacing.slnx                 # warnings are errors (Directory.Build.props)
dotnet test  mb-framepacing.slnx
cd marker/cpp && cmake --preset windows && cmake --build --preset windows && ctest --preset windows   # linux / linux-clang / macos too
cd data/cpp && cmake --preset windows && cmake --build --preset windows && ctest --preset windows     # the C++ data library, the same way
dotnet run --project measure/app/FramePacing/FramePacing.csproj -- selftest --fps 500  # end to end without hardware
python -m unittest discover -s data/python -t data/python    # the Python data library against test-data/data
```

- **mb-quality**
  - It is on the PATH; its config is the root `.mb-quality.json`, and the CSharpier tool manifest is `.config/dotnet-tools.json`.
  - It only checks folders that contain a solution and takes the `*.csproj` files next to it. So **every project folder has its own
    one-line `.slnx`**; keep them when adding a project. The root `mb-framepacing.slnx` is the IDE solution.
  - A run that reports "0 projects" means a project folder is missing its `.slnx`.
- **The root `Directory.Build.props` applies to C# projects only.** Its `PropertyGroup` is conditioned on `.csproj` because MSBuild
  also imports it into the Visual Studio C++ projects CMake generates. Without the condition, the `Platform`/`PlatformTarget` pin
  breaks CMake's compiler detection.
  - `measure/Directory.Build.props` imports it and adds `net10.0` plus `measure/VERSION`.
- **Every .NET project is pinned to AnyCPU**, because some machines set a `Platform=x64` environment variable. Keep that pin:
  without it, single-project builds go to `bin/x64` and cause MSB3270 warnings.
- **C++ formatting and linting**
  - C++ follows `marker/cpp/.clang-format` and `marker/cpp/.clang-tidy` (namespaces are CamelCase: `MB::FrameMarker`); `data/cpp`
    has copies of both.
  - `python tools/check_cpp.py` runs both on our sources only (never `third_party/` or fetched dependencies) of both C++ libraries
    (`--library marker|data` for one), with the versions CI pins in `requirements-dev.txt`. clang-tidy needs a configured build of
    each: `<library>/build/<preset>`, default `windows` (the VS generator writes no compile database, so the script passes the
    include paths); `--preset` takes another one. To apply formatting: `clang-format -i` on the files the script lists.
  - Clang's `-Wconversion` includes `-Wsign-conversion` (GCC's and MSVC's do not), so macOS CI can fail where Windows and Linux
    pass: shift and combine small unsigned types after casting them to `uint32_t`.
  - The `linux-sanitize` preset (Clang, AddressSanitizer + UndefinedBehaviorSanitizer, compile database) is what CI runs the tests
    and clang-tidy with.
  - `cmake/Version.hpp.in` is guarded with `// clang-format off`, because formatting breaks its `@VAR@` placeholders.
- **Python scripts** (`measure/build_standalone.py`, `tools/`, later `marker/unity/build_upm.py`): standard library only. They must pass
  `ruff check .`, `ruff format --check .` and `basedpyright` (config: `ruff.toml`, `pyrightconfig.json`, recommended mode; tools
  pinned in `requirements-dev.txt`, installed into the project's `.venv`: `python -m venv .venv`, then
  `.venv\Scripts\python -m pip install -r requirements-dev.txt`; activate it or call `.venv\Scripts\<tool>`). CI runs all three.
  `tools/check_cpp.py` uses the clang tools next to the Python that runs it, so `.venv\Scripts\python tools/check_cpp.py` gets the
  pinned versions.
- **Unity package** (`com.manabattery.framemarker`):
  - It isn't stored as one folder: `marker/unity/build_upm.py` assembles it from `marker/csharp/source` (core) plus
    `marker/unity/Runtime/Unity` (helpers, all wrapped in `#if UNITY_2021_3_OR_NEWER`), and generates `.meta` files with stable GUIDs.
  - CI runs it with `--check`.
  - `marker/unity/check_in_unity.py` verifies it in a real Unity editor in batch mode (every drawing method pixel exact; `--graphics
glcore|vulkan|d3d12` forces another graphics API); it takes the newest editor Unity Hub installed, or `--unity`. Run it after changing
    the core or the helpers.
  - The core must stay C# 9 / .NET Standard 2.1 without UnityEngine (Unity 2021.2+ has .NET Standard 2.1 in both API levels). Buffer
    APIs take `ReadOnlySpan<T>` for input and `Span<T>` for output, as the C++ library takes `std::span`.
- **Standalone release archive:** `marker/cpp/CMakeLists.txt` finds `VERSION`, `LICENSE` and `licenses/` next to itself in a release
  archive, and falls back to `marker/VERSION`, `marker/LICENSE` and the repository root's `licenses/` otherwise.
- **Docs**
  - Formatting: `npm install && npm run format` (Prettier: Markdown/JSON/YAML; config `.prettierrc.json`, ignores in
    `.prettierignore`).
  - Regenerate the README images with `dotnet run --project measure/tools/DocImages`. The SVG report examples (`report-example-*.svg`) come from test clips
    imported through ffmpeg (skipped without it): example pictures use the test clips, not the synthetic game. It renders the real GUI **offscreen**
    (Avalonia.Headless) in no-save mode and neutralises machine specific text. Never take desktop screenshots.
- **Data libraries (`data/`, BSD 3-Clause):** `MB.FramePacing.Data` reads and writes `captures.mbcd` (`doc/capture-data-format.md`) and
  the analysis output (`doc/analysis-output-format.md`: `summary.json` with `formatVersion`, which covers the CSVs, and the CSVs). The
  tools write and read every file through it; their own types map to it (`CaptureDataMapping` in Capture, `AnalysisDataMapping` in
  Analysis). Keep the output byte for byte: the golden data (`test-data/data`, `digest.json`) is written back exactly, and every
  language's reader must read the digest's values. After a format change: `python tools/update_test_data.py` (needs ffmpeg).
- **Capture data (the default):** a capture decodes every frame's markers live and stores only them, with the timestamps, in
  `captures.mbcd` (`doc/capture-data-format.md`); the frames themselves (`frames.mbfc`) only with `--keep-frames` / the GUI's "Store
  video frames".
  - The recorder's inspection thread runs `LiveFrameDecoder` (search until `MarkerLocator` locks, then `FrameMarkerDecoder`); the
    start/end triggers (`SequenceMonitor`) use that decode instead of decoding again.
  - The analysis starts from `captures.mbcd` (`CaptureDecoder.FromData`). A capture with only `frames.mbfc` is decoded with the same
    steps (`CaptureDecoder.DecodeFrames`) and gets `captures.mbcd`; `analyze --redecode` redoes that. `VideoClipTests` checks that
    live and afterwards give identical records, so keep the two paths on the shared decoder.
- **Media sources**
  - Sources other than capture cards (`mb-framepacing import`, and the GUI's "Video file / Image folder / Network stream") go through
    `MediaInput` -> ffmpeg.
  - Image sequences get their exact times from `--fps` or the timestamp CSV (`FrameTimestamps`), not from ffmpeg: its concat
    timestamps are 40 ms coarse.
  - Non-live sources make the recorder wait instead of dropping frames (`IsLive`).
- **Fast capture** (`--roi auto`, `locate`, the GUI's "Locate marker"):
  - `FfmpegMarkerLocator` runs ffmpeg uncropped into `MarkerProbe` (nothing is recorded). `MarkerCrop` then picks the region and
    the integer downscale, and the capture starts a new ffmpeg with `crop=...:exact=1,scale=...`.
  - The crop starts a whole number of downscale steps before the marker origin; otherwise module edges fall between stored pixels
    and dense start markers stop decoding.
  - The analysis needs no change: locks are in stored pixels. It warns when a region capture has many undecodable captures.
- **Refresh rate and pacing:**
  - A capture card captures at the display's native refresh rate: that is a fundamental assumption, not a setting. The analysis
    takes the capture period as the refresh period; display time steps are whole refreshes. `SyntheticCaptureSource` refuses a capture rate that differs
    from the refresh; `selftest --fps N` simulates an N Hz display.
  - **Charts** live in `MB.FramePacing.Charts` (no GUI): the report cards as shapes (below). The GUI draws them itself with `CardView`
    (`FramePacing.Gui`, the style from `CardStyle`, which parses the SVG's style sheet; hover texts from `CardHover`); the Timeline card
    zooms (wheel), pans (drag) and resets (double-click), the distribution tabs follow its section, and **Save view** writes the card
    on screen as SVG or PNG (`CardImage`). DocImages checks the width, zoom, the sliding window (sideways scroll, scrollbar and drag
    move the same card; near its edge the next one is built), reset and hover with headless input.
    - **The sliding window:** zoomed, the GUI's Timeline is built for the view plus a screen either side (`TimelineWindow`;
      `ReportCard.Build(..., visible)`): what moves with time goes into `ScrollShape` layers that `CardView` clips to the plots and moves
      by `ScrollOffset`, so scrolling builds nothing and only the distribution cards follow (`SectionCards.Follow`). A window's start is
      snapped to a whole pixel column, so windows at one zoom draw the same columns and swap without a visible change. Files never
      contain a window (`SvgCardWriter` writes the layers' shapes in place), and **Save view** builds the card of exactly the view.
    - **Long runs cost per pixel column, not per frame:** `RunChartData` (cached per `ChartRun`, immutable, built on first use) holds
      each kind of value as a `FrameSequence` (`RankBits`: which frames have one; a `WaveletMatrix`: exact k-th smallest and counts below a
      bound for any range in about 20 steps) and the late share (`LateShareData`). A `RunSection` is a frame range found by binary search;
      its statistics are computed only when a tile needs them. The panels walk the pixel columns (`PixelColumns.Walk`) and query min, max,
      median, percentiles and histograms (`SectionHistograms`) exactly: the output is byte-identical to sorting. The GUI builds cards
      at the window's width (`width`), with the whole run's scales (`wholeRunScales`), in the background (`SectionCards`,
      `LatestRequest`: a newer zoom cancels an older build). `CardBenchmarks` measures 1 and 10 hours at 240 Hz: a few ms per zoom or
      window. `--charts` and "Save
      charts" write the SVG cards (`ChartFiles`: the report and every `DistributionCard`). Test chart changes with
      `ChartVideoClipTests`: every series of every chart is compared exactly with the test clips' manifests (`ClipManifest`, shared
      with `VideoClipTests` by linked source files).
    - The report card (`ReportCard`, `mb-framepacing render`) is a port of mb-framepacing-explained's `generate_charts.py`: its style sheet
      and `text()`/`ms()` helpers are verbatim in `SvgMarkup` (Python's half-to-even rounding included; `ReportSvgTests` pins their
      output). It draws from the analysis output (`AnalysisOutput` reads `summary.json` and the frames CSV back to the tick), any section
      (`RunSection`); more frames than pixels draw per column. PNG goes through a headless Edge/Chrome (`HeadlessBrowser`, `MB_BROWSER`).
    - `ReportOptions` picks the items (`ReportItem` ids: `--hide`/`--only`); overlays (`ReportItem.Overlays`, now the animation time
      step on the display time step panel) are opt-in (`Show`, `--show`) and need their panel. The tiles frames dropped and out of order
      (`ReportItem.AutoTiles`) show only when the run or section has either (`RunHeadline.Shown`, which the GUI uses too), unless shown
      on purpose (`Show`); the tiles go in two rows, half of them each (`TilesPerRowFor`: 8 → 4, 10 → 5). The layout options are for a card in a
      document (explained's charts page): `Title`, `StripSeconds` (the strip over the first seconds, its own axis), `HideEmpty` (tiles
      without a value, `HeadlineTile.HasValue`, and the late share without late frames), `TilesPerRow`. The defaults are the standard
      card: `--charts`, Save charts, the GUI and DocImages never set them.
    - Cards are shapes (`CardDrawing`: `RectShape`, `LineShape`, `PathShape`, `TextShape`, `GroupShape`, plus `CardPlot` per plot
      area with its data range, for the GUI's zoom and hover and for tests that read values back); `SvgCardWriter` writes them.
      `DistributionCard` builds the error and display time step histograms, the error percentiles and the drift (`render --cards`).
    - The frame timeline (`FrameTimelineCard`, `render --timeline`, at most 40 frames) is explained's timing diagram from the data: CPU
      boxes (CPU start time + CPU busy, overlapping ones in further lanes) placed on the capture's clock by `PacerToCapture` (display time
      - intended display time - lateness; without a schedule, no frame presented after it appears), present arrows, display cells.
    - The headline numbers come from `RunHeadline` only: the GUI's tiles and the report card's show the same tiles, so add or change
      a number there, not in the GUI. A tile without a number shows "-" with `HasValue` false.
    - The animation error panel draws a refresh line (`error-refresh`, dashed amber, labelled "+1 refresh (16.7 ms)") at every whole
      refresh an error reaches within a tenth of a refresh, over the frames the scale covers, and only inside the scale
      (`ChartScale.ErrorRefreshTicks`, at most four per side).
    - The error and display time step charts cover every value unless a few are more than 8 times beyond the 99th percentile (a hitch);
      those get a mark with their value at the edge (`ChartScale` decides the scales, `ReportCard.ClipMarks` draws the marks).
    - **Keys:** every panel's title says what it shows; its key (`ReportCard.Key`, right aligned) names each colour with one swatch per
      entry and lists only what the section shows. A key, a tile's value with its detail and the display box's rate line are one
      `TextRunsShape` (pieces of text, each in its own classes) that the renderer lays out end to end (a `<text>` with `<tspan>`s in SVG,
      measured pieces in `CardView`), so no width is guessed. Swatches are coloured characters in the `key-*` classes (■ fill, ━ line,
      ┅ refresh line, ▾ mark, ▐▌ for colours that alternate per frame).
    - **Holds and strip cells by kind** (`RunChartData.HoldKinds`, `HoldKind`): unknown (a capture gap, dashed grey), late (red), an
      older frame came back (out of order, pink), frames dropped by the target (orange), as planned (green). Dropped = skipped frame
      indices over a gap-free capture that never came back out of order (`DroppedBeforeFrame`); the strip colours the refreshes where
      they were due orange and the out-of-order refreshes (`PresentedFrame.OlderFrames`, CSV `olderFrames`) pink.
    - **Static stretches:** a violet band (`static-band`) behind every time panel from the first static frame's display time to the next
      frame's (`RunChartData.StaticStretches`), violet strip cells (`strip-static-a`/`-b`). The display time step and frametime scales
      leave static frames' holds and frametimes out (`RunChartData.AnimatingHolds`, `AnimatingFrameTimes`); they get edge marks.
  - **The report is the same for every capture source** (it only pairs decoded markers with display times): histograms in fixed
    0.1 ms bins (`Histogram.DefaultBinWidthTicks`), one error threshold (1 ms, `analyze --error-threshold-ms`,
    `TimelineOptions.ErrorThresholdTicks`), and a display time step is off its target from half a refresh on. A source's precision (a
    camera's period) goes into warnings, never into the binning or the thresholds.
  - Camera captures film faster and calculate the refresh from the frames (`Capture/source/Camera/RefreshEstimator.cs`, also used
    by the calibration). The user's expected display rate (`--display-hz`, capture.json `expectedRefreshHz`) settles an ambiguous
    estimate and is compared with the calculated rate (a capture card: with its capture rate); more than 1 %
    (`TimelineAnalyzer.RefreshTolerance`) is a warning.
  - **Capture gaps** (a capture card's captures not decoded, not recorded, dropped by the source (`CaptureRow.SourceDrops`, a count), or
    missed without a word: `MissedCaptures`, a step of 1.5 periods or more on the device clock, never the host clock): the next
    frame's first sighting is uncertain (`UncertainStart`), so the steps into and out of it are not judged (`UncertainStep`: no animation
    error, no late verdict, not in the frame rates; `RunStatistics.UncertainSteps`), and frame indices skipped across the gap are never
    called dropped. A camera decides uncertain starts itself. Out-of-order captures are kept with the newest frame
    (`PresentedFrame.OlderFrames`).
  - Late frames, the 2 s late share and the "which cause" verdict: `PacingAnalyzer` → `RunPacing` (`runs[].pacing` in
    summary.json). Every frame's target comes from, in order: the pacer's intended display times in the markers (`PacingSource.Schedule`:
    lateness against the schedule, and the animation error split into pacing and prediction error), its target frame time in the
    markers, the target frame rate (`--target-fps`, GUI "Target frame rate", stored in capture.json, overridable at analysis time),
    and otherwise one refresh (the native rate). Without a schedule, a frame after frames the target dropped (`DroppedFrames`) is due one
    target per frame later (1 + dropped): dropped, not late. The synthetic game writes a schedule; its prediction error is 0 by construction.
- **Markers (format version 1, `doc/marker-format.md`):** a 53 byte header shared by frame, start and end, its fields grouped by
  category: format (magic, version, kind), which run and frame (run id, frame index), what the frame shows (flags, animation time),
  pacing (preferred frame time, target frame time, intended display time), the CPU's work (CPU start time and CPU busy, named as
  PresentMon's CPUStartTime and MsCPUBusy). Start/end carry the values of the frame that shows them, and the tearing check compares
  their run id and frame index with the sync marker's. Every main marker (frame, start, end) is QR version 6 (41×41), so it never changes size and has room for future fields.
  - **Pacing terms:** the intended display time is the pacer's aim; the animation time is the predicted display time the game
    animated for (`doc/vocabulary.md`). The target frame time is what the pacer aims for now, the **preferred frame time** what the
    application wants (it differs only while the pacer runs slower); `0xFFFFFFFF` in both = on demand. Never call the preferred frame
    time "desired": that is Vulkan's and Swappy's word for a present time point.
  - **Flags:** bit 0 `Static` (nothing animates: the steps to and from it get no animation error, and the drift sums only judged
    errors); bits 1 to 7 are reserved (write 0, decoders keep them).
  - **Frame rates describe the frames that animate:** a static frame's time on screen (the next frame's display time step, flag
    `StaticBefore`) is left out of average fps, the lows and the display time step statistics and histogram
    (`RunStatistics.CountsTowardFrameRate`, `RunChartData.FrameRateSteps`), and counted (`excludedStaticFrames`); the tile, the report's
    description and the statistics tables say "excluding N static frames" (`RunHeadline.ExcludedStatic`).
  - **Late share:** amber = on screen at least half a refresh longer than the preferred frame time (the marker's, else
    `--target-fps`, else one refresh) without being late; never for static or on-demand frames (`LateShareData`, `PresentedFrame.PreferredTicks`).
  - The start marker (77 bytes) carries a 16 byte opaque sequence id (`SequenceId`: a UUID or a text tag of at
    most 16 ASCII characters, shown as text or UUID hex), not a name. Format version 1 is the baseline for all data (markers,
    captures.mbcd, analysis output): change it in place, no version bump, until there are users. The sync marker (kind 3, 16 bytes: the header's start, run id and frame index; matched to its main marker by both) is QR version 2 (25×25), drawn
    bottom-left: it checks tearing (capture cards, optional) and times the frames for a camera (required). `RecommendedOrigin(kind, …)`
    places both; there are no other slots.
- **Camera capture (VERY EXPERIMENTAL, `doc/camera.md`):**
  - The GUI hides it (the camera card, the synthetic camera source) unless **Settings → Experimental features** is on
    (`GuiSettings.ExperimentalFeatures`, `CaptureViewModel.ExperimentalFeatures`); switched off, a camera chosen earlier is not used.
  - Every place users meet it says "very experimental": CLI help, the GUI card, `CameraRig.ExperimentalNotice` in rig files and
    analysis warnings, docs. Keep it that way until it is validated with real hardware, and keep `doc/camera-status.md`
    (status, known issues, next steps) current with every camera change.
  - Saved cameras: `CameraRigLibrary` (`camera-rigs/` next to the config file; demo/automation GUI runs use their output root, never
    the user's library). GUI: `CameraWizardViewModel` + `CameraWizardWindow` (the wizard), `CameraRigViewModel` (the capture page card).
  - `Capture/source/Camera/`: `CameraCalibrator` (calibrate/verify), `CameraRig`/`CameraZone` (the rig file), `CameraRectifier` and
    `RectifyingCaptureSource` (C# path). `FfmpegCommandBuilder.BuildCameraFilter` is the ffmpeg path; both produce the same layout:
    zones of `CameraZone.StoredSizePx` stacked in scanout order.
  - The analysis switches to `ScanoutModel.Camera` when `capture.json` has a `camera` section.
  - Zones: `CameraZone.MainZone` (the main marker, identifies each frame) and `CameraZone.SyncZone` (the sync marker, times it:
    `TimelineAnalyzer.TimeBySyncMarker`). A camera decode only counts when every sampled module matches the decoded payload's symbol
    (`MarkerDecoder.MaxModuleMismatchFraction`).
  - The synthetic camera (`Capture/source/Synthetic/SyntheticCamera.cs`) is the ground truth. `selftest --camera --fps 1000
--refresh 60 [--tear-every 9]` runs it end to end.
  - Benchmarks: `dotnet run -c Release --project measure/tools/Benchmarks/Benchmarks.csproj -- --filter "*"`. Name the csproj: the
    folder's `.slnx` does not build the libraries optimized.
  - The precision-by-camera-rate table in `doc/camera.md` is generated: `python tools/camera_rate_table.py --update-doc` (runs
    `selftest --camera` per rate; selftest prints its error against the simulation for it). Rerun it after changes to the camera
    pipeline.
- **ffmpeg tests:** the end-to-end tests (`FfmpegImportTests`, category `ffmpeg`) are skipped when no ffmpeg is found; CI installs
  ffmpeg.
- **CI** (mb-quality is not available there; CI runs the same commands directly):
  - `.github/workflows/ci.yml` builds and tests C++, the CMake consumer project and .NET on Windows, Ubuntu and macOS.
  - Its other jobs: `lint` (Prettier, one type per file, ruff, basedpyright, actionlint, the Unity package assembly),
    `dotnet-lint` (dotnet format as mb-quality applies it, CSharpier, vulnerable packages), `cpp-analysis` (sanitizer tests,
    clang-format, clang-tidy) and `semver` (`tools/check_semver.py`).
  - `.github/workflows/release-marker.yml` releases the marker libraries on a `marker-v*` tag, `release-data.yml` the data libraries
    on a `data-v*` tag (`data/cpp/package_release.py --verify` installs the marker library and builds the archive against it; see
    `doc/releasing.md`). A `tools-v*` tag (which must match `measure/VERSION`) also builds the self-contained tools.
  - `.github/dependabot.yml` opens weekly grouped updates for GitHub Actions, NuGet (packages and dotnet tools), npm and pip.
    GoogleTest and nlohmann/json (FetchContent URLs) and qrcodegen (vendored) are updated by hand.
- **Semantic versions:** `python tools/check_semver.py` (after `dotnet tool restore`) checks the three VERSION files and compares the
  public API of `MB.FrameMarker` with the last `marker-v*` release and of `MB.FramePacing.Data` with the last `data-v*` release using
  ApiCompat. `marker/VERSION` and `data/VERSION` are the next releases' versions: raise them in the change that alters the API (0.x:
  minor for any API change; from 1.0: major for breaking changes).
- **Golden set:** if you change the marker payload or geometry, regenerate it with
  `marker/cpp/build/<preset>/Release/marker-render --golden test-data/markers` (Windows: `...\Release\marker-render.exe`), then run
  the C# tests and the Python tests (`python -m unittest discover -s marker/python -t marker/python`).
- **Verify the GUI without touching the desktop:** `dotnet run --project measure/tools/DocImages -c Release -- <scratch dir>` renders
  every page offscreen (Avalonia.Headless) and runs the synthetic demo capture and analysis; compare the images with `doc/images`
  (live numbers on the capture page vary). `mb-framepacing-gui --demo` is **not** headless: it opens a real window and waits for it to
  be closed. Demo, `--output-root` and DocImages runs never load or save the user's GUI settings. Do not take screenshots.

## Conventions

- **Vocabulary:** use the terms of `doc/vocabulary.md` in code, UI text and docs. **Display time** is the moment a frame was first
  seen on screen; **display time step** is how long it stayed (the time to the next frame). Animation error = animation time step -
  display time step.
- **Versions:** there are two version files.
  - `marker/VERSION`: the marker libraries. CMake reads it into `Version.hpp` (and `data/VERSION` into the data library's), which only
    `src/LibraryVersion.cpp` and the tests include: the umbrella headers declare `GetLibraryVersion()`, so a version bump rebuilds one file.
  - `measure/VERSION`: the tools. `measure/Directory.Build.props` reads it.
- **One type per file:** C++ and C# use one class/struct/enum per file (nested private helpers may stay nested). The C++ public API
  has one header per type; `FrameMarker.hpp` includes them all and declares the functions. CI runs `tools/check_one_type_per_file.py`.
- **Hot path:** the marker APIs run every frame, so they must not allocate. Add zero-allocation tests for new API.
- **Reference shaders (`marker/shaders/`, BSD):** one quad whose fragment shader finds each pixel's module, from the packed bits
  (`Bits()` as constants; the fastest way to draw the marker) or a texel per module. One folder per API: `hlsl/` (`FrameMarker.hlsl` is
  the lookup, also included by the Unity shaders; `build_upm.py` copies it into the package), `gl/`, `gles2/` (GLSL ES 1.00: no
  integers, so float arithmetic, and `#error` without `highp`: not 100 % exact with `mediump`), `vulkan/`. `python
tools/check_shaders.py` compiles them all (glslang, and DXC when found; CI runs it); `--render` (needs `pip install moderngl` and a
  GPU; `VULKAN_SDK` for DXC and SPIRV-Cross) draws every one and compares every pixel with `modules_to_bitmap`. Run it after touching
  a shader, and `check_in_unity.py` (also `--graphics glcore|gles|vulkan`) after touching the Unity ones.
- **Encode once, draw from the modules:** every marker library encodes a marker once (`GenerateModules` / C# `TryGenerateModules`: the
  `ModuleMatrix`, 1 bit per module, packed exactly as `modules.csv`) and draws it with `ModulesToQuads`, `ModulesToTriangles`,
  `ModulesToIndexed`, `ModulesToBitmap` or the static grid (`GridVertices` once, `ModulesToGridIndices` per frame). C#'s `ModuleMatrix` is a `ref struct` view over the caller's bytes (no allocation), C++'s a
  value with an inline `std::array`. There are no payload-taking draw functions.
- **.NET:**
  - hand-maintained SDK csproj files;
  - package versions only in the root `Directory.Packages.props`;
  - C# style: a boxed file header, 2-space indent, block namespaces, `m_`/`g_` field prefixes, CSharpier (`.csharpierrc`, width 150).
- **C++:** CMake 4.0+, C++20, warnings as errors, no allocations in the per-frame path, qrcodegen (C variant) vendored, GoogleTest
  through FetchContent (`FIND_PACKAGE_ARGS` lets an installed or Conan GTest win).
- **Our license is split by path** (root `LICENSE` lists it, and every commit in the history carries it):
  - BSD 3-Clause: `marker/` (the libraries applications embed; `marker/LICENSE` holds the text alone and is what the C++ archive,
    the CMake install and the Unity package ship), `doc/marker-format.md`, `doc/integrating.md`, `doc/marker-fields.md`, `test-data/markers/`, and the data
    libraries and their formats: `data/` (`data/LICENSE`), `doc/capture-data-format.md`, `doc/analysis-output-format.md`,
    `test-data/data/`.
  - PolyForm Perimeter 1.0.1: everything else (the tools, their libraries, scripts, other docs, build and CI files). The tools ship
    the root `LICENSE`, which holds both texts, since they include the BSD marker library.
  - Every source file names its license on an `SPDX-License-Identifier` line near its top: `BSD-3-Clause` under `marker/`,
    `test-data/markers/`, `data/` and `test-data/data/`, `LicenseRef-PolyForm-Perimeter-1.0.1` elsewhere (inside the boxed C# header, after a shebang, as an XML
    comment in XAML/MSBuild/solution files). Every code file (not MSBuild, solutions or workflows) has
    `SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS` on the line right before it, in the same comment style: the SPDX
    short form (as the Linux kernel and REUSE use it), never the license text in the file. `python tools/check_license_headers.py`
    checks both (CI `lint`), `--fix` adds missing ones (the copyright with the current year). Third-party code (`third_party/`) keeps its own notices; each language's marker library keeps it in a `third_party/`
    folder of its own (`marker/cpp/third_party/`, `marker/python/mb_framemarker/third_party/`), with its license text next to it.
  - A new file belongs to the license of its path. Moving code across that line (for example from `measure/` into `marker/`)
    changes its license: only Mana Battery can decide that.
- **Licenses:** every third-party component (vendored, NuGet, FetchContent, test-only) needs its license text in `licenses/` and a
  row in `licenses/README.md`, in the same change.
- **Two counters:** the capture index (capture card) and the marker frame index (application) are unrelated; never compare them.
- **GUI log:** `GuiLogging` configures NLog in code: a file per day (`gui-yyyy-MM-dd.log`, the last 7 days kept) in `logs` next to
  the GUI settings, or under the output root for `--output-root` and DocImages runs (DocImages checks it). It also logs the exceptions
  nothing else handles (UI thread, tasks, app domain); catch blocks that show an error also log it.
- **Settings files** (configuration, GUI settings, saved cameras) are written and deleted through `SettingsFile`: a unique temporary
  file, flushed to the disk, then renamed over the target. Before that, the `backup` folder next to it gets `<file>.bak` (the version
  replaced, or the deleted file) and, when the format version changes, `<file>.v<N>.bak` (the last file in format N). Only those; no
  history. Never `File.WriteAllText` or `File.Delete` a settings file directly. Deleting from the GUI asks first.
  - Every settings file has a `formatVersion` (`CurrentFormatVersion` on its type). Loaders refuse a newer format ("update the
    tools"); a file without the field counts as 1. Raise the version when the format changes incompatibly, and migrate older
    formats in the loader.
  - Errors about unreadable settings files include `SettingsFile.BackupHint(path)`.
- **ffmpeg** is an external executable, found via `--ffmpeg` / `MB_FFMPEG` / `mb-framepacing.json` / PATH / install folders
  (`FfmpegLocator`). It is never linked or bundled.
