# mb-framepacing: notes for Claude

Frame pacing / animation error measurement: a C++20 marker library (`cpp/`) draws a QR marker into every frame; the .NET tools
(`dotnet/`) record a capture card through ffmpeg and analyse the markers. See `README.md` for the overview and
`doc/marker-format.md` for the marker specification. **The document is the reference**: C++ (`cpp/src/Payload.cpp`) and C#
(`MarkerPayload.cs`) must match it byte for byte.

## Build and test

```
mb-quality -r --all dotnet                       # the standard check: dotnet format + CSharpier + build + tests + vulnerable packages
mb-quality -r --repair dotnet                    # apply formatting, then build and test
dotnet build dotnet/mb-framepacing.slnx          # warnings are errors (Directory.Build.props)
dotnet test  dotnet/mb-framepacing.slnx
cmake --preset windows && cmake --build --preset windows && ctest --preset windows    # linux / linux-clang / macos presets too
dotnet run --project dotnet/app/FramePacing/FramePacing.csproj -- selftest --fps 500  # end to end without hardware
```

- mb-quality lives in `../mb-quality` (`mb-quality.cmd`); config in `dotnet/.mb-quality.json`. It only checks folders that
  contain a solution and takes the `*.csproj` files next to it, so **every project folder has its own one-line `.slnx`**
  (keep them when adding a project; `dotnet/mb-framepacing.slnx` is the IDE solution). A run that reports "0 projects" means
  a project folder is missing its `.slnx`.
- C++ follows `cpp/.clang-format` and `cpp/.clang-tidy` (namespaces are CamelCase: `MB::FrameMarker`). Run both on our sources
  only (never `third_party/`); the VS generator writes no compile database, so pass the flags to clang-tidy directly:
  ```
  cd cpp
  clang-format -i include/mb/framemarker/FrameMarker.hpp src/*.cpp tests/*.cpp tools/marker-render/main.cpp
  clang-tidy --quiet --header-filter=".*mb/framemarker/.*" src/FrameMarker.cpp src/Payload.cpp tools/marker-render/main.cpp tests/FrameMarkerTests.cpp -- -std=c++20 -Iinclude -Ibuild/windows/include -Ithird_party/qrcodegen -Ibuild/windows/_deps/googletest-src/googletest/include -DMB_FRAMEMARKER_EXPECTED_VERSION=\"0.1.0\"
  ```
  `cmake/Version.hpp.in` is guarded with `// clang-format off` because formatting breaks its `@VAR@` placeholders.
- Docs: `npm install && npm run format` (Prettier: Markdown/JSON/YAML; config `.prettierrc.json`, ignores in `.prettierignore`).
  Regenerate the README images with `dotnet run --project dotnet/tools/DocImages` - it renders the real GUI **offscreen**
  (Avalonia.Headless) in no-save mode and neutralises machine specific text; never take desktop screenshots.
- Sources other than capture cards (`mb-framepacing import`, GUI "Video file / Image folder / Network stream") go through
  `MediaInput` -> ffmpeg. Image sequences get their exact times from `--fps` / the timestamp CSV (`FrameTimestamps`), not from
  ffmpeg (its concat timestamps are 40 ms coarse). Non-live sources make the recorder wait instead of dropping (`IsLive`).
- ffmpeg end-to-end tests (`FfmpegImportTests`, category `ffmpeg`) are skipped when no ffmpeg is found; CI installs ffmpeg.
- CI: `.github/workflows/ci.yml` builds and tests C++ and .NET on Windows, Ubuntu and macOS and checks formatting.
- If you change the marker payload or geometry, regenerate the golden set with
  `cpp/build/<preset>/Release/marker-render --golden test-data/markers` (Windows: `...\Release\marker-render.exe`) and run the C# tests.
- This machine has a `Platform=x64` environment variable: building a single project writes to `bin/x64/...`, while building
  the `.slnx` writes to `bin/...`. `dotnet run --no-build` after a solution build can therefore run a stale binary. Build the
  project you run.
- Verify the GUI without touching the desktop: `mb-framepacing-gui --demo --output-root <dir>` captures and analyses the synthetic
  game and writes the reports to disk (demo and `--output-root` runs never save settings). Do not take screenshots.

## Conventions

- **Version:** only in the root `VERSION` file (CMake reads it into `Version.hpp`; `dotnet/Directory.Build.props` reads it too).
- **.NET:** hand-maintained SDK csproj files in `dotnet/mb-framepacing.slnx` (no FslBuild or MB.gen), with package versions only in
  `dotnet/Directory.Packages.props`. C# style follows the sibling mb-tools repos: a boxed file header, 2-space indent,
  block namespaces, `m_`/`g_` field prefixes, CSharpier (`.csharpierrc`, width 150).
- **C++:** CMake 4.0+, C++20, warnings as errors, no allocations in the per-frame path, qrcodegen (C variant) vendored,
  GoogleTest through FetchContent (`FIND_PACKAGE_ARGS` lets an installed or Conan GTest win).
- **Licenses:** every third-party component (vendored, NuGet, FetchContent, test-only) needs its license text in `licenses/` and a
  row in `licenses/README.md`, in the same change.
- **Two counters:** the capture index (capture card) and the marker frame index (application) are unrelated; never compare them.
- **ffmpeg** is an external executable found via `--ffmpeg` / `MB_FFMPEG` / `mb-framepacing.json` / PATH / install folders
  (`FfmpegLocator`). It is never linked or bundled.
