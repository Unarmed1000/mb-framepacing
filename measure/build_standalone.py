#!/usr/bin/env python3
"""Build self-contained, single-file standalones of the mb-framepacing tools.

Auto-detects the current OS/CPU into a .NET runtime identifier (RID) and runs
`dotnet publish` for the command line tool, the GUI or both. Pass --rid to build
for a different target. The version comes from the repository's VERSION file.

Output: measure/publish/<rid>/<app>/ (the executable, NLog.config and licenses/).
"""

import argparse
import platform
import shutil
import subprocess
import sys
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent

APPS = {
    "cli": SCRIPT_DIR / "app" / "FramePacing" / "FramePacing.csproj",
    "gui": SCRIPT_DIR / "app" / "FramePacing.Gui" / "FramePacing.Gui.csproj",
}

OS_MAP = {"Windows": "win", "Linux": "linux", "Darwin": "osx"}
ARCH_MAP = {"AMD64": "x64", "X86_64": "x64", "ARM64": "arm64", "AARCH64": "arm64"}


def detect_rid() -> str:
    """Map this machine's platform to a .NET RID, e.g. 'win-x64'."""
    system = platform.system()
    machine = platform.machine().upper()

    os_part = OS_MAP.get(system)
    if os_part is None:
        sys.exit(f"Unsupported OS '{system}'. Use --rid to specify a target explicitly.")

    arch_part = ARCH_MAP.get(machine)
    if arch_part is None:
        sys.exit(f"Unsupported architecture '{machine}'. Use --rid to specify a target explicitly.")

    return f"{os_part}-{arch_part}"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Build self-contained single-file standalones of the mb-framepacing tools.",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    parser.add_argument(
        "--app",
        choices=["cli", "gui", "all"],
        default="all",
        help="Which tool to build: the command line tool (mb-framepacing), the GUI (mb-framepacing-gui) or both.",
    )
    parser.add_argument(
        "--rid",
        default=None,
        help="Target .NET runtime identifier (e.g. win-x64, linux-x64, linux-arm64, osx-arm64). "
        "Defaults to auto-detecting the current machine.",
    )
    parser.add_argument(
        "-c",
        "--configuration",
        default="Release",
        help="Build configuration.",
    )
    return parser.parse_args()


def publish(name: str, project: Path, rid: str, configuration: str) -> int:
    output_dir = SCRIPT_DIR / "publish" / rid / name
    command: list[str] = [
        "dotnet",
        "publish",
        str(project),
        "-r",
        rid,
        "-c",
        configuration,
        "--self-contained",
        "-p:PublishSingleFile=true",
        "-p:EnableCompressionInSingleFile=true",
        # Avalonia/SkiaSharp native libraries are unpacked from the single file at start up
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-o",
        str(output_dir),
    ]
    print(f"\nBuilding {name} standalone for {rid} ({configuration})")
    print(" ".join(command))
    result = subprocess.run(command)
    if result.returncode == 0:
        print(f"Output: {output_dir}")
    return result.returncode


def main() -> int:
    args = parse_args()

    if shutil.which("dotnet") is None:
        sys.exit("'dotnet' was not found on PATH. Install the .NET 10 SDK and try again.")

    rid = args.rid or detect_rid()
    names = list(APPS) if args.app == "all" else [args.app]
    for name in names:
        exit_code = publish(name, APPS[name], rid, args.configuration)
        if exit_code != 0:
            return exit_code
    return 0


if __name__ == "__main__":
    sys.exit(main())
