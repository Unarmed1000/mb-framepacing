#!/usr/bin/env python3
# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""Check that every source file names its license with an SPDX-License-Identifier line, and that every code file names its
copyright holder with an SPDX-FileCopyrightText line right before it (see LICENSE and CLAUDE.md). Both are SPDX file tags
(ISO/IEC 5962), the short form the Linux kernel and REUSE use instead of the license text in every file:

    // SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
    // SPDX-License-Identifier: BSD-3-Clause

The license follows the path:
- BSD-3-Clause: sdk/ (the marker libraries applications embed, the data libraries, their formats and golden data).
- LicenseRef-PolyForm-Perimeter-1.0.1: everything else (the PolyForm Perimeter License 1.0.1 is not on the SPDX license list, so it
  has a LicenseRef- identifier; its text is Part 2 of LICENSE).

Source files are code and build files that can hold a comment: C#, C and C++, Python, CMake, XAML, shaders, MSBuild, solutions and
workflows. Code files (all but MSBuild, solutions and workflows) carry the copyright line too. Third-party code (third_party/) keeps
its own notices. Documentation, JSON and test data are covered by LICENSE alone.

A source file must be text: one that holds a NUL byte is reported too (git treats it as binary, so it gets no diffs and text
searches skip it).

Run from anywhere inside the repository:
  python tools/check_license_headers.py          exits with 1 and lists files without the right lines
  python tools/check_license_headers.py --fix    adds the lines where they are missing (the copyright with this year)
"""

import argparse
import datetime
import re
import subprocess
import sys
from pathlib import Path

BSD = "BSD-3-Clause"
POLYFORM = "LicenseRef-PolyForm-Perimeter-1.0.1"
BSD_PATHS = ("sdk/",)
TAG = "SPDX-License-Identifier:"
COPYRIGHT_TAG = "SPDX-FileCopyrightText:"
HOLDER = "Mana Battery ApS"
COPYRIGHT = re.compile(rf"{COPYRIGHT_TAG} Copyright \(C\) \d{{4}}(-\d{{4}})? {re.escape(HOLDER)}(\s*-->)?$")
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
    "*.shader",
    "*.hlsl",
    "*.vert",
    "*.frag",
)
# Build files: the license line only
BUILD_FILES = (".csproj", ".props", ".slnx", ".yml")


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
    """Where the identifier goes: at the end of the C# file header box, after a shebang, XML declaration or GLSL #version line (which
    must come first), otherwise first."""
    if relative.endswith(".cs"):
        boxes = [i for i, line in enumerate(lines[:HEADER_LINES]) if line.startswith("//****")]
        if len(boxes) >= 2:
            return boxes[1]
    if lines and lines[0].startswith(("#!", "<?xml", "#version")):
        return 1
    return 0


def is_code(relative: str) -> bool:
    return not relative.endswith(BUILD_FILES)


def copyright_line(license_line: str) -> str:
    """The copyright line in the license line's comment style: the same line with the other tag and text."""
    start = license_line.index(TAG)
    end = start + len(TAG)
    while end < len(license_line) and license_line[end] == " ":
        end += 1
    while end < len(license_line) and not license_line[end].isspace():
        end += 1
    return f"{license_line[:start]}{COPYRIGHT_TAG} Copyright (C) {datetime.date.today().year} {HOLDER}{license_line[end:]}"


def tracked_files(root: Path) -> list[str]:
    result = subprocess.run(["git", "ls-files", "--", *PATTERNS], cwd=root, capture_output=True, text=True, check=True)
    # Deleted but not yet staged files are still listed; they have nothing to check
    return sorted(line for line in result.stdout.splitlines() if line and "/third_party/" not in line and (root / line).is_file())


def check(root: Path, relative: str, fix: bool) -> str | None:
    path = root / relative
    data = path.read_bytes()
    if b"\0" in data:
        # A NUL byte makes git treat the file as binary: no diffs, and text searches (git grep -I) skip it
        line = data[: data.index(b"\0")].count(b"\n") + 1
        return f"{relative}:{line}: holds a NUL byte, so git treats the file as binary; write it as an escape ('\\0')"
    bom = data.startswith(b"\xef\xbb\xbf")
    text = data.decode("utf-8-sig")
    newline = "\r\n" if "\r\n" in text else "\n"
    lines = text.split(newline)
    identifier = license_for(relative)
    found = [line for line in lines[:HEADER_LINES] if TAG in line]
    ours = [i for i, line in enumerate(lines[:HEADER_LINES]) if line.rstrip(" ->").endswith(f"{TAG} {identifier}")]
    if found and not ours:
        return f"{relative}: names another license ({found[0].strip()}), expected {identifier}"
    if not ours:
        if not fix:
            return f"{relative}: no '{TAG} {identifier}' line"
        at = insert_at(relative, lines)
        lines.insert(at, header_line(relative, identifier))
        ours = [at]
    if is_code(relative):
        # The copyright holder right before the license line
        at = ours[0]
        if at == 0 or not COPYRIGHT.search(lines[at - 1].rstrip()):
            if not fix:
                return f"{relative}: no '{COPYRIGHT_TAG} Copyright (C) <year> {HOLDER}' line right before the license line"
            lines.insert(at, copyright_line(lines[at]))
    if fix:
        data_after = (b"\xef\xbb\xbf" if bom else b"") + newline.join(lines).encode("utf-8")
        if data_after != data:
            _ = path.write_bytes(data_after)
    return None


class Arguments(argparse.Namespace):
    """The parsed command line."""

    fix: bool = False


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    _ = parser.add_argument("--fix", action="store_true", help="add the license and copyright lines where they are missing")
    args = parser.parse_args(namespace=Arguments())
    root = Path(subprocess.run(["git", "rev-parse", "--show-toplevel"], capture_output=True, text=True, check=True).stdout.strip())
    files = tracked_files(root)
    problems = [problem for relative in files if (problem := check(root, relative, args.fix))]
    if problems:
        print("License and copyright lines (LICENSE, CLAUDE.md 'Conventions'):")
        for problem in problems:
            print("  " + problem)
        return 1
    print(f"License and copyright lines: OK ({len(files)} files)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
