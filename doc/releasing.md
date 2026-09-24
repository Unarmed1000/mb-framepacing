# Releasing

The repository has two release streams with their own versions ([semantic versioning](https://semver.org)):

| Stream           | Version file      | Tag             | What gets published                                                            |
| ---------------- | ----------------- | --------------- | ------------------------------------------------------------------------------ |
| Marker libraries | `marker/VERSION`  | `marker-v1.2.3` | C++ source archives on a GitHub Release, the Unity package on the `upm` branch |
| Tools            | `measure/VERSION` | `tools-v1.2.3`  | Self-contained executables as build artifacts of the CI run                    |

The marker libraries (C++, C# and the Unity package) share one version because they implement the same marker format and API.

## Marker libraries

`marker/VERSION` is the version of the **next** release. Raise it in the change that alters the API, not only when you release:
CI (`tools/check_semver.py`, the `semver` job) compares the public API of the C# library `MB.FrameMarker` with the last `marker-v*`
release and fails when the version does not cover the change. The C++ API mirrors the C# API, so this guards both.

| Change since the last release                     | 0.x             | from 1.0        |
| ------------------------------------------------- | --------------- | --------------- |
| Breaking (removed or changed API, renamed params) | raise the minor | raise the major |
| Additions only                                    | raise the minor | raise the minor |
| No API change (fixes)                             | raise the patch | raise the patch |

Run the check locally with `dotnet tool restore` and `python tools/check_semver.py` (it needs the release tags: `git fetch --tags`).

1. Make sure `marker/VERSION` is the version to release (for example `0.2.0`).
2. Before a release, check the Unity package in a real editor (CI cannot run Unity):

   ```sh
   python marker/unity/check_in_unity.py
   ```

3. Commit, tag and push the tag:

   ```sh
   git commit -am "Marker libraries 0.2.0"
   git tag marker-v0.2.0
   git push origin master marker-v0.2.0
   ```

The workflow `.github/workflows/release-marker.yml` then:

1. **Checks the tag** is semantic versioning and matches `marker/VERSION`, and that the version covers the API changes since the
   previous release.
2. **Tests** the C++ and C# libraries on Windows, Ubuntu and macOS, and builds the CMake consumer project in every documented way.
3. **Packs** `mb-framemarker-cpp-<version>.tar.gz` and `.zip` plus `SHA256SUMS`, then extracts the archive, builds it, runs its
   tests and consumes it through FetchContent before anything is published.
4. **Creates the GitHub Release** with the archives and the FetchContent and Unity snippets (with the real hash).
5. **Publishes the Unity package** on the `upm` branch, tagged `upm/v<version>`.

Nothing is published if any step fails. To try the packaging locally:

```sh
python marker/cpp/package_release.py --output dist --verify
python marker/unity/build_upm.py --output dist/upm --check
```

## Tools

1. Update `measure/VERSION`, commit, then tag `tools-v<version>` and push the tag.
2. The CI workflow checks the tag against `measure/VERSION` and builds the self-contained executables for Windows, Ubuntu and
   macOS as downloadable artifacts of the run (kept 30 days).
