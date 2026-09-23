#!/usr/bin/env python3
"""Check the Unity package in a real Unity editor (batch mode, no window).

Assembles the package (build_upm.py), creates a throw-away Unity project that references it and runs UnityCheck/FrameMarkerUnityCheck.cs:
the package must compile, reproduce the C++ module matrices on Unity's scripting runtime and render pixel exact. Needs a Unity editor
with an active license (Unity Hub sign-in); CI has none, so run it locally before a marker release.

  python marker/unity/check_in_unity.py [--unity <path to the Unity executable>] [--keep]
"""

import argparse
import json
import os
import platform
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
REPOSITORY_ROOT = SCRIPT_DIR.parent.parent
TIMEOUT_SECONDS = 1800


class Arguments(argparse.Namespace):
    """The parsed command line."""

    unity: str | None = None
    keep: bool = False


def parse_args() -> Arguments:
    parser = argparse.ArgumentParser(description="Check the Unity package in a real Unity editor (batch mode).")
    _ = parser.add_argument("--unity", help="The Unity editor executable (default: the newest editor installed by Unity Hub).")
    _ = parser.add_argument("--keep", action="store_true", help="Keep the temporary project and log.")
    return parser.parse_args(namespace=Arguments())


def find_unity() -> Path | None:
    """The newest editor installed by Unity Hub in its default location."""
    system = platform.system()
    if system == "Windows":
        candidates = Path("C:/Program Files/Unity/Hub/Editor").glob("*/Editor/Unity.exe")
    elif system == "Darwin":
        candidates = Path("/Applications/Unity/Hub/Editor").glob("*/Unity.app/Contents/MacOS/Unity")
    else:
        candidates = (Path.home() / "Unity/Hub/Editor").glob("*/Editor/Unity")
    # The version folder: .../Editor/<version>/Editor/Unity(.exe) or .../Editor/<version>/Unity.app/Contents/MacOS/Unity
    depth = 3 if system == "Darwin" else 1
    editors = sorted(candidates, key=lambda path: version_key(path.parents[depth].name))
    return editors[-1] if editors else None


def version_key(name: str) -> list[int]:
    """'6000.3.24f1' -> [6000, 3, 24, 1], so versions sort numerically."""
    return [int(part) for part in "".join(ch if ch.isdigit() else " " for ch in name).split()]


def create_project(work: Path) -> Path:
    package = work / "package"
    result = subprocess.run([sys.executable, str(SCRIPT_DIR / "build_upm.py"), "--output", str(package), "--check"], check=False)
    if result.returncode != 0:
        sys.exit("build_upm.py failed")

    project = work / "project"
    (project / "Assets" / "Editor").mkdir(parents=True)
    (project / "Packages").mkdir()
    manifest = {"dependencies": {"com.manabattery.framemarker": f"file:{package.as_posix()}"}}
    _ = (project / "Packages" / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    _ = shutil.copy2(SCRIPT_DIR / "UnityCheck" / "FrameMarkerUnityCheck.cs", project / "Assets" / "Editor" / "FrameMarkerUnityCheck.cs")
    return project


def main() -> int:
    args = parse_args()
    unity = Path(args.unity) if args.unity else find_unity()
    if unity is None or not unity.exists():
        sys.exit("No Unity editor found; pass --unity <path to the Unity executable>.")

    work = Path(tempfile.mkdtemp(prefix="mb-framemarker-unity-"))
    project = create_project(work)
    log = work / "unity.log"
    command = [
        str(unity),
        "-batchmode",
        "-projectPath",
        str(project),
        "-executeMethod",
        "FrameMarkerUnityCheck.Run",
        "-logFile",
        str(log),
    ]
    print(f"Unity: {unity}")
    print(f"Project: {project}")
    env = {**os.environ, "MB_FRAMEMARKER_TEST_DATA": str(REPOSITORY_ROOT / "test-data" / "markers")}
    try:
        result = subprocess.run(command, env=env, timeout=TIMEOUT_SECONDS, check=False)
        exit_code = result.returncode
    except subprocess.TimeoutExpired:
        exit_code = -1
        print(f"Unity did not finish within {TIMEOUT_SECONDS} s")

    text = log.read_text(encoding="utf-8", errors="replace") if log.exists() else ""
    for line in text.splitlines():
        if "FrameMarkerUnityCheck" in line or "error CS" in line or "Compilation failed" in line:
            print(line)
    passed = exit_code == 0 and "FrameMarkerUnityCheck: PASS" in text
    print("Unity check: " + ("PASS" if passed else f"FAIL (exit code {exit_code}, log {log})"))
    if passed and not args.keep:
        shutil.rmtree(work, ignore_errors=True)
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
