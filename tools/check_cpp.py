#!/usr/bin/env python3
"""Check the C++ marker library with clang-format and clang-tidy (config: marker/cpp/.clang-format, marker/cpp/.clang-tidy).

Only our sources are checked, never third_party/. clang-tidy needs a configured build (GoogleTest headers, the generated
Version.hpp):
  - with a compile database (Ninja/Makefiles, CMAKE_EXPORT_COMPILE_COMMANDS=ON, as the linux-sanitize preset and CI do) it
    uses the real compile flags;
  - without one (the Visual Studio generator writes none) it passes the include paths of that build directly.

  python tools/check_cpp.py                                      # clang-format, then clang-tidy with marker/cpp/build/windows
  python tools/check_cpp.py --build-dir marker/cpp/build/linux-sanitize
  python tools/check_cpp.py --format-only
The CI versions are pinned in requirements-dev.txt (python -m pip install -r requirements-dev.txt).
"""

import argparse
import subprocess
import sys
from pathlib import Path

# Relative to marker/cpp. The consumer project (tests/consumer) is its own CMake project, so clang-tidy only formats it.
FORMAT_GLOBS = ["include/mb/framemarker/*.hpp", "src/*.cpp", "tests/*.cpp", "tests/consumer/*.cpp", "tools/*/*.cpp"]
TIDY_GLOBS = ["src/*.cpp", "tests/*.cpp", "tools/*/*.cpp"]


class Arguments(argparse.Namespace):
    build_dir: str = "marker/cpp/build/windows"
    format_only: bool = False


def files(cpp: Path, globs: list[str]) -> list[str]:
    return sorted(str(path.relative_to(cpp).as_posix()) for pattern in globs for path in cpp.glob(pattern))


def run(command: list[str], cwd: Path) -> bool:
    print("> " + " ".join(command[:3]) + (" ..." if len(command) > 3 else ""), flush=True)
    return subprocess.run(command, cwd=cwd).returncode == 0


def tidy_command(cpp: Path, build: Path, sources: list[str]) -> list[str]:
    command = ["clang-tidy", "--quiet", "--warnings-as-errors=*", "--header-filter=.*mb/framemarker/.*"]
    if (build / "compile_commands.json").is_file():
        return [*command, "-p", str(build), *sources]
    version = (cpp.parent / "VERSION").read_text(encoding="utf-8").strip()
    flags = [
        "-std=c++20",
        "-Iinclude",
        f"-I{build / 'include'}",
        "-Ithird_party/qrcodegen",
        f"-isystem{build / '_deps/googletest-src/googletest/include'}",
        f'-DMB_FRAMEMARKER_EXPECTED_VERSION="{version}"',
    ]
    return [*command, *sources, "--", *flags]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    _ = parser.add_argument("--build-dir", help="configured CMake build directory (relative to the repository root)")
    _ = parser.add_argument("--format-only", action="store_true", help="only run clang-format")
    args = parser.parse_args(namespace=Arguments())

    root = Path(__file__).resolve().parent.parent
    cpp = root / "marker/cpp"
    ok = run(["clang-format", "--dry-run", "--Werror", *files(cpp, FORMAT_GLOBS)], cpp)
    if args.format_only:
        return 0 if ok else 1

    build = (root / args.build_dir).resolve()
    if not (build / "include").is_dir():
        print(f"error: {build} is not a configured build of marker/cpp (run cmake --preset first)")
        return 1
    ok = run(tidy_command(cpp, build, files(cpp, TIDY_GLOBS)), cpp) and ok
    print("C++ checks passed" if ok else "C++ checks FAILED")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
