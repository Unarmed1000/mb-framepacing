# mb-framepacing: notes for Claude

Frame pacing / animation error measurement.

The repository has two parts, and the license follows them (see Conventions):

- **`sdk/`** (BSD 3-Clause) holds everything applications embed or use to read the results: one SDK with one version, laid out by
  language (`sdk/cpp/`, `sdk/csharp/`, `sdk/python/`, `sdk/unity/`, `sdk/shaders/`), its docs and its golden data. Its modules:
  **marker** (the QR marker drawn into every frame), **data** (reads the tools' capture data and analysis output), **pacer** (C++
  only, experimental, off by default), **trace** (the frame record an application logs to its own Perfetto trace) and **core** (the
  shared types: `Point`, `Rectangle`, the nanosecond time types, `ByteSpanUtil`). The SDK never reads a clock. Details:
  `doc/claude/sdk-and-data.md`.
- **`measure/`** holds the .NET tools that **measure**: they record a capture card through ffmpeg and analyse the markers.

See `README.md` for the overview and `sdk/doc/marker-format.md` for the marker specification. **The document is the reference**: C++
(`sdk/cpp/marker/source/mb/framepacing/marker/FrameMarker.cpp`) and C# (`sdk/csharp/marker/source/FrameMarker.cs`) must match it byte for byte. The tools'
`MarkerPayload` (`measure/libs/MB.FramePacing.MarkerDecoding`) delegates to the C# module; ZXing is only used for decoding.

## Layout

