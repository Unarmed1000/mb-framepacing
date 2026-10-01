# mb-framepacing: notes for Claude

Frame pacing / animation error measurement.

The repository has two parts, and the license follows them (see Conventions):

- **`sdk/`** (BSD 3-Clause) holds everything applications embed or use to read the results: one SDK with one version, laid out by
  language (`sdk/cpp/`, `sdk/csharp/`, `sdk/python/`, `sdk/unity/`, `sdk/shaders/`), its docs and its golden data. Its modules:
  - **marker**: what goes **into** the application, a QR marker drawn into every frame (C++ `MB::FramePacing::Marker`, C#
    `MB.FramePacing.Marker`, Python `mb_framepacing.marker`, the Unity package);
  - **data**: reads the tools' capture data and analysis output (C++ `MB::FramePacing::Data`, C# `MB.FramePacing.Data`, Python
    `mb_framepacing.data`);
  - **pacer** (C++ `MB::FramePacing::Pacer`; off, and its C# port out of the tree, until it is reworked): plans every frame on the display's refreshes with the adaptive swap interval rule
    (the full-window rule of mb-framepacing-explained's simulation, and its fix as the default) and aligns the animation time to refreshes (`AnimationClock`);
  - **core**: the types every module shares, `Point` and `Rectangle` (always valid: a negative size is 0) in every language (C++
    `MB::FramePacing` with the library version and the time types in `core/time/`: `TimeSpan` (C#'s `System.TimeSpan`, out of range throws), `TickCount64`, `TickCount32` (wraps every 429.5 s, compares across the wrap), `TimeSpan32`, and the optional `core/time/ChronoConversion.hpp`; `ByteSpanUtil` (`WriteLE`/`ReadLE<T>`: little-endian values, the
    byte count from the type) for every module's file and wire formats; the core and the marker module have 100 % test coverage
    (regions, functions, lines, branches), measured with llvm-cov without asserts (`NDEBUG`); the C# core has the same `TickCount64`, `TickCount32` and `TimeSpan32`
    (`sdk/csharp/core/source/Time/`, member for member, `System.TimeSpan` as the signed interval, .NET exceptions; `TimeSpanUtil.FromSeconds`
    converts seconds to the tick on every runtime, since Unity's `TimeSpan.FromSeconds` rounds to a millisecond), and the C# core and
    marker module have 100 % line and branch coverage too: `python tools/check_csharp_coverage.py` (Microsoft code coverage through `dotnet test --collect`; CI's `dotnet-lint`), C# assembly `MB.FramePacing`, Python
    `mb_framepacing`). The SDK never reads a clock: applications pass their own clock's times (the C++ tests' `SteadyClock` is a test
    helper). The core's types hide same-named types that a `using` brings into `MB.FramePacing.*` code: the GUI writes `Avalonia.Point`.
- **`measure/`** holds the .NET tools that **measure**: they record a capture card through ffmpeg and analyse the markers.

See `README.md` for the overview and `sdk/doc/marker-format.md` for the marker specification. **The document is the reference**: C++
(`sdk/cpp/marker/source/mb/framepacing/marker/FrameMarker.cpp`) and C# (`sdk/csharp/marker/source/FrameMarker.cs`) must match it byte for byte. The tools'
`MarkerPayload` (`measure/libs/MB.FramePacing.MarkerDecoding`) delegates to the C# module; ZXing is only used for decoding.

## Layout

| Path                                              | Contents                                                                                                          |
| ------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| `sdk/`                                            | **BSD 3-Clause**: everything below it; `sdk/README.md` is its entry point (where to start, parts, versions)       |
| `sdk/VERSION`, `sdk/LICENSE`                      | The SDK's one version (every module and language, released with `sdk-v*` tags) and its BSD text                   |
| `sdk/cpp/`                                        | The C++20 library: one CMake project (`mb_framepacing`), presets, `package_release.py`, `tests/consumer`          |
| `sdk/cpp/core/`                                   | Core module `mb_framepacing::core`: library version, time types, `Point`, `Rectangle` + tests                     |
| `sdk/cpp/marker/`                                 | Marker module `mb_framepacing::marker`, vendored qrcodegen, `marker-render` tool, GoogleTest tests                |
| `sdk/cpp/data/`                                   | Data module `mb_framepacing::data` (reads; nlohmann/json via FetchContent, inside only) + GoogleTest tests        |
| `sdk/cpp/pacer/`                                  | Pacer module `mb_framepacing::pacer`, GoogleTest tests with the simulation and `pacer-sim` (`tests/`, test code)  |
| `sdk/cpp/conan/`                                  | Conan 2 recipe `mb-framepacing`, a component per module (conan-center-index layout, a local-recipes-index remote) |
| `sdk/csharp/core/`                                | C# core module `MB.FramePacing` (`Rectangle`; .NET Standard 2.1, C# 9, no dependencies) + NUnit tests             |
| `sdk/csharp/marker/`                              | C# marker module `MB.FramePacing.Marker` (.NET Standard 2.1, C# 9, no dependencies) + NUnit tests                 |
| `sdk/csharp/data/`                                | C# data module `MB.FramePacing.Data` (.NET 10): reads and writes captures.mbcd and the analysis output            |
| `sdk/python/`                                     | Python package `mb_framepacing` (`marker`, `data`; standard library only, Python 3.12) + unittest tests           |
| `sdk/unity/`                                      | Unity package `com.manabattery.framepacing` sources (helpers, samples), `build_upm.py`, `check_in_unity.py`       |
| `sdk/shaders/`                                    | Reference shaders that draw the marker as one quad: HLSL, GLSL for OpenGL 3.3/ES 3.0, OpenGL ES 2.0 and Vulkan    |
| `sdk/doc/`                                        | Marker format and fields, integrating, Unity, vocabulary, capture data and analysis output formats                |
| `sdk/test-data/markers/`                          | Golden marker images and module digest from the C++ library                                                       |
| `sdk/test-data/data/`                             | The data modules' golden data: a test clip imported and analysed, and `digest.json`                               |
| `sdk/test-data/pacer/`                            | The pacer's golden data: scenario frames from test clips and every scenario's result (`pacer-sim --golden`)       |
| `measure/VERSION`                                 | Version of the tools (released with `tools-v*` tags)                                                              |
| `measure/app/`, `measure/libs/`, `measure/tools/` | CLI, Avalonia GUI, MarkerDecoding/Capture/Analysis/Charts libraries (+ `UnitTest/`), DocImages, Benchmarks        |
| `measure/doc/`                                    | Usage, camera, install guides, and the README images (`measure/doc/images`)                                       |
| `measure/test-data/videos/`                       | 60 Hz test clips with manifests from mb-framepacing-explained, `VideoClipTests`                                   |
| `doc/`                                            | Project docs: releasing, roadmap                                                                                  |
| `tools/`                                          | Repository scripts (checks, golden data, shaders, camera rate table)                                              |
| root `Directory.*.props`, `UnitTest.props`        | Shared .NET build settings (C# projects only; see below), central package versions                                |
| `mb-framepacing.slnx`                             | IDE solution with every .NET project                                                                              |
| `licenses/`                                       | Third-party licenses                                                                                              |

## Build and test

```
mb-quality -r --all .                            # the standard check: dotnet format + CSharpier + build + tests + vulnerable packages
mb-quality -r --repair .                         # apply formatting, then build and test
dotnet build mb-framepacing.slnx                 # warnings are errors (Directory.Build.props)
dotnet test  mb-framepacing.slnx
cd sdk/cpp && cmake --preset windows && cmake --build --preset windows && ctest --preset windows   # every module; linux / linux-clang / macos too
dotnet run --project measure/app/FramePacing/FramePacing.csproj -- selftest --fps 500  # end to end without hardware
uv sync                                          # the Python dev tools in .venv, on the Python of .python-version (3.12)
uv run python -m unittest discover -s sdk/python -t sdk/python    # the Python package against sdk/test-data
uv run tools/check_conan.py                      # the Conan recipe built from this checkout, in a temporary Conan home
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
  - C++ follows `sdk/cpp/.clang-format` and `sdk/cpp/.clang-tidy` (namespaces are CamelCase: `MB::FramePacing::Marker`). The
    `.clang-tidy` checks and options are gtec-demo-framework's (plus `hicpp-exception-baseclass`). Suppress a finding only with
    `NOLINTNEXTLINE(<check>)` and a comment line saying why (the MSVC standard library makes `bugprone-exception-escape` report
    allocation failures that are caught).
  - `python tools/check_cpp.py` runs both on our sources only (never `third_party/` or fetched dependencies) of every module
    (`--module core|marker|data|pacer` for one), with the versions CI pins in `uv.lock` (`uv run tools/check_cpp.py`). clang-tidy needs a
    configured build: `sdk/cpp/build/<preset>`, default `windows` (the VS generator writes no compile database, so the script passes the
    include paths); `--preset` takes another one. To apply formatting: `clang-format -i` on the files the script lists.
  - **The C++ library is one project of modules** (Boost/Poco style): a folder per module (`sdk/cpp/<module>/{include,source,tests}`),
    each a static library `mb_framepacing_<module>` (alias and export `mb_framepacing::<module>`), headers `<mb/framepacing/<module>/<Type>.hpp>`
    (no umbrella headers: callers include each type's header; functions live in a header of their own, e.g. `marker/FrameMarker.hpp`, as
    C#'s static classes), sources mirroring them (`source/mb/framepacing/<module>/<Name>.cpp`, one per header; private helpers in
    `source/.../detail/`, in a namespace named for what they are, such as `Marker::WireFormat` and `Data::CaptureDataFormat`, and named in
    full at every use: no `using namespace`), namespaces
    `MB::FramePacing` (core) and `MB::FramePacing::<Module>`. One export set and package (`find_package(mb_framepacing CONFIG
COMPONENTS ...)`). `MB_FRAMEPACING_BUILD_MARKER` / `_DATA` / `_PACER` leave modules out (no data module: nlohmann/json is never fetched).
    Each module's tests are their own executable. What they share is in `sdk/cpp/testing` (`mb_framepacing_test_support`, an OBJECT
    library built with the tests only, never installed): `Testing::AllocationCounter` and the counting global `operator new`/`delete`
    the allocation tests link.
  - **Executable size:** `sdk/cpp/tests/size` holds size probes (a baseline program, then one using the core, the marker, the marker
    and data modules), linked so unused code is dropped (the modules are built with `-ffunction-sections -fdata-sections`).
    `tools/measure_sdk_size.py --toolchain <id>` builds them in Release and MinSizeRel (in `sdk/cpp/build/size`) and reports each
    one's size minus the baseline's; `--update-doc` rewrites the table in `sdk/cpp/README.md` (between `sdk-size-table` markers),
    `--check` fails beyond 10 % (at least 4 KiB). CI's `cpp-size` job checks MSVC, GCC, Clang and AppleClang and uploads each
    measurement (`--update-doc size-*.json` takes them). Refresh the table in the change that alters a module's size.
  - **Benchmarks:** `sdk/cpp/marker/benchmarks` (Google Benchmark 1.9.5 through FetchContent, `MB_FRAMEPACING_BUILD_BENCHMARKS`, off
    by default, on in the `windows` and `linux-sanitize` presets): encoding, every drawing output, a whole frame. CI builds them
    (`cpp-analysis`) but never runs them: timings on shared runners are noise. Run a Release build locally. The C# marker has the
    same groups with BenchmarkDotNet (`sdk/csharp/marker/Benchmarks`, built with the solution; `Benchmarks` folders, like `UnitTest`,
    are kept out of their library's compile by the root `Directory.Build.props`).
  - Clang's `-Wconversion` includes `-Wsign-conversion` (GCC's and MSVC's do not), so macOS CI can fail where Windows and Linux
    pass: shift and combine small unsigned types after casting them to `uint32_t`.
  - The `linux-sanitize` preset (Clang, AddressSanitizer + UndefinedBehaviorSanitizer, compile database) is what CI runs the tests
    and clang-tidy with.
  - `cmake/Version.hpp.in` is guarded with `// clang-format off`, because formatting breaks its `@VAR@` placeholders.
- **Python scripts** (`measure/build_standalone.py`, `tools/`, `sdk/unity/build_upm.py`): standard library only. They must pass
  `ruff check .`, `ruff format --check .` and `basedpyright` (config: `ruff.toml`, `pyrightconfig.json`, recommended mode). CI runs
  all three.
- **uv manages the Python dev tools:** the root `pyproject.toml` (not a package) pins them in its `dev` group, `uv.lock` pins
  everything they bring, and `.python-version` is the minimum Python (3.12). `uv sync` creates `.venv` on it; `uv run <tool or
script>` runs inside it, and CI runs `uv sync --locked`. `tools/check_cpp.py` and `tools/check_conan.py` use the tools next to the
  Python that runs them, so `uv run tools/check_cpp.py` gets the pinned versions. Raise a tool: change its pin in `pyproject.toml`,
  then `uv lock` (Dependabot's `uv` ecosystem does this weekly). A one-off extra: `uv run --with <package> ...`.
- **Unity package** (`com.manabattery.framepacing`):
  - It isn't stored as one folder: `sdk/unity/build_upm.py` assembles it from `sdk/csharp/core/source` (`Runtime/Core`, asmdef
    `MB.FramePacing`), `sdk/csharp/marker/source` (`Runtime/Marker`, `MB.FramePacing.Marker`) plus `sdk/unity/Runtime/Unity` (helpers,
    all wrapped in `#if UNITY_2021_3_OR_NEWER`), and generates `.meta` files with stable GUIDs.
  - CI runs it with `--check`.
  - `sdk/unity/check_in_unity.py` verifies it in a real Unity editor in batch mode (every drawing method pixel exact; `--graphics
glcore|vulkan|d3d12` forces another graphics API); it takes the newest editor Unity Hub installed, or `--unity`. Run it after changing
    the core, the marker module or the helpers.
  - The core and the marker module must stay C# 9 / .NET Standard 2.1 without UnityEngine (Unity 2021.2+ has .NET Standard 2.1 in both API levels). Buffer
    APIs take `ReadOnlySpan<T>` for input and `Span<T>` for output, as the C++ library takes `std::span`.
- **Standalone release archive:** `sdk/cpp/CMakeLists.txt` finds `VERSION`, `LICENSE`, `licenses/` and `shaders/` next to itself in a
  release archive, and falls back to `sdk/VERSION`, `sdk/LICENSE`, `sdk/shaders` and the repository root's `licenses/` otherwise. The
  archive (`sdk/cpp/package_release.py`) holds every module, the docs and `test-data/data`, not `conan/`.
- **Docs**
  - Formatting: `npm install && npm run format` (Prettier: Markdown/JSON/YAML; config `.prettierrc.json`, ignores in
    `.prettierignore`).
  - Regenerate the README images with `dotnet run --project measure/tools/DocImages`. The SVG report examples (`report-example-*.svg`) come from test clips
    imported through ffmpeg (skipped without it): example pictures use the test clips, not the synthetic game. It renders the real GUI **offscreen**
    (Avalonia.Headless) in no-save mode and neutralises machine specific text. Never take desktop screenshots.
- **Data modules (BSD 3-Clause):** `MB.FramePacing.Data` reads and writes `captures.mbcd` (`sdk/doc/capture-data-format.md`) and
  the analysis output (`sdk/doc/analysis-output-format.md`: `summary.json` with `formatVersion`, which covers the CSVs, and the CSVs). The
  tools write and read every file through it; their own types map to it (`CaptureDataMapping` in Capture, `AnalysisDataMapping` in
  Analysis). Keep the output byte for byte: the golden data (`sdk/test-data/data`, `digest.json`) is written back exactly, and every
  language's reader must read the digest's values. After a format change: `python tools/update_test_data.py` (needs ffmpeg).
  - **Typed times** (C++ now, C# and measure/ to follow, the same names): points in time are `TickCount64` (named `…Time`:
    `FirstSeenTime`, `HostTime`, `DeviceTime`, empty when unknown), spans `TimeSpan` (named for what they are: `DisplayDelta`, `Drift`,
    `TargetFrameTime`), the marker's own 32-bit values `TimeSpan32` (`MarkerTargetFrameTime`, `CpuBusy`); the CSV's milliseconds parse
    with `ParseMilliseconds`. The files' bytes and the golden data do not change.
- **Pacer module (`sdk/cpp/pacer`, `sdk/doc/pacer.md`):** the C++ module is off (`MB_FRAMEPACING_BUILD_PACER` and Conan's
  `with_pacer` default to off) until it is reworked; build it with `-DMB_FRAMEPACING_BUILD_PACER=ON` to work on it.
  - Values in, values out: `FrameInput` (the platform's values) → `FrameSchedule` (what to apply, the marker's pacing values), `FrameEnd`
    → CPU busy. No platform API, no callbacks, no clock reads; made once (the rule's window), no allocation after that.
  - `FrameInput`'s optional platform values (vsync, previous display, predicted display, `RefreshPeriodNanoseconds`) use **0 =
    unknown**, as the marker fields do (agreed with the user over `std::optional`: one convention, plain blittable structs).
  - **Always valid:** `RefreshPeriod` (1 tick to 1 s, no default: the application gives its display's period) and `PacerSettings`
    (constructed from the period; setters assert, then clamp). The ranges keep the rule's Q32 arithmetic within 64 bits; nothing
    downstream sanitizes. A reported period of another nanosecond, or `SetRefreshPeriod` with another period, restarts the pacer.
  - Integer arithmetic only (periods and grids in 2⁻³² ticks): a port must give the golden data's bytes too. The C#
    port (`MB.FramePacing.Pacer`, last in commit e7ff58b's tree) was removed until the rework: port it again from the reworked C++.
  - The simulation of a frame loop (`pacer/tests/simulation`) and `pacer-sim` (`pacer/tests/pacer-sim`) are test code, built with the
    tests only: never part of the library.
  - Golden data: `python tools/update_pacer_test_data.py` writes the scenarios' frames from the test clips and runs `pacer-sim --golden`;
    the tests compare every scenario's result byte for byte, and cross-check the clips (`60-busy`: the full-window rule reproduces the
    sister repo's swap intervals and refreshes; `60-busy-full-rate`: at a fixed swap interval every frame is on the clip's refresh).
    Run it after a change to the rule or the planning and review the difference.
- **Capture data (the default):** a capture decodes every frame's markers live and stores only them, with the timestamps, in
  `captures.mbcd` (`sdk/doc/capture-data-format.md`); the frames themselves (`frames.mbfc`) only with `--keep-frames` / the GUI's "Store
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
      card: `--charts`, Save charts, the GUI and DocImages never set them. `ClampStatic` (on by default) is the one the GUI switches:
      its "Clamp static" check box, which Save view and Save charts follow (`ChartFiles.Write(report, options)`).
    - Cards are shapes (`CardDrawing`: `RectShape`, `LineShape`, `PathShape`, `TextShape`, `GroupShape`, plus `CardPlot` per plot
      area with its data range, for the GUI's zoom and hover and for tests that read values back); `SvgCardWriter` writes them.
      `DistributionCard` builds the error and display time step histograms, the error percentiles and the drift (`render --cards`).
    - The frame timeline (`FrameTimelineCard`, `render --timeline`, at most 40 frames) is explained's timing diagram from the data: CPU
      boxes (CPU start time + CPU busy, overlapping ones in further lanes) placed on the capture's clock by `PacerToCapture` (display time
      - intended display time - lateness; without a schedule, no frame presented after it appears), present arrows, display cells. A
        static frame's cells are violet (`strip-static-a`/`-b`) and the error row says "static" on the static step after it; a frame presented on demand is never
        "held longer" (no interval to be late for).
    - The headline numbers come from `RunHeadline` only: the GUI's tiles and the report card's show the same tiles, and the GUI's
      Display card the report's display box (`RunHeadline.Display`, `DisplaySummary`), so add or change a number there, not in the GUI.
      A tile without a number shows "-" with `HasValue` false.
    - The animation error panel draws a refresh line (`error-refresh`, dashed amber, labelled "+1 refresh (16.7 ms)") at every whole
      refresh an error reaches within a tenth of a refresh, over the frames the scale covers, and only inside the scale
      (`ChartScale.ErrorRefreshTicks`, at most four per side).
    - The error and display time step charts cover every value unless a few are more than 8 times beyond the 99th percentile (a hitch);
      those get a mark with their value at the edge (`ChartScale` decides the scales, `ReportCard.ClipMarks` draws the marks).
    - **Keys:** every panel's title says what it shows; its key (`ReportCard.Key`, right aligned) names each colour with one swatch per
      entry and lists only what the section shows. A key, a tile's value with its detail and the display box's rate line are one
      `TextRunsShape` (pieces of text, each in its own classes) that the renderer lays out end to end (a `<text>` with `<tspan>`s in SVG,
      measured pieces in `CardView`), so no width is guessed. Swatches are coloured characters in the `key-*` classes (■ fill, ━ line,
      ┅ refresh and reference lines, ▐▌ for colours that alternate per frame).
    - **Holds and strip cells by kind** (`RunChartData.HoldKinds`, `HoldKind`): unknown (a capture gap, dashed grey), late (red), an
      older frame came back (out of order, pink), frames dropped by the target (orange), as planned (green). Dropped = skipped frame
      indices over a gap-free capture that never came back out of order (`DroppedBeforeFrame`); the strip colours the refreshes where
      they were due orange and the out-of-order refreshes (`PresentedFrame.OlderFrames`, CSV `olderFrames`) pink (anywhere in a frame's cells).
    - **Reference lines** (`frame-time-lines`, shown by default): the target (light grey) and preferred frame time (amber, only where it
      differs) as dashed stepped lines on the display time step panel (whole refreshes, `FrameTimeRounding.WholeRefreshes`, the
      analysis's rule) and the frametime panel (as written). Per hold, the values of the frame that ends it (`FrameReference`: the
      marker's, else the analysis's fallbacks, without the schedule's step or the 1 + dropped multiple); `RunChartData.StepReferences`
      / `FrameTimeReferences` are stretches of equal values, and the scales take the animating frames' values (static ones stay out).
    - **Events panel** (`events`, under the strip, at every zoom): two lanes never mixed up, **frames** (dropped by the target, out of
      order, torn) and **capture** (not recorded, dropped by the source, missed, not decoded), one mark per pixel column and lane, the
      first kind of `RunEvents.FrameKinds` / `CaptureKinds` that the column has. `RunEvents` (`RunChartData.Events`) holds each kind's
      events sorted with running totals; the capture's come from `ChartRun.Captures` (the capture rows as captures.csv has them: live from
      the report, `ChartRun.CapturesOf` once per capture; from files, `AnalysisOutput` reads captures.csv), so the report from the files
      is the live one (`ChartVideoClipTests`). The key counts each kind in the section, the description names the capture gaps by kind,
      and `CardHover` lists a column's events with the frames they name. The strip has no marks.
    - **Static stretches:** a violet band (`static-band`) behind every time panel from the first static frame's display time to the next
      frame's (`RunChartData.StaticStretches`), violet strip cells (`strip-static-a`/`-b`). With `ReportOptions.ClampStatic` (the
      default; `render --no-static-clamp`, the GUI's "Clamp static" check box, `GuiSettings.ClampStatic`) the display time step and
      frametime scales leave static frames' values out: holds, the animation time step overlay, frametimes, CPU busy and the reference
      lines of holds ending at a static frame (`RunChartData.AnimatingHolds`, `AnimatingAnimationHolds`, `AnimatingFrameTimes`,
      `AnimatingCpuBusy`, `FrameTimesAndCpuBusy`, `AnimatingStepReferences`); they get edge marks with their value. Off, the `All…`
      and unfiltered sequences set the scales.
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
- **Markers (format version 1, `sdk/doc/marker-format.md`):** a 53 byte header shared by frame, start and end, its fields grouped by
  category: format (magic, version, kind), which run and frame (run id, frame index), what the frame shows (flags, animation time),
  pacing (preferred frame time, target frame time, intended display time), the CPU's work (CPU start time and CPU busy, named as
  PresentMon's CPUStartTime and MsCPUBusy). Start/end carry the values of the frame that shows them, and the tearing check compares
  their run id and frame index with the sync marker's. Every main marker (frame, start, end) is QR version 6 (41×41), so it never changes size and has room for future fields.
  - **Pacing terms:** the intended display time is the pacer's aim; the animation time is the predicted display time the game
    animated for (`sdk/doc/vocabulary.md`). The target frame time is what the pacer aims for now, the **preferred frame time** what the
    application wants (it differs only while the pacer runs slower); `0xFFFFFFFF` in both = on demand. Never call the preferred frame
    time "desired": that is Vulkan's word for a present time point.
  - **Flags:** static is a frame's time on screen (nothing animates until the next frame; the frame itself may have moved). Bit 0
    `StaticAfter` says it on the frame (known upfront: no pending work), bit 1 `StaticBefore` on the next frame (known in hindsight,
    only for frame index − 1). `TimelineAnalyzer.BuildFrames` turns both into `PresentedFrameFlags.StaticAfter` on the frame and
    `StaticBefore` on the next: that static step gets no animation or prediction error (the drift sums only judged errors), the step
    into the static frame is judged. Independent flags, not an enum; bits 2 to 7 are reserved (write 0, decoders keep them).
  - **Field order:** every payload type (C++ and C# `Payload`, Python `Payload`, the tools' `MarkerPayload`) lists its fields in the
    order of the wire format: kind, run id, frame index, flags, animation time (required by every constructor; C++ also has a default
    constructor and stays trivially copyable and standard layout: private fields read through getters, `WithKind` for another kind; the
    constructor asserts the kind and `EncodePayload` refuses an unknown one, the flags are kept as given), then the optional
    preferred frame time, target frame time, intended display time, CPU start time and CPU busy. The golden CSVs' columns
    (`sdk/test-data/markers`) and the docs' field lists follow it; keep new fields in their wire position.
  - **Frame rates describe the frames that animate:** a static frame's time on screen (the next frame's display time step, flag
    `StaticBefore`) is left out of average fps, the lows and the display time step statistics and histogram
    (`RunStatistics.CountsTowardFrameRate`, `RunChartData.FrameRateSteps`), and counted (`excludedStaticFrames`); the tile, the report's
    description and the statistics tables say "excluding N static frames" (`RunHeadline.ExcludedStatic`).
  - **Late share:** amber = on screen at least half a refresh longer than the preferred frame time (the marker's, else
    `--target-fps`, else one refresh) without being late; never for static steps or on-demand frames (`LateShareData`, `PresentedFrame.PreferredTicks`).
  - The start marker (77 bytes) carries a 16 byte opaque sequence id (`SequenceId`: a UUID or a text tag of at
    most 16 ASCII characters, shown as text or UUID hex), not a name. Format version 1 is the baseline for all data (markers,
    captures.mbcd, analysis output): change it in place, no version bump, until there are users. The sync marker (kind 3, 16 bytes: the header's start, run id and frame index; matched to its main marker by both) is QR version 2 (25×25), drawn
    bottom-left: it checks tearing (capture cards, optional) and times the frames for a camera (required). `Options.RecommendedOrigin(kind, …)`
    places both; there are no other slots.
- **Camera capture (VERY EXPERIMENTAL, `measure/doc/camera.md`):**
  - The GUI hides it (the camera card, the synthetic camera source) unless **Settings → Experimental features** is on
    (`GuiSettings.ExperimentalFeatures`, `CaptureViewModel.ExperimentalFeatures`); switched off, a camera chosen earlier is not used.
  - Every place users meet it says "very experimental": CLI help, the GUI card, `CameraRig.ExperimentalNotice` in rig files and
    analysis warnings, docs. Keep it that way until it is validated with real hardware, and keep `measure/doc/camera-status.md`
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
  - The precision-by-camera-rate table in `measure/doc/camera.md` is generated: `python tools/camera_rate_table.py --update-doc` (runs
    `selftest --camera` per rate; selftest prints its error against the simulation for it). Rerun it after changes to the camera
    pipeline.
- **ffmpeg tests:** the end-to-end tests (`FfmpegImportTests`, category `ffmpeg`) are skipped when no ffmpeg is found; CI installs
  ffmpeg.
- **CI** (mb-quality is not available there; CI runs the same commands directly):
  - `.github/workflows/ci.yml` builds and tests C++, the CMake consumer project and .NET on Windows, Ubuntu and macOS.
  - Its other jobs: `lint` (Prettier, one type per file, ruff, basedpyright, actionlint, the Unity package assembly),
    `dotnet-lint` (dotnet format as mb-quality applies it, CSharpier, vulnerable packages), `cpp-analysis` (sanitizer tests,
    clang-format, clang-tidy) and `semver` (`tools/check_semver.py`).
  - `.github/workflows/release-sdk.yml` releases the SDK (C++ archive, Unity package, Conan check) on an `sdk-v*` tag (see
    `doc/releasing.md`). A `tools-v*` tag (which must match `measure/VERSION`) also builds the self-contained tools.
  - `.github/dependabot.yml` opens weekly grouped updates for GitHub Actions, NuGet (packages and dotnet tools), npm and pip.
    GoogleTest and nlohmann/json (FetchContent URLs) and qrcodegen (vendored) are updated by hand.
- **Conan recipe (`sdk/cpp/conan`):**
  - `recipes/mb-framepacing/all/` holds `conanfile.py` (a component per module; options `with_marker`, `with_data`, `with_pacer`), `conandata.yml`
    (each version's release archive URL and SHA-256) and `test_package/`; `recipes/mb-framepacing/config.yml` lists the versions. It
    builds the release archive (`package_release.py`), never the checkout.
  - A version is added after its release: `python tools/add_conan_version.py <version>` (reads the release's `SHA256SUMS`), then
    commit. The release workflow tests the new archive through the recipe (`check_conan.py --released`).
  - `tools/check_conan.py` (CI `conan` job, Windows/Ubuntu/macOS) builds the recipe from this checkout's archive, using a copy of
    `sdk/cpp/conan` as a local-recipes-index remote: `conan test` of the test package with `compiler.cppstd=20`, and a build with the
    core and marker modules only.
  - **Every Conan run uses a temporary `CONAN_HOME`**: never touch the user's cache (their global Conan may be another version;
    running a newer one migrates the cache). Conan is pinned in `pyproject.toml`'s dev group.
  - basedpyright excludes `sdk/cpp/conan` (Conan's API is untyped); ruff still checks the recipe. The test package's C++ is formatted
    by `check_cpp.py` (with `sdk/cpp/.clang-format`).
- **Semantic versions:** `python tools/check_semver.py` (after `dotnet tool restore`) checks the two VERSION files and compares the
  public API of every C# module (`MB.FramePacing.Marker`, `MB.FramePacing.Data`) with the last `sdk-v*` release using ApiCompat.
  `sdk/VERSION` is the next release's version: raise it in the change that alters the API (0.x: minor for any API change; from 1.0:
  major for breaking changes).
  - A version may be a pre-release: `-alpha.N`, `-beta.N` or `-rc.N` only (doc/releasing.md "Pre-releases"). CMake splits it off
    (`project()` takes the numbers; `Version.hpp` has `VersionString` and `VersionPrerelease`), .NET makes it the `VersionSuffix`, and
    Python spells it as PEP 440 does (`0.2.0b1`, by hand in `pyproject.toml` and `__version__`; `test_version.py` checks both). The
    API baseline is the newest stable release.
  - CMake reads VERSION on every configure (a normal variable; an edit reconfigures); `-D<NAME>_VERSION_OVERRIDE` builds one tree as
    another version.
- **Golden set:** if you change the marker payload or geometry, regenerate it with
  `sdk/cpp/build/<preset>/marker/marker-render --golden sdk/test-data/markers` (Windows: `sdk\cpp\build\windows\marker\Release\marker-render.exe`),
  then run the C# tests and the Python tests (`python -m unittest discover -s sdk/python -t sdk/python`).
- **Verify the GUI without touching the desktop:** `dotnet run --project measure/tools/DocImages -c Release -- <scratch dir>` renders
  every page offscreen (Avalonia.Headless) and runs the synthetic demo capture and analysis; compare the images with `measure/doc/images`
  (live numbers on the capture page vary). `mb-framepacing-gui --demo` is **not** headless: it opens a real window and waits for it to
  be closed. Demo, `--output-root` and DocImages runs never load or save the user's GUI settings. Do not take screenshots.

## Conventions

- **Branches:** `master` restarted on 2026-09-30 as one commit holding the tree of `legacy` 595e0f4, so its history has one folder
  structure (`sdk/`, `measure/`). All development happens on `master`, and its history stays linear. `legacy` holds the history
  before that. It is frozen (it ends in a merge of `master`, with the same tree) and may be deleted: old commit SHAs, such as
  submodule pins, only stay reachable while it exists.
- **Vocabulary:** use the terms of `sdk/doc/vocabulary.md` in code, UI text and docs. **Display time** is the moment a frame was first
  seen on screen; **display time step** is how long it stayed (the time to the next frame). Animation error = animation time step -
  display time step.
- **Versions:** there are two version files.
  - `sdk/VERSION`: the SDK, every module and language (C#: `sdk/Directory.Build.props`; Python: by hand in `pyproject.toml` and
    `mb_framepacing.__version__`). CMake reads it into `mb/framepacing/core/Version.hpp`, which only `core/source/mb/framepacing/core/GetLibraryVersion.cpp` and the
    tests include: `core/GetLibraryVersion.hpp` declares `GetLibraryVersion()`, so a version bump rebuilds one file.
  - `measure/VERSION`: the tools. `measure/Directory.Build.props` reads it.
- **One type per file:** C++ and C# use one class/struct/enum per file (nested private helpers may stay nested). The C++ public API
  has one header per type and no umbrella headers (a header that includes a whole module is a god file:
  `tools/check_one_type_per_file.py` rejects one); the functions have headers of their own. CI runs `tools/check_one_type_per_file.py`.
- **Hot path:** the marker APIs run every frame, so they must not allocate. Add zero-allocation tests for new API.
- **Reference shaders (`sdk/shaders/`, BSD):** one quad whose fragment shader finds each pixel's module, from the packed bits
  (`Bits()` as constants; the fastest way to draw the marker) or a texel per module. One folder per API: `hlsl/` (`FrameMarker.hlsl` is
  the lookup, also included by the Unity shaders; `build_upm.py` copies it into the package), `gl/`, `gles2/` (GLSL ES 1.00: no
  integers, so float arithmetic, and `#error` without `highp`: not 100 % exact with `mediump`), `vulkan/`. `python
tools/check_shaders.py` compiles them all (glslang, and DXC when found; CI runs it); `--render` (`uv run --with moderngl tools/check_shaders.py --render`, needs a
  GPU; `VULKAN_SDK` for DXC and SPIRV-Cross) draws every one and compares every pixel with `modules_to_bitmap`. Run it after touching
  a shader, and `check_in_unity.py` (also `--graphics glcore|gles|vulkan`) after touching the Unity ones.
- **Encode once, draw from the modules:** every marker library encodes a marker once (`GenerateModules` / C# `TryGenerateModules`: the
  `ModuleMatrix`, 1 bit per module, packed exactly as `modules.csv`) and draws it with `ModulesToQuads`, `ModulesToTriangles`,
  `ModulesToIndexed`, `ModulesToBitmap` or the static grid (`GridVertices` once, `ModulesToGridIndices` per frame). A drawn rectangle is a
  `MarkerQuad` (a core `Rectangle` `Rect` and `Dark`). `Options` (module size and quiet zone) is always valid: C++ asserts a value outside its range and clamps it
  without asserts, C# and Python clamp (C# stores it relative to the defaults, so `default(Options)` is `Options.Default`); the sizing
  and placement are its members (`Recommended`, `Minimum`, `MarkerSizePx`, `QuietZonePx`, `RecommendedOrigin`). Pixel formats are named
  by their channels: `R8`, `R8G8B8`, `R8G8B8A8`. The C# marker mirrors the C++ one: the wire format is the internal `WireFormat`, the symbol sizes are `ModuleMatrix`'s
  (`MainSize`, `SyncSize`, `SizeFor`, `PackedModuleByteCount`), the sizing limits `Options`', `Payload.MaxEncodedByteCount` and
  `Payload.OnDemandFrameTime` the payload's, `PixelFormatUtil.BytesPerPixel` the pixel formats'; `Payload`'s fields are typed and named
  as C++'s getters, and its constructor throws `ArgumentOutOfRangeException` for a kind that is not a `MarkerKind`. The C# static
  class is `FrameMarker` (not `Marker`: a class named like its namespace `MB.FramePacing.Marker` breaks lookups in `MB.FramePacing.*` code). C#'s `ModuleMatrix` is a `ref struct` view over the caller's bytes (no allocation), C++'s a
  value with an inline `std::array`. There are no payload-taking draw functions.
- **.NET:**
  - hand-maintained SDK csproj files;
  - package versions only in the root `Directory.Packages.props`;
  - C# style: a boxed file header, 2-space indent, block namespaces, `m_`/`g_` field prefixes, CSharpier (`.csharpierrc`, width 150).
  - SDK value types are `readonly struct`s with `public readonly` fields (derived values are expression-bodied properties) and
    `GetHashCode` through `HashCode.Combine` (a `HashCode` builder past 8 fields, `Payload`).
- **C++:** CMake 4.0+, C++20, warnings as errors, no allocations in the per-frame path, qrcodegen (C variant) vendored, GoogleTest
  through FetchContent (`FIND_PACKAGE_ARGS` lets an installed or Conan GTest win).
- **Our license is split by path** (root `LICENSE` lists it, and every commit in the history carries it):
  - BSD 3-Clause: everything under `sdk/`: the SDK's libraries in every language (`sdk/LICENSE` holds the text alone and is what the
    C++ archive, the CMake install, the Python package and the Unity package ship), their docs (`sdk/doc/`) and their golden data
    (`sdk/test-data/`).
  - PolyForm Perimeter 1.0.1: everything else (the tools, their libraries, scripts, other docs, build and CI files). The tools ship
    the root `LICENSE`, which holds both texts, since they include the BSD marker library.
  - Every source file names its license on an `SPDX-License-Identifier` line near its top: `BSD-3-Clause` under `sdk/`,
    `LicenseRef-PolyForm-Perimeter-1.0.1` elsewhere (inside the boxed C# header, after a shebang, as an XML
    comment in XAML/MSBuild/solution files). Every code file (not MSBuild, solutions or workflows) has
    `SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS` on the line right before it, in the same comment style: the SPDX
    short form (as the Linux kernel and REUSE use it), never the license text in the file. `python tools/check_license_headers.py`
    checks both (CI `lint`), `--fix` adds missing ones (the copyright with the current year). Third-party code (`third_party/`) keeps its own notices; each language's marker module keeps it in a `third_party/`
    folder of its own (`sdk/cpp/marker/third_party/`, `sdk/python/mb_framepacing/marker/third_party/`), with its license text next to it.
  - A new file belongs to the license of its path. Moving code across that line (for example from `measure/` into `sdk/`)
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
