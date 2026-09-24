#!/usr/bin/env python3
"""Check the semantic versions of the two release streams (see doc/releasing.md).

1. marker/VERSION and measure/VERSION are MAJOR.MINOR.PATCH and never lower than the newest release tag of their stream
   (marker-v*, tools-v*).
2. The public API of the C# marker library MB.FrameMarker is compared with the newest marker-v* release (Microsoft's ApiCompat,
   from the local tool manifest: dotnet tool restore). The C++ API mirrors it, so this also guards the C++ library.
   - A breaking change needs a new major version (a new minor version while the major version is 0).
   - Any other API change (an addition) needs at least a new minor version.
   Without a marker-v* tag there is nothing to compare with, and the API check is skipped. A tag on the checked out commit itself
   (the release run of that tag) is not a baseline; the release before it is.

marker/VERSION is the version of the next release, so raise it in the same change that alters the API.

Run from anywhere inside the repository (needs the release tags: git fetch --tags):
  python tools/check_semver.py
Exits with 1 and explains what to raise when a check fails.
"""

import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

SEMVER = re.compile(r"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$")
MARKER_PROJECT = Path("marker/csharp/MB.FrameMarker.csproj")
MARKER_ASSEMBLY = "MB.FrameMarker.dll"

Version = tuple[int, int, int]


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
    return (int(match.group(1)), int(match.group(2)), int(match.group(3)))


def show(version: Version) -> str:
    return f"{version[0]}.{version[1]}.{version[2]}"


def git(root: Path, *args: str) -> str:
    return subprocess.run(["git", *args], cwd=root, capture_output=True, text=True, check=True).stdout


def newest_tag(root: Path, prefix: str, skip: frozenset[str]) -> tuple[str, Version] | None:
    newest: tuple[str, Version] | None = None
    for tag in git(root, "tag", "--list", prefix + "*").splitlines():
        version = parse(tag.removeprefix(prefix))
        if tag not in skip and version is not None and (newest is None or version > newest[1]):
            newest = (tag, version)
    return newest


def check_stream(root: Path, version_file: str, prefix: str) -> Version | None:
    """Checks the VERSION file of one stream; returns its version when it is valid."""
    text = (root / version_file).read_text(encoding="utf-8").strip()
    version = parse(text)
    if version is None:
        error(f"{version_file} is '{text}', expected MAJOR.MINOR.PATCH (for example 1.2.3)")
        return None
    newest = newest_tag(root, prefix, frozenset())
    if newest is not None and version < newest[1]:
        error(f"{version_file} is {text}, lower than the released {newest[0]}")
        return None
    print(f"{version_file}: {text}" + (f" (newest release {newest[0]})" if newest else " (no release yet)"))
    return version


def build_marker_library(source_root: Path, output: Path) -> Path | None:
    project = source_root / MARKER_PROJECT
    result = subprocess.run(["dotnet", "build", str(project), "-c", "Release", "-o", str(output), "--nologo", "-v", "quiet"], cwd=source_root)
    if result.returncode != 0:
        error(f"building {project} failed")
        return None
    return output / MARKER_ASSEMBLY


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


def check_marker_api(root: Path, version: Version) -> bool:
    # The release run checks out the new tag itself; compare with the release before it
    at_head = frozenset(git(root, "tag", "--points-at", "HEAD").splitlines())
    newest = newest_tag(root, "marker-v", at_head)
    if newest is None:
        notice("No marker-v* release yet, so there is no API to compare with")
        return True
    tag, released = newest

    with tempfile.TemporaryDirectory(prefix="mb-semver-") as temp:
        worktree = Path(temp) / "baseline"
        _ = git(root, "worktree", "add", "--detach", str(worktree), tag)
        try:
            baseline = build_marker_library(worktree, Path(temp) / "baseline-bin")
        finally:
            _ = git(root, "worktree", "remove", "--force", str(worktree))
        current = build_marker_library(root, Path(temp) / "current-bin")
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
            error(f"MB.FrameMarker has breaking API changes since {tag}: raise marker/VERSION to at least {needed}")
            return False
        notice(f"MB.FrameMarker has breaking API changes since {tag}, covered by marker/VERSION {show(version)}")
        return True
    if not unchanged:
        if not raised_minor:
            print(changes_report)
            error(f"MB.FrameMarker has API additions since {tag}: raise marker/VERSION to at least {released[0]}.{released[1] + 1}.0")
            return False
        notice(f"MB.FrameMarker has API additions since {tag}, covered by marker/VERSION {show(version)}")
        return True
    print(f"MB.FrameMarker: public API unchanged since {tag}")
    return True


def main() -> int:
    root = Path(git(Path.cwd(), "rev-parse", "--show-toplevel").strip())
    marker = check_stream(root, "marker/VERSION", "marker-v")
    tools = check_stream(root, "measure/VERSION", "tools-v")
    if marker is None or tools is None:
        return 1
    return 0 if check_marker_api(root, marker) else 1


if __name__ == "__main__":
    sys.exit(main())
