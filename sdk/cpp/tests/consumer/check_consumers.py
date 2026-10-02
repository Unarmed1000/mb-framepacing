#!/usr/bin/env python3
# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause
"""Build and run the consumer project (this folder) against mb_framepacing the ways doc/integrating.md documents.

fetchcontent   FetchContent_Declare(URL ... URL_HASH ...), pointed at the source tree with FETCHCONTENT_SOURCE_DIR_MB_FRAMEPACING
subdirectory   add_subdirectory(<source tree>)
package        cmake --install to a temporary prefix, then find_package(mb_framepacing <version> CONFIG REQUIRED COMPONENTS ...)
archive        FetchContent of a release archive (a local path as URL, with its SHA256) (only with --archive)

The pacer module is experimental and off by default: the runs above must not get it. Two more runs ask for it and pace a frame
(MB_CONSUMER_PACER): subdirectory-pacer, and package-pacer against an install built with -DMB_FRAMEPACING_BUILD_PACER=ON.

python sdk/cpp/tests/consumer/check_consumers.py [--source <mb_framepacing source tree>] [--archive <mb-framepacing-cpp-x.y.z.tar.gz>]
"""

import argparse
import hashlib
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

CONSUMER_DIR = Path(__file__).resolve().parent
DEFAULT_SOURCE = CONSUMER_DIR.parent.parent


class Arguments(argparse.Namespace):
    """The parsed command line."""

    source: str = str(DEFAULT_SOURCE)
    archive: str | None = None


def parse_args() -> Arguments:
    parser = argparse.ArgumentParser(description="Build and run the mb_framepacing consumer project in every documented way.")
    _ = parser.add_argument("--source", default=str(DEFAULT_SOURCE), help="The mb_framepacing source tree (default: sdk/cpp).")
    _ = parser.add_argument("--archive", help="Also consume this release archive through FetchContent (URL + SHA256).")
    return parser.parse_args(namespace=Arguments())


def run(command: list[str]) -> None:
    print("> " + " ".join(command), flush=True)
    _ = subprocess.run(command, check=True)


def read_version(source: Path) -> str:
    # A release archive carries VERSION next to CMakeLists.txt, the repository keeps it in sdk/VERSION
    for candidate in (source / "VERSION", source.parent / "VERSION"):
        if candidate.exists():
            return candidate.read_text(encoding="utf-8").strip()
    sys.exit(f"No VERSION file for '{source}'")


def build_and_run(work: Path, name: str, definitions: dict[str, str]) -> None:
    print(f"\n== {name}", flush=True)
    build = work / name
    run(["cmake", "-S", str(CONSUMER_DIR), "-B", str(build), "-DCMAKE_BUILD_TYPE=Release", *[f"-D{key}={value}" for key, value in definitions.items()]])
    run(["cmake", "--build", str(build), "--config", "Release", "--parallel"])
    executables = [path for path in (build / "Release" / "consumer.exe", build / "consumer.exe", build / "consumer") if path.exists()]
    if not executables:
        sys.exit(f"{name}: the consumer executable was not built")
    run([str(executables[0])])


def check_installs_nothing(work: Path, name: str) -> None:
    """An application that builds the library inside its own project (add_subdirectory, FetchContent) installs its own files, not the
    library's: MB_FRAMEPACING_INSTALL is off unless mb_framepacing is the top level project."""
    prefix = work / (name + "-prefix")
    run(["cmake", "--install", str(work / name), "--config", "Release", "--prefix", str(prefix)])
    installed = sorted(str(path.relative_to(prefix)) for path in prefix.rglob("*") if path.is_file()) if prefix.exists() else []
    if installed:
        sys.exit(f"{name}: the consumer's install holds the library's files ({len(installed)}: {', '.join(installed[:5])}, ...)")


def install_library(work: Path, source: Path, name: str, options: list[str]) -> Path:
    """Build the library as its own project and install it: the prefix a consumer finds it in."""
    print(f"\n== install ({name})", flush=True)
    library = work / name
    prefix = work / (name + "-prefix")
    tests_and_tools_off = ["-DMB_FRAMEPACING_BUILD_TESTS=OFF", "-DMB_FRAMEPACING_BUILD_TOOLS=OFF"]
    run(["cmake", "-S", str(source), "-B", str(library), "-DCMAKE_BUILD_TYPE=Release", *tests_and_tools_off, *options])
    run(["cmake", "--build", str(library), "--config", "Release", "--parallel"])
    run(["cmake", "--install", str(library), "--config", "Release", "--prefix", str(prefix)])
    return prefix


def check_has_no_pacer(prefix: Path) -> None:
    """The pacer is experimental: an install with the default options has none of it."""
    found = sorted(str(path.relative_to(prefix)) for path in prefix.rglob("*") if "pacer" in path.name.lower())
    if found:
        sys.exit(f"The default install holds the pacer module, which is off by default ({', '.join(found[:5])})")


def main() -> int:
    args = parse_args()
    source = Path(args.source).resolve()
    version = read_version(source)
    major_minor = ".".join(version.split(".")[:2])
    work = Path(tempfile.mkdtemp(prefix="mb-framepacing-consumer-"))
    try:
        build_and_run(
            work,
            "fetchcontent",
            {"MB_CONSUMER_MODE": "fetchcontent", "FETCHCONTENT_SOURCE_DIR_MB_FRAMEPACING": source.as_posix(), "MB_CONSUMER_VERSION": major_minor},
        )
        check_installs_nothing(work, "fetchcontent")
        build_and_run(work, "subdirectory", {"MB_CONSUMER_MODE": "subdirectory", "MB_FRAMEPACING_SOURCE_DIR": source.as_posix()})
        check_installs_nothing(work, "subdirectory")

        prefix = install_library(work, source, "library", [])
        check_has_no_pacer(prefix)
        build_and_run(work, "package", {"MB_CONSUMER_MODE": "package", "CMAKE_PREFIX_PATH": prefix.as_posix(), "MB_CONSUMER_VERSION": major_minor})

        # The experimental pacer, for an application that asks for it
        pacer = {"MB_CONSUMER_PACER": "ON"}
        build_and_run(work, "subdirectory-pacer", {"MB_CONSUMER_MODE": "subdirectory", "MB_FRAMEPACING_SOURCE_DIR": source.as_posix(), **pacer})
        prefix = install_library(work, source, "library-pacer", ["-DMB_FRAMEPACING_BUILD_PACER=ON"])
        package = {"MB_CONSUMER_MODE": "package", "CMAKE_PREFIX_PATH": prefix.as_posix(), "MB_CONSUMER_VERSION": major_minor}
        build_and_run(work, "package-pacer", {**package, **pacer})

        if args.archive:
            archive = Path(args.archive).resolve()
            sha256 = hashlib.sha256(archive.read_bytes()).hexdigest()
            build_and_run(
                work,
                "archive",
                {
                    "MB_CONSUMER_MODE": "fetchcontent",
                    "MB_FRAMEPACING_URL": archive.as_posix(),
                    "MB_FRAMEPACING_SHA256": sha256,
                    "MB_CONSUMER_VERSION": major_minor,
                },
            )
    except subprocess.CalledProcessError as error:
        print(f"\nConsumer check failed: {error}")
        return 1
    finally:
        shutil.rmtree(work, ignore_errors=True)
    names = ["fetchcontent", "subdirectory", "package", "subdirectory-pacer", "package-pacer"] + (["archive"] if args.archive else [])
    print("\nConsumer check: OK (" + ", ".join(names) + ")")
    return 0


if __name__ == "__main__":
    sys.exit(main())
