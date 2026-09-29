# Releasing

The repository has three release streams with their own versions ([semantic versioning](https://semver.org)):

| Stream           | Version file         | Tag             | What gets published                                                            |
| ---------------- | -------------------- | --------------- | ------------------------------------------------------------------------------ |
| Marker libraries | `sdk/marker/VERSION` | `marker-v1.2.3` | C++ source archives on a GitHub Release, the Unity package on the `upm` branch |
| Data libraries   | `sdk/data/VERSION`   | `data-v1.2.3`   | C++ source archives on a GitHub Release (C# and Python: the source at the tag) |
| Tools            | `measure/VERSION`    | `tools-v1.2.3`  | Self-contained executables as build artifacts of the CI run                    |

The marker libraries (C++, C# and the Unity package) share one version because they implement the same marker format and API.

## Pre-releases

A version may carry a pre-release: `-alpha.N`, `-beta.N` or `-rc.N` (N from 1), in the VERSION file and the tag alike
(`marker-v0.2.0-beta.1`). They sort as semantic versioning says: `0.2.0-alpha.1` < `0.2.0-alpha.2` < `0.2.0-beta.1` < `0.2.0-rc.1` <
`0.2.0`. The release runs as any other and its GitHub Release is marked as a pre-release. Each language spells it as its own tools
expect:

| Where                                    | `0.2.0-beta.1` is                                                                          |
| ---------------------------------------- | ------------------------------------------------------------------------------------------ |
| C++ (`Version.hpp`, `GetLibraryVersion`) | `VersionString` `"0.2.0-beta.1"`, `VersionPrerelease` `"beta.1"`, numbers 0, 2, 0          |
| CMake package                            | version 0.2.0: `find_package(mb_framemarker 0.2)` also accepts its pre-releases            |
| .NET                                     | `VersionPrefix` 0.2.0, `VersionSuffix` beta.1 (NuGet's `0.2.0-beta.1`)                     |
| Python (`pyproject.toml`, `__version__`) | `0.2.0b1` (PEP 440: `a`, `b`, `rc`); write it there by hand, a test checks it              |
| Unity package, Conan                     | `0.2.0-beta.1`; Conan version ranges skip pre-releases unless asked (`include_prerelease`) |

The API check (`tools/check_semver.py`) compares with the newest **stable** release: the pre-releases of a version may change its API
among themselves, and the version must cover the changes since the last stable release. A VERSION file is never lower than the newest
tag of its stream, pre-releases included.

## Marker libraries

`sdk/marker/VERSION` is the version of the **next** release. Raise it in the change that alters the API, not only when you release:
CI (`tools/check_semver.py`, the `semver` job) compares the public API of the C# library `MB.FrameMarker` with the last `marker-v*`
release and fails when the version does not cover the change. The C++ API mirrors the C# API, so this guards both.

| Change since the last release                     | 0.x             | from 1.0        |
| ------------------------------------------------- | --------------- | --------------- |
| Breaking (removed or changed API, renamed params) | raise the minor | raise the major |
| Additions only                                    | raise the minor | raise the minor |
| No API change (fixes)                             | raise the patch | raise the patch |

Run the check locally with `dotnet tool restore` and `python tools/check_semver.py` (it needs the release tags: `git fetch --tags`).

1. Make sure `sdk/marker/VERSION` is the version to release (for example `0.2.0`).
2. Before a release, check the Unity package in a real editor (CI cannot run Unity):

   ```sh
   python sdk/marker/unity/check_in_unity.py
   ```

3. Commit, tag and push the tag:

   ```sh
   git commit -am "Marker libraries 0.2.0"
   git tag marker-v0.2.0
   git push origin master marker-v0.2.0
   ```

The workflow `.github/workflows/release-marker.yml` then:

1. **Checks the tag** is semantic versioning and matches `sdk/marker/VERSION`, and that the version covers the API changes since the
   previous release.
2. **Tests** the C++ and C# libraries on Windows, Ubuntu and macOS, and builds the CMake consumer project in every documented way.
3. **Packs** `mb-framemarker-cpp-<version>.tar.gz` and `.zip` plus `SHA256SUMS`, then extracts the archive, builds it, runs its
   tests and consumes it through FetchContent before anything is published.
4. **Creates the GitHub Release** with the archives and the FetchContent and Unity snippets (with the real hash).
5. **Publishes the Unity package** on the `upm` branch, tagged `upm/v<version>`.
6. **Builds the Conan recipe** from the published archive on Windows, Ubuntu and macOS (see [Conan](#conan)).

Nothing is published if any step fails. To try the packaging locally:

```sh
python sdk/marker/cpp/package_release.py --output dist --verify
python sdk/marker/unity/build_upm.py --output dist/upm --check
```

## Data libraries

The data libraries (C#, Python and C++) read the tools' data (`captures.mbcd` and the analysis output) and share one version,
`sdk/data/VERSION`, the version of the **next** release: raise it in the change that alters the API, as for the marker libraries.
`tools/check_semver.py` compares the public API of the C# library `MB.FramePacing.Data` with the last `data-v*` release. A change to the
data formats themselves is a format version (see [the analysis output format](../sdk/doc/analysis-output-format.md)), not only a library version.

1. Make sure `sdk/data/VERSION` is the version to release.
2. Commit, tag and push the tag:

   ```sh
   git commit -am "Data libraries 0.2.0"
   git tag data-v0.2.0
   git push origin master data-v0.2.0
   ```

The workflow `.github/workflows/release-data.yml` then:

1. **Checks the tag** is semantic versioning and matches `sdk/data/VERSION`, and that the version covers the API changes since the
   previous release.
2. **Tests** the C++, C# and Python libraries on Windows, Ubuntu and macOS against the golden data.
3. **Packs** `mb-framepacingdata-cpp-<version>.tar.gz` and `.zip` plus `SHA256SUMS` (with the golden data, so the archive tests
   itself), then installs this checkout's marker library, and builds, tests and installs the extracted archive against it, and builds a
   consumer through `find_package` and one through FetchContent before anything is published.
4. **Creates the GitHub Release** with the archives and the C++, Python and C# snippets (with the real hash).
5. **Builds the Conan recipe** from the published archive on Windows, Ubuntu and macOS (see [Conan](#conan)). Its marker library comes
   from the recipes in the tagged commit, so commit the marker release's Conan version before tagging the data libraries.

The C++ library needs the marker library; a release of the data libraries names the marker version it needs (`sdk/data/cpp/CMakeLists.txt`,
`MB_FRAMEPACINGDATA_MARKER_VERSION`). To try the packaging locally:

```sh
python sdk/data/cpp/package_release.py --output dist --verify
```

## Conan

The Conan 2 recipes (`sdk/conan/recipes/mb-framemarker` and `mb-framepacingdata`) are laid out as conan-center-index is, so a checkout
works as a `local-recipes-index` remote. Each version builds its release archive, so a version is added **after** its release: the
release workflow tests it through the recipe without committing it; to publish it, add it and commit:

```sh
python tools/add_conan_version.py marker 0.2.0     # or: data 0.2.0; reads the release's SHA256SUMS
git commit -am "Conan: mb-framemarker 0.2.0"
```

`python tools/check_conan.py` builds both recipes from this checkout's sources (CI runs it on every push); `--released` uses the
recipes' own versions and archives. Both run in a temporary Conan home, never the user's cache.

**ConanCenter, later:** fork conan-center-index, copy `sdk/conan/recipes/<name>` to its `recipes/<name>` (mb-framemarker first: the
data recipe needs it), and open a pull request per library and version. The first one needs the Contributor License Agreement. Their
build service builds and publishes the binaries after a manual review; a new version is a pull request that adds it to `config.yml` and
`conandata.yml`.

## Tools

1. Update `measure/VERSION`, commit, then tag `tools-v<version>` and push the tag.
2. The CI workflow checks the tag against `measure/VERSION` and builds the self-contained executables for Windows, Ubuntu and
   macOS as downloadable artifacts of the run (kept 30 days).
