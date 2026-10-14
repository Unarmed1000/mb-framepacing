#!/usr/bin/env python3
# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause
"""Build and run the consumer project (this folder) against mb_framepacing the ways doc/integrating.md documents.

fetchcontent   FetchContent_Declare(URL ... URL_HASH ...), pointed at the source tree with FETCHCONTENT_SOURCE_DIR_MB_FRAMEPACING
subdirectory   add_subdirectory(<source tree>)
package        cmake --install to a temporary prefix, then find_package(mb_framepacing <version> CONFIG REQUIRED COMPONENTS ...)
archive        FetchContent of a release archive (a local path as URL, with its SHA256) (only with --archive)

The pacer is experimental and off by default: the runs above must not get it. Two more runs ask for the pacer and pace a frame
(MB_CONSUMER_PACER): subdirectory-pacer, and package-pacer against an install built with -DMB_FRAMEPACING_BUILD_PACER=ON. The trace
module is on by default: two more runs ask for it and make a frame's record (MB_CONSUMER_TRACE), subdirectory-trace and package-trace
against the default install.

--package-only runs the package way alone, with the default modules: the library built and installed once, the check that the
install holds the trace module and nothing of the pacer, and a consumer that finds it. It is what a system runs when another one runs all
of them (CI: everything on Ubuntu, this on Windows and macOS, where a build of the library takes longest).

python sdk/cpp/tests/consumer/check_consumers.py [--source <mb_framepacing source tree>] [--archive <mb-framepacing-cpp-x.y.z.tar.gz>]
                                                 [--package-only]
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
# The modules that are off unless an application asks for them
EXPERIMENTAL_MODULES = ("pacer",)


class Arguments(argparse.Namespace):
    """The parsed command line."""

    source: str = str(DEFAULT_SOURCE)
    archive: str | None = None
    package_only: bool = False


def parse_args() -> Arguments:
    parser = argparse.ArgumentParser(description="Build and run the mb_framepacing consumer project in every documented way.")
    _ = parser.add_argument("--source", default=str(DEFAULT_SOURCE), help="The mb_framepacing source tree (default: sdk/cpp).")
    _ = parser.add_argument("--archive", help="Also consume this release archive through FetchContent (URL + SHA256).")
    _ = parser.add_argument("--package-only", action="store_true", help="Only the package way, with the default modules.")
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


def module_files(prefix: Path, module: str) -> list[str]:
    """What an install holds of a module: its headers (include/mb/framepacing/<module>/), its library (mb_framepacing_<module>) and
    the CMake files that name its target (mb_framepacing::<module>). Found by those and not by the module's name alone, which is a
    plain word that another module's files may have in their names."""
    found: list[str] = []
    for path in prefix.rglob("*"):
        if not path.is_file():
            continue
        relative = path.relative_to(prefix).as_posix()
        header = f"/mb/framepacing/{module}/" in f"/{relative}"
        library = f"mb_framepacing_{module}" in path.name.lower()
        target = path.suffix == ".cmake" and f"mb_framepacing::{module}" in path.read_text(encoding="utf-8")
        if header or library or target:
            found.append(relative)
    return sorted(found)


def check_has_no_experimental_module(prefix: Path) -> None:
    """The pacer is experimental: an install with the default options has nothing of it."""
    for module in EXPERIMENTAL_MODULES:
        found = module_files(prefix, module)
        if module == "pacer":
            # No other module has a file with the pacer's name in its own
            found += sorted(path.relative_to(prefix).as_posix() for path in prefix.rglob("*") if "pacer" in path.name.lower())
        if found:
            sys.exit(f"The default install holds the {module} module, which is off by default ({', '.join(found[:5])})")


def check_has_module(prefix: Path, module: str) -> None:
    """An install that should hold a module does: what shows that the check of the default install looks for the right files."""
    if not module_files(prefix, module):
        sys.exit(f"The install built with the {module} module holds none of its files")


def main() -> int:
    args = parse_args()
    source = Path(args.source).resolve()
    version = read_version(source)
    major_minor = ".".join(version.split(".")[:2])
    work = Path(tempfile.mkdtemp(prefix="mb-framepacing-consumer-"))
    try:
        if args.package_only:
            if args.archive:
                sys.exit("--package-only does not take --archive")
            prefix = install_library(work, source, "library", [])
            check_has_no_experimental_module(prefix)
            check_has_module(prefix, "trace")
            definitions = {"MB_CONSUMER_MODE": "package", "CMAKE_PREFIX_PATH": prefix.as_posix(), "MB_CONSUMER_VERSION": major_minor}
            build_and_run(work, "package", definitions)
            print("\nConsumer check: OK (package)")
            return 0

        build_and_run(
            work,
            "fetchcontent",
            {"MB_CONSUMER_MODE": "fetchcontent", "FETCHCONTENT_SOURCE_DIR_MB_FRAMEPACING": source.as_posix(), "MB_CONSUMER_VERSION": major_minor},
        )
        check_installs_nothing(work, "fetchcontent")
        build_and_run(work, "subdirectory", {"MB_CONSUMER_MODE": "subdirectory", "MB_FRAMEPACING_SOURCE_DIR": source.as_posix()})
        check_installs_nothing(work, "subdirectory")

        default_prefix = install_library(work, source, "library", [])
        check_has_no_experimental_module(default_prefix)
        check_has_module(default_prefix, "trace")
        default_package = {"MB_CONSUMER_MODE": "package", "CMAKE_PREFIX_PATH": default_prefix.as_posix(), "MB_CONSUMER_VERSION": major_minor}
        build_and_run(work, "package", default_package)

        # The experimental pacer, for an application that asks for it
        pacer = {"MB_CONSUMER_PACER": "ON"}
        build_and_run(work, "subdirectory-pacer", {"MB_CONSUMER_MODE": "subdirectory", "MB_FRAMEPACING_SOURCE_DIR": source.as_posix(), **pacer})
        prefix = install_library(work, source, "library-pacer", ["-DMB_FRAMEPACING_BUILD_PACER=ON"])
        check_has_module(prefix, "pacer")
        package = {"MB_CONSUMER_MODE": "package", "CMAKE_PREFIX_PATH": prefix.as_posix(), "MB_CONSUMER_VERSION": major_minor}
        build_and_run(work, "package-pacer", {**package, **pacer})

        # The trace module, which the default install holds
        trace = {"MB_CONSUMER_TRACE": "ON"}
        build_and_run(work, "subdirectory-trace", {"MB_CONSUMER_MODE": "subdirectory", "MB_FRAMEPACING_SOURCE_DIR": source.as_posix(), **trace})
        build_and_run(work, "package-trace", {**default_package, **trace})

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
    names = ["fetchcontent", "subdirectory", "package", "subdirectory-pacer", "package-pacer", "subdirectory-trace", "package-trace"]
    names += ["archive"] if args.archive else []
    print("\nConsumer check: OK (" + ", ".join(names) + ")")
    return 0


if __name__ == "__main__":
    sys.exit(main())
