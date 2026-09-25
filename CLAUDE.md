# mb-framepacing: notes for Claude

Frame pacing / animation error measurement.

- **`marker/`** holds what goes **into** the application: the marker libraries, which draw a QR marker into every frame.
  They are the C++20 library (`marker/cpp/`), the general C# library (`marker/csharp/`) and the Unity package (`marker/unity/`).
- **`measure/`** holds the .NET tools that **measure**: they record a capture card through ffmpeg and analyse the markers.

See `README.md` for the overview and `doc/marker-format.md` for the marker specification. **The document is the reference**: C++
(`marker/cpp/src/Payload.cpp`) and C# (`marker/csharp/source/Marker.cs`) must match it byte for byte. The tools'
`MarkerPayload` delegates to the C# library; ZXing is only used for decoding.

## Layout

| Path                                              | Contents                                                                                     |
| ------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| `marker/VERSION`                                  | Version of the marker libraries (released with `marker-v*` tags)                             |
| `marker/cpp/`                                     | C++20 library, `marker-render` tool, GoogleTest tests, CMake presets                         |
| `marker/csharp/`                                  | General C# library `MB.FrameMarker` (.NET Standard 2.0, C# 9, no dependencies) + NUnit tests |
| `marker/unity/`                                   | Unity package sources (helpers, samples), `build_upm.py`, `check_in_unity.py`                |
| `measure/VERSION`                                 | Version of the tools (released with `tools-v*` tags)                                         |
| `measure/app/`, `measure/libs/`, `measure/tools/` | CLI, Avalonia GUI, Marker/Capture/Analysis libraries (+ `UnitTest/`), DocImages, Benchmarks  |
| root `Directory.*.props`, `UnitTest.props`        | Shared .NET build settings (C# projects only; see below), central package versions           |
| `mb-framepacing.slnx`                             | IDE solution with every .NET project                                                         |
| `doc/`, `test-data/markers/`, `licenses/`         | Docs and images, golden marker images from the C++ library, third-party licenses             |

## Build and test

```
mb-quality -r --all .                            # the standard check: dotnet format + CSharpier + build + tests + vulnerable packages
mb-quality -r --repair .                         # apply formatting, then build and test
dotnet build mb-framepacing.slnx                 # warnings are errors (Directory.Build.props)
dotnet test  mb-framepacing.slnx
cd marker/cpp && cmake --preset windows && cmake --build --preset windows && ctest --preset windows   # linux / linux-clang / macos too
dotnet run --project measure/app/FramePacing/FramePacing.csproj -- selftest --fps 500  # end to end without hardware
```

- **mb-quality**
  - It lives in `../mb-quality` (`mb-quality.cmd`); its config is the root `.mb-quality.json`, and the CSharpier tool manifest is
    `.config/dotnet-tools.json`.
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
  - C++ follows `marker/cpp/.clang-format` and `marker/cpp/.clang-tidy` (namespaces are CamelCase: `MB::FrameMarker`).
  - `python tools/check_cpp.py` runs both on our sources only (never `third_party/`), with the versions CI pins in
    `requirements-dev.txt`. clang-tidy needs a configured build: the default is `marker/cpp/build/windows` (the VS generator
    writes no compile database, so the script passes the include paths); `--build-dir` takes another one. To apply formatting:
    `clang-format -i` on the files the script lists.
  - Clang's `-Wconversion` includes `-Wsign-conversion` (GCC's and MSVC's do not), so macOS CI can fail where Windows and Linux
    pass: shift and combine small unsigned types after casting them to `uint32_t`.
  - The `linux-sanitize` preset (Clang, AddressSanitizer + UndefinedBehaviorSanitizer, compile database) is what CI runs the tests
    and clang-tidy with.
  - `cmake/Version.hpp.in` is guarded with `// clang-format off`, because formatting breaks its `@VAR@` placeholders.
- **Python scripts** (`measure/build_standalone.py`, `tools/`, later `marker/unity/build_upm.py`): standard library only. They must pass
  `ruff check .`, `ruff format --check .` and `basedpyright` (config: `ruff.toml`, `pyrightconfig.json`, recommended mode; tools
  pinned in `requirements-dev.txt`, installed with `python -m pip install -r requirements-dev.txt`). CI runs all three.
- **Unity package** (`com.manabattery.framemarker`):
  - It isn't stored as one folder: `marker/unity/build_upm.py` assembles it from `marker/csharp/source` (core) plus
    `marker/unity/Runtime/Unity` (helpers, all wrapped in `#if UNITY_2021_3_OR_NEWER`), and generates `.meta` files with stable GUIDs.
  - CI runs it with `--check`.
  - `marker/unity/check_in_unity.py` verifies it in a real Unity editor in batch mode; Unity editors are installed on this machine
    under `C:/Program Files/Unity/Hub/Editor`. Run it after changing the core or the helpers.
  - The core must stay C# 9 / .NET Standard 2.0 without UnityEngine.
- **Standalone release archive:** `marker/cpp/CMakeLists.txt` finds `VERSION`, `LICENSE` and `licenses/` next to itself in a release
  archive, and falls back to `marker/VERSION` and the repository root otherwise.
- **Docs**
  - Formatting: `npm install && npm run format` (Prettier: Markdown/JSON/YAML; config `.prettierrc.json`, ignores in
    `.prettierignore`).
  - Regenerate the README images with `dotnet run --project measure/tools/DocImages`. It renders the real GUI **offscreen**
    (Avalonia.Headless) in no-save mode and neutralises machine specific text. Never take desktop screenshots.
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
- **Camera capture (VERY EXPERIMENTAL, `doc/camera.md`):**
  - Every place users meet it says "very experimental": CLI help, the GUI card, `CameraRig.ExperimentalNotice` in rig files and
    analysis warnings, docs. Keep it that way until it is validated with real hardware, and keep `doc/camera-status.md`
    (status, known issues, next steps) current with every camera change.
  - Saved cameras: `CameraRigLibrary` (`camera-rigs/` next to the config file; demo/automation GUI runs use their output root, never
    the user's library). GUI: `CameraWizardViewModel` + `CameraWizardWindow` (the wizard), `CameraRigViewModel` (the capture page card).
  - `Capture/source/Camera/`: `CameraCalibrator` (calibrate/verify), `CameraRig`/`CameraZone` (the rig file), `CameraRectifier` and
    `RectifyingCaptureSource` (C# path). `FfmpegCommandBuilder.BuildCameraFilter` is the ffmpeg path; both produce the same layout:
    zones of `CameraZone.StoredSizePx` stacked in scanout order.
  - The analysis switches to `ScanoutModel.Camera` when `capture.json` has a `camera` section.
  - The synthetic camera (`Capture/source/Synthetic/SyntheticCamera.cs`) is the ground truth. `selftest --camera --fps 1000
--refresh 60 [--tear-every 9]` runs it end to end.
  - Benchmarks: `dotnet run -c Release --project measure/tools/Benchmarks/Benchmarks.csproj -- --filter "*"`. Name the csproj: the
    folder's `.slnx` does not build the libraries optimized.
- **ffmpeg tests:** the end-to-end tests (`FfmpegImportTests`, category `ffmpeg`) are skipped when no ffmpeg is found; CI installs
  ffmpeg.
- **CI** (mb-quality is not available there; CI runs the same commands directly):
  - `.github/workflows/ci.yml` builds and tests C++, the CMake consumer project and .NET on Windows, Ubuntu and macOS.
  - Its other jobs: `lint` (Prettier, one type per file, ruff, basedpyright, actionlint, the Unity package assembly),
    `dotnet-lint` (dotnet format as mb-quality applies it, CSharpier, vulnerable packages), `cpp-analysis` (sanitizer tests,
    clang-format, clang-tidy) and `semver` (`tools/check_semver.py`).
  - `.github/workflows/release-marker.yml` releases the marker libraries on a `marker-v*` tag (see `doc/releasing.md`). A
    `tools-v*` tag (which must match `measure/VERSION`) also builds the self-contained tools.
  - `.github/dependabot.yml` opens weekly grouped updates for GitHub Actions, NuGet (packages and dotnet tools), npm and pip.
    GoogleTest (FetchContent URL) and qrcodegen (vendored) are updated by hand.
- **Semantic versions:** `python tools/check_semver.py` (after `dotnet tool restore`) checks both VERSION files and compares the
  public API of `MB.FrameMarker` with the last `marker-v*` release using ApiCompat. `marker/VERSION` is the next release's version:
  raise it in the change that alters the API (0.x: minor for any API change; from 1.0: major for breaking changes).
- **Golden set:** if you change the marker payload or geometry, regenerate it with
  `marker/cpp/build/<preset>/Release/marker-render --golden test-data/markers` (Windows: `...\Release\marker-render.exe`), then run
  the C# tests.
- **Verify the GUI without touching the desktop:** `dotnet run --project measure/tools/DocImages -c Release -- <scratch dir>` renders
  every page offscreen (Avalonia.Headless) and runs the synthetic demo capture and analysis; compare the images with `doc/images`
  (live numbers on the capture page vary). `mb-framepacing-gui --demo` is **not** headless: it opens a real window and waits for it to
  be closed. Demo, `--output-root` and DocImages runs never load or save the user's GUI settings. Do not take screenshots.

## Conventions

- **Versions:** there are two version files.
  - `marker/VERSION`: the marker libraries. CMake reads it into `Version.hpp`.
  - `measure/VERSION`: the tools. `measure/Directory.Build.props` reads it.
- **One type per file:** C++ and C# use one class/struct/enum per file (nested private helpers may stay nested). The C++ public API
  has one header per type; `FrameMarker.hpp` includes them all and declares the functions. CI runs `tools/check_one_type_per_file.py`.
- **Hot path:** the marker APIs run every frame, so they must not allocate. Add zero-allocation tests for new API.
- **.NET:**
  - hand-maintained SDK csproj files (no FslBuild or MB.gen);
  - package versions only in the root `Directory.Packages.props`;
  - C# style follows the sibling mb-tools repos: a boxed file header, 2-space indent, block namespaces, `m_`/`g_` field prefixes,
    CSharpier (`.csharpierrc`, width 150).
- **C++:** CMake 4.0+, C++20, warnings as errors, no allocations in the per-frame path, qrcodegen (C variant) vendored, GoogleTest
  through FetchContent (`FIND_PACKAGE_ARGS` lets an installed or Conan GTest win).
- **Licenses:** every third-party component (vendored, NuGet, FetchContent, test-only) needs its license text in `licenses/` and a
  row in `licenses/README.md`, in the same change.
- **Two counters:** the capture index (capture card) and the marker frame index (application) are unrelated; never compare them.
- **ffmpeg** is an external executable, found via `--ffmpeg` / `MB_FFMPEG` / `mb-framepacing.json` / PATH / install folders
  (`FfmpegLocator`). It is never linked or bundled.