| Path                                              | Contents                                                                                                             |
| ------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| `sdk/`                                            | **BSD 3-Clause**: everything below it; `sdk/README.md` is its entry point (where to start, parts, versions)          |
| `sdk/VERSION`, `sdk/LICENSE`                      | The SDK's one version (every module and language, released with `sdk-v*` tags) and its BSD text                      |
| `sdk/cpp/`                                        | The C++ library (C++20/17): one CMake project (`mb_framepacing`), presets, `package_release.py`, `tests/consumer`    |
| `sdk/cpp/core/`                                   | Core module `mb_framepacing::core`: library version, time types, `Point`, `Rectangle` + tests                        |
| `sdk/cpp/marker/`                                 | Marker module `mb_framepacing::marker` with its own QR encoder, `reference/` (qrcodegen), `marker-render`, tests     |
| `sdk/cpp/data/`                                   | Data module `mb_framepacing::data` (reads; nlohmann/json via FetchContent, inside only) + GoogleTest tests           |
| `sdk/cpp/pacer/`                                  | Pacer module `mb_framepacing::pacer`, GoogleTest tests with the simulations and `pacer-sim` (`tests/`, test code)    |
| `sdk/cpp/trace/`, `sdk/csharp/trace/`             | Trace module `mb_framepacing::trace` / `MB.FramePacing.Trace`: the frame record + tests                              |
| `sdk/cpp/conan/`                                  | Conan 2 recipe `mb-framepacing`, a component per module (conan-center-index layout, a local-recipes-index remote)    |
| `sdk/csharp/core/`                                | C# core module `MB.FramePacing` (`Point`, `Rectangle`, the time types; .NET Standard 2.1, C# 9) + NUnit tests        |
| `sdk/csharp/marker/`                              | C# marker module `MB.FramePacing.Marker` (.NET Standard 2.1, C# 9, no dependencies) + NUnit tests                    |
| `sdk/csharp/data/`                                | C# data module `MB.FramePacing.Data` (.NET 10): reads and writes captures.mbcd and the analysis output               |
| `sdk/python/`                                     | Python package `mb_framepacing` (`marker`, `data`; standard library only, Python 3.12) + unittest tests              |
| `sdk/unity/`                                      | Unity package `com.manabattery.framepacing` sources (helpers, samples), `build_upm.py`, `check_in_unity.py`          |
| `sdk/shaders/`                                    | Reference shaders that draw the marker as one quad: HLSL, GLSL for OpenGL 3.3/ES 3.0, OpenGL ES 2.0 and Vulkan       |
| `sdk/doc/`                                        | Marker format and fields, integrating, Unity, vocabulary, the data formats, the pacer guide, encoding performance    |
| `sdk/test-data/markers/`                          | Golden marker images and module digest from the C++ library                                                          |
| `sdk/test-data/data/`                             | The data modules' golden data: a test clip imported and analysed, and `digest.json`                                  |
| `sdk/test-data/pacer/`                            | The pacer's golden data (`pacer-sim --golden`): scenario frames and results, the tier loops' digests and frames      |
| `sdk/test-data/trace/`                            | The trace module's golden data: payloads with the frame record each gives (`frame-records.csv`)                      |
| `measure/VERSION`                                 | Version of the tools (released with `tools-v*` tags)                                                                 |
| `measure/app/`, `measure/libs/`, `measure/tools/` | CLI, Avalonia GUI, MarkerDecoding/Capture/TraceJoin/Analysis/Charts libraries (+ `UnitTest/`), DocImages, Benchmarks |
| `measure/doc/`                                    | Usage, install guides, live capture and camera (both experimental), the README images (`measure/doc/images`)         |
| `measure/test-data/videos/`                       | Test clips with manifests from mb-framepacing-explained, `VideoClipTests`, the stress clips' `StressClipTests`       |
| `measure/test-data/trace/`, `frame-logs/`         | Made-up traces and frame logs with what the scripts gave for them: what the C# ports are held to                     |
| `doc/`                                            | Project docs: releasing, roadmap                                                                                     |
| `doc/claude/`                                     | Topic notes for Claude, pointed to from CLAUDE.md "Topic notes"                                                      |
| `pacer-captures/`                                 | Capture sessions of the pacer: results documents, charts, `runs.csv`; the zips are assets of a GitHub release        |
| `tools/`                                          | Repository scripts (checks, golden data, shaders, camera rate table)                                                 |
| root `Directory.*.props`, `UnitTest.props`        | Shared .NET build settings (C# projects only; see below), central package versions                                   |
| `mb-framepacing.slnx`                             | IDE solution with every .NET project                                                                                 |
| `licenses/`                                       | Third-party licenses                                                                                                 |

## Build and test

```
mb-quality -r --all .                            # the standard check: dotnet format + CSharpier + build + tests + vulnerable packages
mb-quality -r --repair .                         # apply formatting, then build and test
dotnet build mb-framepacing.slnx                 # warnings are errors (Directory.Build.props)
dotnet test  mb-framepacing.slnx
cd sdk/cpp && cmake --preset windows && cmake --build --preset windows && ctest --preset windows   # every module, the pacer too (on in windows and linux-sanitize); linux / linux-clang / macos
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
- **Python scripts** (`measure/build_standalone.py`, `tools/`, `sdk/unity/build_upm.py`): standard library only, but for the
  Perfetto scripts (`doc/claude/perfetto-trace.md`). They must pass `ruff check .`, `ruff format --check .` and `basedpyright` (config: `ruff.toml`,
  `pyrightconfig.json`, recommended mode). CI runs all three. Their tests are in `tools/tests` (`python -m unittest discover -s
tools/tests -t tools`; CI's `lint`).
- **uv manages the Python dev tools:** the root `pyproject.toml` (not a package) pins them in its `dev` group, `uv.lock` pins
  everything they bring, and `.python-version` is the minimum Python (3.12). `uv sync` creates `.venv` on it; `uv run <tool or
script>` runs inside it, and CI runs `uv sync --locked`. `tools/check_cpp.py` and `tools/check_conan.py` use the tools next to the
  Python that runs them, so `uv run tools/check_cpp.py` gets the pinned versions. Raise a tool: change its pin in `pyproject.toml`,
  then `uv lock` (Dependabot's `uv` ecosystem does this weekly). A one-off extra: `uv run --with <package> ...`.
- **Docs**
  - Formatting: `npm install && npm run format` (Prettier: Markdown/JSON/YAML; config `.prettierrc.json`, ignores in
    `.prettierignore`).
  - Regenerate the README images with `dotnet run --project measure/tools/DocImages`. The SVG report examples (`report-example-*.svg`) come from test clips
    imported through ffmpeg (skipped without it): example pictures use the test clips, not the synthetic game. It renders the real GUI **offscreen**
    (Avalonia.Headless) in no-save mode and neutralises machine specific text; the GUI screenshots import the test clip `60-busy-adaptive` as a
    video file (so they need ffmpeg). Never take desktop screenshots. The README's two pictures of the marker in an application
    (`example-app-marker.png`, `example-app-sync-marker.png`) are not DocImages': they are the Vulkan FramePacing sample of the user's
    unofficial gtec-demo-framework (`DemoApps/Vulkan/FramePacing/ExampleMarker.png`, `ExampleSyncMarker.png`, taken with the
    framework's screenshot option), copied unchanged; new ones come from there.
- **The root README never mentions an experimental feature** (the user's rule) except in its one "Experimental features" section,
  which names each (live capture, camera capture, the pacer) with a link to its guide: no experimental commands, options, GUI
  switches or diagram paths anywhere else in it. The other documents say "experimental" wherever the feature appears.
- **ffmpeg tests:** the end-to-end tests (`FfmpegImportTests`, category `ffmpeg`) are skipped when no ffmpeg is found; CI installs
  ffmpeg.
- **The .NET tests run at once and are meant to be quick** (the user, 2026-10-13: "a simple project", a minute or two for the
  tests at the very most): the root `UnitTest.props` gives every test project `Parallelizable(ParallelScope.All)` and
  `FixtureLifeCycle(InstancePerTestCase)`, so every test case runs in parallel on a fixture object of its own (what `SetUp` keeps
  in fields is that test's alone; a `OneTimeSetUp` would have to be static).
  - **A test owns what it changes:** a temporary folder of its own (a new GUID), never a file, folder or static shared with
    another test. Helpers that keep scratch state keep it per thread (`TestMarkers`: a `MarkerGenerator` and its bytes are
    `[ThreadStatic]`).
  - **What can not run at once is `[NonParallelizable]` with the reason in a comment** (NUnit runs those after the parallel
    ones, one at a time): tests that set environment variables, start a browser (`HeadlessBrowserTests`, `Png_IsTwiceTheSize`)
    or share Perfetto's trace processor and `TraceLoader`'s cache. A project that is all of that sets `ParallelTests` to `false`
    (the TraceJoin tests).
  - **A test clip is imported once for a test run** (`VideoClips.Imported(clip, ffmpeg, directory)`): the import (ffmpeg, a
    marker decoded in every frame) is the cost, so the tests of a clip get a copy of one capture, each in its own folder, as
    an analysis writes its output next to the capture. Only a test of the import itself calls `VideoClips.Import`.
  - After adding a test that touches files, processes or statics, run the suite a few times in a row: a sharing fault shows
    as a test that fails now and then.
  - **The category `slow` is what CI runs on one system only** (Ubuntu; the user, 2026-10-14, to see whether that is enough
    before capture data is stored for the tests): the tests that import every test clip through ffmpeg (`VideoClipTests`,
    `ChartVideoClipTests`) and the simulated cameras (`CameraEndToEndTests`, `FfmpegCameraTests`, `CameraCalibratorTests`,
    `SyntheticCameraTests`, the monitor rates of `RefreshGridTests`). Windows and macOS run `--filter "TestCategory!=slow"`,
    which keeps the import tests (`FfmpegImportTests`, `PlaybackPageTests`). Locally, and in mb-quality, everything runs. A new
    test that takes seconds gets the category with its reason; a new test of how the tools behave on a system does not.
- **Verify the GUI without touching the desktop:** `dotnet run --project measure/tools/DocImages -c Release -- <scratch dir>` renders
  every page offscreen (Avalonia.Headless), imports and analyses a test clip, and runs the synthetic camera; compare the images with
  `measure/doc/images` (live numbers on the camera capture page vary). `--output-root` and DocImages (`Program.Automation`) runs never
  load or save the user's GUI settings. Do not take screenshots.

## Topic notes

Read the file for the area a task touches before changing it; each holds that area's rules and the user's decisions.

| File                                | Read before touching                                                               |
| ----------------------------------- | ---------------------------------------------------------------------------------- |
| `doc/claude/cpp.md`                 | C++ code, CMake, clang-format/tidy, C++17, size probes, QR encoder, benchmarks     |
| `doc/claude/sdk-and-data.md`        | the core types, the data modules, file formats, typed times, CSV code              |
| `doc/claude/markers.md`             | the marker payload, geometry, drawing, reference shaders, the golden set           |
| `doc/claude/pacer.md`               | the pacer module, its docs and capture sessions (experimental)                     |
| `doc/claude/perfetto-trace.md`      | the trace module, the trace join, Perfetto scripts, trace processor                |
| `doc/claude/capture.md`             | capture data, live capture, playback reports, media sources, marker location       |
| `doc/claude/analysis-and-charts.md` | refresh rate and pacing analysis, the report cards and charts, frame stages charts |
| `doc/claude/camera.md`              | camera capture (very experimental), its benchmarks and rate table                  |
| `doc/claude/ci-and-release.md`      | CI workflows, the Unity package, the release archive, Conan, semantic versions     |

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
- **No X11 macro names in public C++ headers:** `Xlib.h` and `X.h` define plain words as macros (`None`, `Status`, `Bool`, `True`,
  `False`, `Success`, `Always`, `Above`, `Below`, `Complex`, ...), and Vulkan's and EGL's platform headers pull them in on Linux, so
  an enumerator `None` or a member `Status` does not compile in an application that includes them first. Every module's tests compile
  all its public headers after those macros (`mb_framepacing_add_x11_macro_check`, `sdk/cpp/testing/X11Macros.cpp.in`). The names are
  the same in every language (agreed with the user, although .NET names a flags enum's zero `None`): `MarkerFlags::NoFlags`
  (Python `NO_FLAGS`), `SwapIntervalChange::Unchanged`, the data rows' `CaptureStatus` (Python `capture_status`). Files keep their
  words: the CSV column is `status`, the pacer's golden files say `None`.
- **.NET:**
  - hand-maintained SDK csproj files;
  - package versions only in the root `Directory.Packages.props`;
  - C# style: a boxed file header, 2-space indent, block namespaces, `m_`/`g_` field prefixes, CSharpier (`.csharpierrc`, width 150).
  - SDK value types are `readonly struct`s with `public readonly` fields (derived values are expression-bodied properties) and
    `GetHashCode` through `HashCode.Combine` (a `HashCode` builder past 8 fields, `Payload`).
- **C++:** CMake 4.0+, C++20 (C++17 for every module but data with `MB_FRAMEPACING_CXX_STANDARD=17`), warnings as errors, no allocations in the per-frame path, qrcodegen (C variant) vendored as the QR encoder's reference (tests and benchmarks only), GoogleTest
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
    checks both (CI `lint`), `--fix` adds missing ones (the copyright with the current year). It also reports a source file that holds a NUL
    byte (git treats such a file as binary: no diffs, skipped by text searches): write `'\0'`. Third-party code (`third_party/`) keeps its own notices; each language's marker module keeps it in a `third_party/`
    folder of its own (`sdk/cpp/marker/reference/third_party/`, `sdk/python/mb_framepacing/marker/third_party/`; the Python one has its
    license text next to it, the C++ one's is in the files' headers and in `licenses/`).
  - A new file belongs to the license of its path. Moving code across that line (for example from `measure/` into `sdk/`)
    changes its license: only Mana Battery can decide that.
- **Licenses:** every third-party component (vendored, NuGet, FetchContent, test-only) needs its license text in `licenses/` and a
  row in `licenses/README.md`, in the same change.
- **Two counters:** the capture index (capture card) and the marker frame index (application) are unrelated; never compare them.
- **Photosensitivity:** the marker is a high-contrast pattern that changes every frame, so every guide that tells someone to draw it
  carries the warning (the README's workflow step 1, `integrating.md`, `unity.md`, every SDK README: `sdk/`, `cpp`, `csharp/marker`,
  `python`, `unity`, `shaders`; a pointer in `marker-format.md` "Sizing"), and `integrating.md` "Photosensitivity" has the numbers.
  `FlashGuideline` (MarkerDecoding) is WCAG 2.3.1's estimate: a quarter of a 10° field may flash (2.8 % of the screen), a fine
  pattern with squares under 0.1° (1/300 of the width) is exempt. The library default (6 px on 1080p) is at its edge; `marker-size`
  prints the numbers and says when a setup is outside. Never describe the marker as safe: the guideline lowers the risk. A new
  guide or sample that draws the marker gets the warning too.
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
