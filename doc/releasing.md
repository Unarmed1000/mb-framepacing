# Releasing

The repository has two release streams with their own versions ([semantic versioning](https://semver.org)):

| Stream | Version file      | Tag            | What gets published                                                                                                   |
| ------ | ----------------- | -------------- | --------------------------------------------------------------------------------------------------------------------- |
| SDK    | `sdk/VERSION`     | `sdk-v1.2.3`   | C++ source archives on a GitHub Release, the Unity package on the `upm` branch (C# and Python: the source at the tag) |
| Tools  | `measure/VERSION` | `tools-v1.2.3` | Self-contained executables as build artifacts of the CI run                                                           |

The SDK (every module, C++, C#, Python and the Unity package) has one version: its modules implement the same formats and are released
together, as Boost's, Qt's and Poco's modules are.

## Pre-releases

A version may carry a pre-release: `-alpha.N`, `-beta.N` or `-rc.N` (N from 1), in the VERSION file and the tag alike
(`sdk-v0.2.0-beta.1`). They sort as semantic versioning says: `0.2.0-alpha.1` < `0.2.0-alpha.2` < `0.2.0-beta.1` < `0.2.0-rc.1` <
`0.2.0`. The release runs as any other and its GitHub Release is marked as a pre-release. Each language spells it as its own tools
expect:

| Where                                    | `0.2.0-beta.1` is                                                                          |
| ---------------------------------------- | ------------------------------------------------------------------------------------------ |
| C++ (`Version.hpp`, `GetLibraryVersion`) | `VersionString` `"0.2.0-beta.1"`, `VersionPrerelease` `"beta.1"`, numbers 0, 2, 0          |
| CMake package                            | version 0.2.0: `find_package(mb_framepacing 0.2)` also accepts its pre-releases            |
| .NET                                     | `VersionPrefix` 0.2.0, `VersionSuffix` beta.1 (NuGet's `0.2.0-beta.1`)                     |
| Python (`pyproject.toml`, `__version__`) | `0.2.0b1` (PEP 440: `a`, `b`, `rc`); write it there by hand, a test checks it              |
| Unity package, Conan                     | `0.2.0-beta.1`; Conan version ranges skip pre-releases unless asked (`include_prerelease`) |

The API check (`tools/check_semver.py`) compares with the newest **stable** release: the pre-releases of a version may change its API
among themselves, and the version must cover the changes since the last stable release. A VERSION file is never lower than the newest
tag of its stream, pre-releases included.

## SDK

`sdk/VERSION` is the version of the **next** release. Raise it in the change that alters the API, not only when you release: CI
(`tools/check_semver.py`, the `semver` job) compares the public API of every C# module (`MB.FramePacing.Marker`,
`MB.FramePacing.Data`) with the last `sdk-v*` release and fails when the version does not cover the change. The C++ marker API mirrors
the C# one, so this guards both. A change to the data formats themselves is a format version (see
[the analysis output format](../sdk/doc/analysis-output-format.md)), not only an SDK version.

| Change since the last release                     | 0.x             | from 1.0        |
| ------------------------------------------------- | --------------- | --------------- |
| Breaking (removed or changed API, renamed params) | raise the minor | raise the major |
| Additions only                                    | raise the minor | raise the minor |
| No API change (fixes)                             | raise the patch | raise the patch |

Run the check locally with `dotnet tool restore` and `python tools/check_semver.py` (it needs the release tags: `git fetch --tags`).

1. Make sure `sdk/VERSION` is the version to release (for example `0.2.0`), and `sdk/python/pyproject.toml` and
   `sdk/python/mb_framepacing/__init__.py` say the same (a test checks them).
2. Before a release, check the Unity package in a real editor (CI cannot run Unity):

   ```sh
   python sdk/unity/check_in_unity.py
   ```

3. Commit, tag and push the tag:

   ```sh
   git commit -am "SDK 0.2.0"
   git tag sdk-v0.2.0
   git push origin master sdk-v0.2.0
   ```

The workflow `.github/workflows/release-sdk.yml` then:

1. **Checks the tag** is semantic versioning and matches `sdk/VERSION`, and that the version covers the API changes since the previous
   release.
2. **Tests** the C++, C# and Python libraries on Windows, Ubuntu and macOS against the golden data, and builds the CMake consumer
   project in every documented way.
3. **Packs** `mb-framepacing-cpp-<version>.tar.gz` and `.zip` plus `SHA256SUMS` (every module, with the golden data, so the archive
   tests itself), then extracts the archive, builds it, runs its tests and consumes it through FetchContent, `add_subdirectory` and
   `find_package` with components before anything is published.
4. **Creates the GitHub Release** with the archives and the C++, Unity, C# and Python snippets (with the real hash).
5. **Publishes the Unity package** on the `upm` branch, tagged `upm/v<version>`.
6. **Builds the Conan recipe** from the published archive on Windows, Ubuntu and macOS (see [Conan](#conan)).

Nothing is published if any step fails. To try the packaging locally:

```sh
python sdk/cpp/package_release.py --output dist --verify
python sdk/unity/build_upm.py --output dist/upm --check
```

## Conan

The Conan 2 recipe (`sdk/cpp/conan/recipes/mb-framepacing`) is laid out as conan-center-index is, so a checkout works as a
`local-recipes-index` remote. Every module is a component (`mb_framepacing::core`, `::marker`, `::data`), and the options `with_marker`
and `with_data` leave modules out. Each version builds its release archive, so a version is added **after** its release: the release
workflow tests it through the recipe without committing it; to publish it, add it and commit:

```sh
python tools/add_conan_version.py 0.2.0     # reads the release's SHA256SUMS
git commit -am "Conan: mb-framepacing 0.2.0"
```

`python tools/check_conan.py` builds the recipe from this checkout's sources, with every module and without the data module (CI runs it
on every push); `--released` uses the recipe's own versions and archives. Both run in a temporary Conan home, never the user's cache.

**ConanCenter, later:** fork conan-center-index, copy `sdk/cpp/conan/recipes/mb-framepacing` to its `recipes/mb-framepacing`, and open
a pull request per version. The first one needs the Contributor License Agreement. Their build service builds and publishes the binaries
after a manual review; a new version is a pull request that adds it to `config.yml` and `conandata.yml`.

## Tools

1. Update `measure/VERSION`, commit, then tag `tools-v<version>` and push the tag.
2. The CI workflow checks the tag against `measure/VERSION` and builds the self-contained executables for Windows, Ubuntu and
   macOS as downloadable artifacts of the run (kept 30 days).
