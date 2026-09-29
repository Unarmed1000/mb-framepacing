#!/usr/bin/env python3
# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""Check the C++ libraries (the marker library, sdk/marker/cpp, and the data library, sdk/data/cpp) with clang-format and clang-tidy (config: each
library's .clang-format and .clang-tidy, the same rules).

Only our sources are checked, never third_party/ or fetched dependencies. clang-tidy needs a configured build of each library (GoogleTest
and nlohmann/json headers, the generated Version.hpp), found at <library>/build/<preset>:
  - with a compile database (Ninja/Makefiles, CMAKE_EXPORT_COMPILE_COMMANDS=ON, as the linux-sanitize preset and CI do) it
    uses the real compile flags;
  - without one (the Visual Studio generator writes none) it passes the include paths of that build directly.

  python tools/check_cpp.py                          # clang-format, then clang-tidy with the windows preset's builds
  python tools/check_cpp.py --preset linux-sanitize
  python tools/check_cpp.py --library data           # only sdk/data/cpp
  python tools/check_cpp.py --format-only
The CI versions are pinned in requirements-dev.txt (installed into .venv, see CLAUDE.md). The clang tools of the Python that runs
this script win (.venv/Scripts or .venv/bin), so '.venv/Scripts/python tools/check_cpp.py' uses the pinned versions without activating
the environment; otherwise they come from PATH.
"""

import argparse
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class Library:
    """A C++ library: its folder (relative to the repository), what to format and lint (relative to it), and how to compile a source
    without a compile database."""

    folder: str
    format_globs: tuple[str, ...]
    tidy_globs: tuple[str, ...]
    header_filter: str
    version_file: str
    version_define: str
    # Include folders relative to the library folder, and relative to its build folder
    includes: tuple[str, ...]
    build_includes: tuple[str, ...]


# The consumer project (sdk/marker/cpp/tests/consumer) is its own CMake project, so clang-tidy only formats it.
LIBRARIES = {
    "marker": Library(
        folder="sdk/marker/cpp",
        format_globs=("include/mb/framemarker/*.hpp", "src/*.cpp", "tests/*.cpp", "tests/consumer/*.cpp", "tools/*/*.cpp"),
        tidy_globs=("src/*.cpp", "tests/*.cpp", "tools/*/*.cpp"),
        header_filter=".*mb/framemarker/.*",
        version_file="sdk/marker/VERSION",
        version_define="MB_FRAMEMARKER_EXPECTED_VERSION",
        includes=("include", "third_party/qrcodegen"),
        build_includes=("include",),
    ),
    "data": Library(
        folder="sdk/data/cpp",
        format_globs=("include/mb/framepacingdata/*.hpp", "src/*.cpp", "tests/*.cpp"),
        tidy_globs=("src/*.cpp", "tests/*.cpp"),
        header_filter=".*mb/framepacingdata/.*",
        version_file="sdk/data/VERSION",
        version_define="MB_FRAMEPACINGDATA_EXPECTED_VERSION",
        includes=("include", "../../marker/cpp/include"),
        build_includes=("include", "mb_framemarker/include"),
    ),
}


class Arguments(argparse.Namespace):
    preset: str = "windows"
    library: str = "all"
    format_only: bool = False


def tool(name: str) -> str:
    """The tool installed next to the running Python (a venv's Scripts or bin folder), else the name for a PATH lookup."""
    exe = name + ".exe" if sys.platform == "win32" else name
    here = Path(sys.executable).parent
    for folder in (here, here / "Scripts", here / "bin"):
        if (folder / exe).is_file():
            return str(folder / exe)
    return name


def files(cpp: Path, globs: tuple[str, ...]) -> list[str]:
    return sorted(str(path.relative_to(cpp).as_posix()) for pattern in globs for path in cpp.glob(pattern))


def run(command: list[str], cwd: Path) -> bool:
    print("> " + " ".join(command[:3]) + (" ..." if len(command) > 3 else ""), flush=True)
    return subprocess.run(command, cwd=cwd).returncode == 0


def tidy_command(root: Path, library: Library, build: Path, sources: list[str]) -> list[str]:
    command = [tool("clang-tidy"), "--quiet", "--warnings-as-errors=*", f"--header-filter={library.header_filter}"]
    if (build / "compile_commands.json").is_file():
        return [*command, "-p", str(build), *sources]
    version = (root / library.version_file).read_text(encoding="utf-8").strip()
    flags = [
        "-std=c++20",
        *(f"-I{include}" for include in library.includes),
        *(f"-I{build / include}" for include in library.build_includes),
        f"-isystem{build / '_deps/googletest-src/googletest/include'}",
        f"-isystem{build / '_deps/nlohmann_json-src/include'}",
        f'-D{library.version_define}="{version}"',
        f'-DMB_FRAMEPACINGDATA_SOURCE_DIR="{(root / library.folder).as_posix()}"',
    ]
    return [*command, *sources, "--", *flags]


def check(root: Path, library: Library, args: Arguments) -> bool:
    cpp = root / library.folder
    ok = run([tool("clang-format"), "--dry-run", "--Werror", *files(cpp, library.format_globs)], cpp)
    if args.format_only:
        return ok
    build = cpp / "build" / args.preset
    if not (build / "include").is_dir():
        print(f"error: {build} is not a configured build of {library.folder} (run cmake --preset {args.preset} there first)")
        return False
    return run(tidy_command(root, library, build, files(cpp, library.tidy_globs)), cpp) and ok


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    _ = parser.add_argument("--preset", help="the CMake preset whose builds clang-tidy uses (default: windows)")
    _ = parser.add_argument("--library", choices=["all", *LIBRARIES], help="check one library only (default: all)")
    _ = parser.add_argument("--format-only", action="store_true", help="only run clang-format")
    args = parser.parse_args(namespace=Arguments())

    root = Path(__file__).resolve().parent.parent
    ok = True
    for name, library in LIBRARIES.items():
        if args.library in ("all", name):
            ok = check(root, library, args) and ok
    print("C++ checks passed" if ok else "C++ checks FAILED")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
