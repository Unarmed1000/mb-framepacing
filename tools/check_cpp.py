#!/usr/bin/env python3
# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""Check the C++ library (sdk/cpp: the core, marker and data modules) with clang-format and clang-tidy (config: sdk/cpp/.clang-format and
.clang-tidy). The consumer project (sdk/cpp/tests/consumer), the size probes (sdk/cpp/tests/size) and the Conan recipe's test package
(sdk/cpp/conan) are only formatted.

Only our sources are checked, never third_party/ or fetched dependencies. clang-tidy needs a configured build of the library (GoogleTest
and nlohmann/json headers, the generated Version.hpp), found at sdk/cpp/build/<preset>:
  - with a compile database (Ninja/Makefiles, CMAKE_EXPORT_COMPILE_COMMANDS=ON, as the linux-sanitize preset and CI do) it
    uses the real compile flags;
  - without one (the Visual Studio generator writes none) it passes the include paths of that build directly.

  python tools/check_cpp.py                          # clang-format, then clang-tidy with the windows preset's build
  python tools/check_cpp.py --preset linux-sanitize
  python tools/check_cpp.py --module data            # only sdk/cpp/data
  python tools/check_cpp.py --format-only
The CI versions are pinned in pyproject.toml and uv.lock (uv sync installs them into .venv, see CLAUDE.md). The clang tools of the
Python that runs this script win (.venv/Scripts or .venv/bin), so 'uv run tools/check_cpp.py' uses the pinned versions; otherwise they
come from PATH.
"""

import argparse
import subprocess
import sys
from collections.abc import Sequence
from pathlib import Path

LIBRARY = "sdk/cpp"
MODULES = ("core", "marker", "data", "pacer")
# The modules' shared test support (sdk/cpp/testing): checked with them, never part of the library
TEST_SUPPORT = "testing"
# Per module, relative to its folder: what to format, and what clang-tidy checks (it follows the headers they include)
FORMAT_GLOBS = (
    "include/**/*.hpp",
    "source/**/*.cpp",
    "source/**/*.hpp",
    "tests/**/*.cpp",
    "tests/**/*.hpp",
    "tools/*/*.cpp",
    "benchmarks/*.cpp",
    "benchmarks/*.hpp",
)
TIDY_GLOBS = ("source/**/*.cpp", "tests/**/*.cpp", "tools/*/*.cpp", "benchmarks/*.cpp")
HEADER_FILTER = ".*mb/framepacing/.*"
# The consumer project is its own CMake project, so clang-tidy only formats it
CONSUMER_GLOB = "tests/consumer/*.cpp"
# The size probes (tools/measure_sdk_size.py): formatted only, as the consumer project
SIZE_GLOB = "tests/size/*.cpp"
# The Conan recipe's test package builds against the package, not in the library's build, so it is only formatted
CONAN_TEST_PACKAGES = "sdk/cpp/conan/recipes/*/all/test_package/*.cpp"


class Arguments(argparse.Namespace):
    preset: str = "windows"
    module: str = "all"
    format_only: bool = False


def tool(name: str) -> str:
    """The tool installed next to the running Python (a venv's Scripts or bin folder), else the name for a PATH lookup."""
    exe = name + ".exe" if sys.platform == "win32" else name
    here = Path(sys.executable).parent
    for folder in (here, here / "Scripts", here / "bin"):
        if (folder / exe).is_file():
            return str(folder / exe)
    return name


def files(cpp: Path, modules: Sequence[str], globs: tuple[str, ...]) -> list[str]:
    return sorted(str(path.relative_to(cpp).as_posix()) for module in modules for pattern in globs for path in (cpp / module).glob(pattern))


def run(command: list[str], cwd: Path) -> bool:
    print("> " + " ".join(command[:3]) + (" ..." if len(command) > 3 else ""), flush=True)
    return subprocess.run(command, cwd=cwd).returncode == 0


def tidy_command(root: Path, build: Path, sources: list[str]) -> list[str]:
    command = [tool("clang-tidy"), "--quiet", "--warnings-as-errors=*", f"--header-filter={HEADER_FILTER}"]
    if (build / "compile_commands.json").is_file():
        return [*command, "-p", str(build), *sources]
    version = (root / "sdk" / "VERSION").read_text(encoding="utf-8").strip()
    cpp = root / LIBRARY
    flags = [
        "-std=c++20",
        *(f"-I{cpp / module / 'include'}" for module in (*MODULES, TEST_SUPPORT)),
        # the marker's tests and benchmarks compare its QR encoder with the vendored reference
        f"-I{cpp / 'marker' / 'reference' / 'third_party' / 'qrcodegen'}",
        f"-I{cpp / 'marker' / 'reference'}",
        f"-I{cpp / 'pacer' / 'tests' / 'simulation'}",
        # the marker and data tests check their private formats
        f"-I{cpp / 'marker' / 'source'}",
        f"-I{cpp / 'data' / 'source'}",
        f"-I{build / 'include'}",
        f"-isystem{build / '_deps/googletest-src/googletest/include'}",
        f"-isystem{build / '_deps/googlebenchmark-src/include'}",
        f"-isystem{build / '_deps/nlohmann_json-src/include'}",
        f'-DMB_FRAMEPACING_EXPECTED_VERSION="{version}"',
        f'-DMB_FRAMEPACING_DATA_SOURCE_DIR="{(cpp / "data").as_posix()}"',
        f'-DMB_FRAMEPACING_PACER_SOURCE_DIR="{(cpp / "pacer").as_posix()}"',
    ]
    return [*command, *sources, "--", *flags]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    _ = parser.add_argument("--preset", help="the CMake preset whose build clang-tidy uses (default: windows)")
    _ = parser.add_argument("--module", choices=["all", *MODULES], help="check one module only (default: all)")
    _ = parser.add_argument("--format-only", action="store_true", help="only run clang-format")
    args = parser.parse_args(namespace=Arguments())

    root = Path(__file__).resolve().parent.parent
    cpp = root / LIBRARY
    modules = [*MODULES, TEST_SUPPORT] if args.module == "all" else [args.module]
    formatted = files(cpp, modules, FORMAT_GLOBS)
    if args.module == "all":
        formatted += sorted(str(path.relative_to(cpp).as_posix()) for path in cpp.glob(CONSUMER_GLOB))
        formatted += sorted(str(path.relative_to(cpp).as_posix()) for path in cpp.glob(SIZE_GLOB))
    ok = run([tool("clang-format"), "--dry-run", "--Werror", *formatted], cpp)
    if args.module == "all":
        test_packages = sorted(path.relative_to(root).as_posix() for path in root.glob(CONAN_TEST_PACKAGES))
        ok = run([tool("clang-format"), "--dry-run", "--Werror", *test_packages], root) and ok
    if not args.format_only:
        build = cpp / "build" / args.preset
        if not (build / "include").is_dir():
            print(f"error: {build} is not a configured build of {LIBRARY} (run cmake --preset {args.preset} there first)")
            return 1
        ok = run(tidy_command(root, build, files(cpp, modules, TIDY_GLOBS)), cpp) and ok
    print("C++ checks passed" if ok else "C++ checks FAILED")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
