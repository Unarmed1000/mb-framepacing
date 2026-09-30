#!/usr/bin/env python3
# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""Check the semantic versions of the two release streams (see doc/releasing.md).

1. sdk/VERSION and measure/VERSION are MAJOR.MINOR.PATCH, optionally with a pre-release (-alpha.N, -beta.N or -rc.N), and never lower
   than the newest release tag of their stream (sdk-v*, tools-v*), pre-releases included, in semantic version order:
   0.2.0-alpha.1 < 0.2.0-alpha.2 < 0.2.0-beta.1 < 0.2.0-rc.1 < 0.2.0.
2. The public API of every C# module of the SDK (MB.FramePacing, MB.FramePacing.Marker, MB.FramePacing.Data,
   MB.FramePacing.Pacer) is compared with the newest stable sdk-v*
   release (Microsoft's ApiCompat, from the local tool manifest: dotnet tool restore). Pre-releases are not a baseline: the pre-releases
   of a version may change its API among themselves. The C++ marker API mirrors the C# one, so this also guards the C++ marker module.
   - A breaking change needs a new major version (a new minor version while the major version is 0).
   - Any other API change (an addition) needs at least a new minor version.
   Without a release tag of the stream there is nothing to compare with, and its API check is skipped; a module the baseline release does
   not have yet (a new one) has nothing to compare with either. A tag on the checked out commit itself (the release run of that tag) is not
   a baseline; the release before it is.

sdk/VERSION is the version of the next SDK release, so raise it in the same change that alters the API.

Run from anywhere inside the repository (needs the release tags: git fetch --tags):
  python tools/check_semver.py
Exits with 1 and explains what to raise when a check fails.
"""

import os
import re
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path

SEMVER = re.compile(r"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-(alpha|beta|rc)\.([1-9][0-9]*))?$")
# A pre-release sorts before its release, and alpha before beta before rc
PRERELEASE_LABELS = ("alpha", "beta", "rc")


@dataclass(frozen=True)
class ApiStream:
    """A release stream whose C# library's public API is compared with its last release."""

    library: str
    project: Path
    version_file: str
    tag_prefix: str

    @property
    def assembly(self) -> str:
        return self.library + ".dll"


# The SDK's C# modules: one version and one release for all of them
API_STREAMS = (
    ApiStream("MB.FramePacing", Path("sdk/csharp/core/MB.FramePacing.csproj"), "sdk/VERSION", "sdk-v"),
    ApiStream("MB.FramePacing.Marker", Path("sdk/csharp/marker/MB.FramePacing.Marker.csproj"), "sdk/VERSION", "sdk-v"),
    ApiStream("MB.FramePacing.Data", Path("sdk/csharp/data/MB.FramePacing.Data.csproj"), "sdk/VERSION", "sdk-v"),
    ApiStream("MB.FramePacing.Pacer", Path("sdk/csharp/pacer/MB.FramePacing.Pacer.csproj"), "sdk/VERSION", "sdk-v"),
)

# (major, minor, patch, 1 for a release or 0 for a pre-release, the pre-release label's rank, its number): sorts as semver does
Version = tuple[int, int, int, int, int, int]


def error(message: str) -> None:
    # GitHub Actions shows ::error:: lines as annotations on the run
    prefix = "::error::" if os.environ.get("GITHUB_ACTIONS") else "error: "
    print(prefix + message)


def notice(message: str) -> None:
    prefix = "::notice::" if os.environ.get("GITHUB_ACTIONS") else ""
    print(prefix + message)


def parse(text: str) -> Version | None:
    match = SEMVER.match(text)
    if match is None:
        return None
    numbers = (int(match.group(1)), int(match.group(2)), int(match.group(3)))
    if match.group(4) is None:
        return (*numbers, 1, 0, 0)
    return (*numbers, 0, PRERELEASE_LABELS.index(match.group(4)), int(match.group(5)))


def is_prerelease(version: Version) -> bool:
    return version[3] == 0


def show(version: Version) -> str:
    numbers = f"{version[0]}.{version[1]}.{version[2]}"
    return f"{numbers}-{PRERELEASE_LABELS[version[4]]}.{version[5]}" if is_prerelease(version) else numbers


def git(root: Path, *args: str) -> str:
    return subprocess.run(["git", *args], cwd=root, capture_output=True, text=True, check=True).stdout


def newest_tag(root: Path, prefix: str, skip: frozenset[str], stable_only: bool = False) -> tuple[str, Version] | None:
    newest: tuple[str, Version] | None = None
    for tag in git(root, "tag", "--list", prefix + "*").splitlines():
        version = parse(tag.removeprefix(prefix))
        if version is None or tag in skip or (stable_only and is_prerelease(version)):
            continue
        if newest is None or version > newest[1]:
            newest = (tag, version)
    return newest


def check_stream(root: Path, version_file: str, prefix: str) -> Version | None:
    """Checks the VERSION file of one stream; returns its version when it is valid."""
    text = (root / version_file).read_text(encoding="utf-8").strip()
    version = parse(text)
    if version is None:
        error(f"{version_file} is '{text}', expected MAJOR.MINOR.PATCH, optionally with -alpha.N, -beta.N or -rc.N (for example 1.2.3 or 1.2.3-beta.1)")
        return None
    newest = newest_tag(root, prefix, frozenset())
    if newest is not None and version < newest[1]:
        error(f"{version_file} is {text}, lower than the released {newest[0]}")
        return None
    print(f"{version_file}: {text}" + (f" (newest release {newest[0]})" if newest else " (no release yet)"))
    return version


def build_library(stream: ApiStream, source_root: Path, output: Path) -> Path | None:
    project = source_root / stream.project
    result = subprocess.run(["dotnet", "build", str(project), "-c", "Release", "-o", str(output), "--nologo", "-v", "quiet"], cwd=source_root)
    if result.returncode != 0:
        error(f"building {project} failed")
        return None
    return output / stream.assembly


def api_compat(root: Path, baseline: Path, current: Path, strict: bool) -> tuple[bool, str] | None:
    """Runs ApiCompat; returns (compatible, report), or None when ApiCompat itself failed."""
    # CP0003 (assembly version changed) comes with every version bump, so it is not an API change
    command = ["dotnet", "apicompat", "-l", str(baseline), "-r", str(current), "--enable-rule-cannot-change-parameter-name", "--noWarn", "CP0003"]
    if strict:
        command.append("--strict-mode")
    result = subprocess.run(command, cwd=root, capture_output=True, text=True)
    report = (result.stdout + result.stderr).strip()
    # Incompatibilities are reported as CPnnnn diagnostics; any other failure (tool not restored, bad input) is not an API verdict
    if result.returncode != 0 and not re.search(r"\bCP\d{4}\b", report):
        print(report)
        error("ApiCompat failed (run 'dotnet tool restore' first)")
        return None
    return (result.returncode == 0, report)


def check_api(root: Path, stream: ApiStream, version: Version) -> bool:
    # The release run checks out the new tag itself; compare with the release before it
    at_head = frozenset(git(root, "tag", "--points-at", "HEAD").splitlines())
    newest = newest_tag(root, stream.tag_prefix, at_head, stable_only=True)
    if newest is None:
        notice(f"No stable {stream.tag_prefix}* release yet, so there is no {stream.library} API to compare with")
        return True
    tag, released = newest

    with tempfile.TemporaryDirectory(prefix="mb-semver-") as temp:
        worktree = Path(temp) / "baseline"
        _ = git(root, "worktree", "add", "--detach", str(worktree), tag)
        try:
            if not (worktree / stream.project).is_file():
                notice(f"{tag} has no {stream.library} ({stream.project}), so there is no API to compare with")
                return True
            baseline = build_library(stream, worktree, Path(temp) / "baseline-bin")
        finally:
            _ = git(root, "worktree", "remove", "--force", str(worktree))
        current = build_library(stream, root, Path(temp) / "current-bin")
        if baseline is None or current is None:
            return False

        breaking = api_compat(root, baseline, current, strict=False)
        changes = api_compat(root, baseline, current, strict=True)
        if breaking is None or changes is None:
            return False
        compatible, breaking_report = breaking
        unchanged, changes_report = changes

    raised_major = version[0] > released[0]
    raised_minor = version[:2] > released[:2]
    if not compatible:
        # In 0.x a new minor version may break the API (semver: anything may change before 1.0)
        allowed = raised_minor if released[0] == 0 else raised_major
        if not allowed:
            needed = f"{released[0]}.{released[1] + 1}.0" if released[0] == 0 else f"{released[0] + 1}.0.0"
            print(breaking_report)
            error(f"{stream.library} has breaking API changes since {tag}: raise {stream.version_file} to at least {needed}")
            return False
        notice(f"{stream.library} has breaking API changes since {tag}, covered by {stream.version_file} {show(version)}")
        return True
    if not unchanged:
        if not raised_minor:
            print(changes_report)
            error(f"{stream.library} has API additions since {tag}: raise {stream.version_file} to at least {released[0]}.{released[1] + 1}.0")
            return False
        notice(f"{stream.library} has API additions since {tag}, covered by {stream.version_file} {show(version)}")
        return True
    print(f"{stream.library}: public API unchanged since {tag}")
    return True


def main() -> int:
    root = Path(git(Path.cwd(), "rev-parse", "--show-toplevel").strip())
    sdk = check_stream(root, "sdk/VERSION", "sdk-v")
    tools = check_stream(root, "measure/VERSION", "tools-v")
    if sdk is None or tools is None:
        return 1
    results = [check_api(root, stream, sdk) for stream in API_STREAMS]
    return 0 if all(results) else 1


if __name__ == "__main__":
    sys.exit(main())
