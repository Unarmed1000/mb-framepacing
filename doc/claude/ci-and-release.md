# CI and release: notes for Claude

Part of the notes for Claude; `CLAUDE.md` holds the rules every task needs.

- **CI** (mb-quality is not available there; CI runs the same commands directly):
  - `.github/workflows/ci.yml` builds and tests C++, the CMake consumer project and .NET on Windows, Ubuntu and macOS. Ubuntu
    runs all of it; Windows and macOS, the slower runners, leave out the .NET tests of the category `slow` and run the consumer
    project one way (`check_consumers.py --package-only`: the installed package with the default modules) instead of every
    documented way.
  - Its other jobs: `lint` (Prettier, one type per file, license headers, ruff, basedpyright, the Python library's tests, the
    reference shaders, actionlint, the Unity package assembly), `dotnet-lint` (dotnet format as mb-quality applies it, CSharpier,
    vulnerable packages, C# coverage, C# assembly sizes), `cpp-analysis` (sanitizer tests, a minute of fuzzing, clang-format,
    clang-tidy), `cpp-size` (MSVC, GCC, Clang, AppleClang), `conan` (Windows, Ubuntu, macOS) and `semver`
    (`tools/check_semver.py`).
  - `.github/workflows/release-sdk.yml` releases the SDK (C++ archive, Unity package, Conan check) on an `sdk-v*` tag (see
    `doc/releasing.md`). A `tools-v*` tag (which must match `measure/VERSION`) also builds the self-contained tools.
  - `.github/dependabot.yml` opens weekly grouped updates for GitHub Actions, NuGet (packages and dotnet tools), npm and uv (the dev tools).
    GoogleTest and nlohmann/json (FetchContent URLs) and qrcodegen (vendored) are updated by hand.
- **Unity package** (`com.manabattery.framepacing`):
  - It isn't stored as one folder: `sdk/unity/build_upm.py` assembles it from `sdk/csharp/core/source` (`Runtime/Core`, asmdef
    `MB.FramePacing`), `sdk/csharp/marker/source` (`Runtime/Marker`, `MB.FramePacing.Marker`) plus `sdk/unity/Runtime/Unity` (helpers,
    all wrapped in `#if UNITY_2021_3_OR_NEWER`), and generates `.meta` files with stable GUIDs.
  - CI runs it with `--check`: every file the package takes from the repository equals its source, and each default reference names a
    file and a field of the package.
  - `sdk/unity/check_in_unity.py` verifies it in a real Unity editor in batch mode (every drawing method pixel exact; `--graphics
glcore|vulkan|d3d12` forces another graphics API); it takes the newest editor Unity Hub installed, or `--unity`. Run it after changing
    the core, the marker module or the helpers. It also checks that `FrameMarkerTexture` keeps a quiet zone in its range and that
    `FrameMarkerOverlay` keeps drawing after a frame's draw threw (it reports the first failure and tries the next frame).
  - The core and the marker module must stay C# 9 / .NET Standard 2.1 without UnityEngine (Unity 2021.2+ has .NET Standard 2.1 in both API levels). Buffer
    APIs take `ReadOnlySpan<T>` for input and `Span<T>` for output, as the C++ library takes `MB::FramePacing::Span` (`std::span` in C++20).
- **Standalone release archive:** `sdk/cpp/CMakeLists.txt` finds `VERSION`, `LICENSE`, `licenses/` and `shaders/` next to itself in a
  release archive, and falls back to `sdk/VERSION`, `sdk/LICENSE`, `sdk/shaders` and the repository root's `licenses/` otherwise. The
  archive (`sdk/cpp/package_release.py`) holds every module, the docs, `test-data/data` and `test-data/pacer`, not `conan/`.
- **Conan recipe (`sdk/cpp/conan`):**
  - `recipes/mb-framepacing/all/` holds `conanfile.py` (a component per module; options `with_marker`, `with_data`, `with_pacer`), `conandata.yml`
    (each version's release archive URL and SHA-256) and `test_package/`; `recipes/mb-framepacing/config.yml` lists the versions. It
    builds the release archive (`package_release.py`), never the checkout.
  - A version is added after its release: `python tools/add_conan_version.py <version>` (reads the release's `SHA256SUMS`), then
    commit. The release workflow tests the new archive through the recipe (`check_conan.py --released`).
  - `tools/check_conan.py` (CI `conan` job, Windows/Ubuntu/macOS) builds the recipe from this checkout's archive, using a copy of
    `sdk/cpp/conan` as a local-recipes-index remote: `conan test` of the test package with `compiler.cppstd=20`, the same with
    `with_pacer=True` (the test package then paces a frame), and a build with the core and marker modules only.
  - **Every Conan run uses a temporary `CONAN_HOME`**: never touch the user's cache (their global Conan may be another version;
    running a newer one migrates the cache). Conan is pinned in `pyproject.toml`'s dev group.
  - basedpyright excludes `sdk/cpp/conan` (Conan's API is untyped); ruff still checks the recipe. The test package's C++ is formatted
    by `check_cpp.py` (with `sdk/cpp/.clang-format`).
- **Semantic versions:** `python tools/check_semver.py` (after `dotnet tool restore`) checks the two VERSION files and compares the
  public API of every C# module (`MB.FramePacing`, `MB.FramePacing.Marker`, `MB.FramePacing.Data`) with the newest stable `sdk-v*`
  release using ApiCompat.
  `sdk/VERSION` is the next release's version: raise it in the change that alters the API (0.x: minor for any API change; from 1.0:
  major for breaking changes).
  - A version may be a pre-release: `-alpha.N`, `-beta.N` or `-rc.N` only (doc/releasing.md "Pre-releases"). CMake splits it off
    (`project()` takes the numbers; `Version.hpp` has `VersionString` and `VersionPrerelease`), .NET makes it the `VersionSuffix`, and
    Python spells it as PEP 440 does (`0.2.0b1`, by hand in `pyproject.toml` and `__version__`; `test_version.py` checks both). The
    API baseline is the newest stable release.
  - CMake reads VERSION on every configure (a normal variable; an edit reconfigures); `-D<NAME>_VERSION_OVERRIDE` builds one tree as
    another version.
