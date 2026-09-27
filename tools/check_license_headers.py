#!/usr/bin/env python3
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""Check that every source file names its license with an SPDX-License-Identifier line (see LICENSE and CLAUDE.md).

The license follows the path:
- BSD-3-Clause: marker/ (the libraries applications embed) and test-data/markers/.
- LicenseRef-PolyForm-Perimeter-1.0.1: everything else (the PolyForm Perimeter License 1.0.1 is not on the SPDX license list, so it
  has a LicenseRef- identifier; its text is Part 2 of LICENSE).

Source files are code and build files that can hold a comment: C#, C and C++, Python, CMake, XAML, MSBuild, solutions and workflows.
Third-party code (third_party/) keeps its own notices. Documentation, JSON and test data are covered by LICENSE alone.

Run from anywhere inside the repository:
  python tools/check_license_headers.py          exits with 1 and lists files without the right identifier
  python tools/check_license_headers.py --fix    adds the identifier where it is missing
"""

import argparse
import subprocess
import sys
from pathlib import Path

BSD = "BSD-3-Clause"
POLYFORM = "LicenseRef-PolyForm-Perimeter-1.0.1"
BSD_PATHS = ("marker/", "test-data/markers/")
TAG = "SPDX-License-Identifier:"
# The identifier must be near the top, where readers and tools look for it
HEADER_LINES = 20

PATTERNS = (
    "*.cs",
    "*.cpp",
    "*.hpp",
    "*.h",
    "*.c",
    "*.hpp.in",
    "*.cmake.in",
    "*CMakeLists.txt",
    "*.py",
    "*.axaml",
    "*.csproj",
    "*.props",
    "*.slnx",
    "*.yml",
)


def license_for(relative: str) -> str:
    return BSD if relative.startswith(BSD_PATHS) else POLYFORM


def header_line(relative: str, identifier: str) -> str:
    name = relative.rsplit("/", 1)[-1]
    if name.endswith((".axaml", ".csproj", ".props", ".slnx")):
        return f"<!-- {TAG} {identifier} -->"
    if name.endswith((".py", ".yml", ".cmake.in")) or name == "CMakeLists.txt":
        return f"# {TAG} {identifier}"
    if name.endswith(".cs"):
        return f"//* {TAG} {identifier}"
    return f"// {TAG} {identifier}"


def insert_at(relative: str, lines: list[str]) -> int:
    """Where the identifier goes: inside the C# file header box, after a shebang or XML declaration, otherwise first."""
    if relative.endswith(".cs"):
        for i, line in enumerate(lines[:HEADER_LINES]):
            if line.startswith("//* (c) "):
                return i + 1
    if lines and (lines[0].startswith("#!") or lines[0].startswith("<?xml")):
        return 1
    return 0


def tracked_files(root: Path) -> list[str]:
    result = subprocess.run(["git", "ls-files", "--", *PATTERNS], cwd=root, capture_output=True, text=True, check=True)
    # Deleted but not yet staged files are still listed; they have nothing to check
    return sorted(line for line in result.stdout.splitlines() if line and "/third_party/" not in line and (root / line).is_file())


def check(root: Path, relative: str, fix: bool) -> str | None:
    path = root / relative
    data = path.read_bytes()
    bom = data.startswith(b"\xef\xbb\xbf")
    text = data.decode("utf-8-sig")
    newline = "\r\n" if "\r\n" in text else "\n"
    lines = text.split(newline)
    identifier = license_for(relative)
    found = [line for line in lines[:HEADER_LINES] if TAG in line]
    if any(line.rstrip(" ->").endswith(f"{TAG} {identifier}") for line in found):
        return None
    if found:
        return f"{relative}: names another license ({found[0].strip()}), expected {identifier}"
    if not fix:
        return f"{relative}: no '{TAG} {identifier}' line"
    lines.insert(insert_at(relative, lines), header_line(relative, identifier))
    _ = path.write_bytes((b"\xef\xbb\xbf" if bom else b"") + newline.join(lines).encode("utf-8"))
    return None


class Arguments(argparse.Namespace):
    """The parsed command line."""

    fix: bool = False


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    _ = parser.add_argument("--fix", action="store_true", help="add the identifier where it is missing")
    args = parser.parse_args(namespace=Arguments())
    root = Path(subprocess.run(["git", "rev-parse", "--show-toplevel"], capture_output=True, text=True, check=True).stdout.strip())
    files = tracked_files(root)
    problems = [problem for relative in files if (problem := check(root, relative, args.fix))]
    if problems:
        print("License identifiers (LICENSE, CLAUDE.md 'Conventions'):")
        for problem in problems:
            print("  " + problem)
        return 1
    print(f"License identifiers: OK ({len(files)} files)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
