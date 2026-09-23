#!/usr/bin/env python3
"""Build and run the consumer project (this folder) against mb_framemarker the ways doc/integrating.md documents.

fetchcontent   FetchContent_Declare(URL ... URL_HASH ...), pointed at the source tree with FETCHCONTENT_SOURCE_DIR_MB_FRAMEMARKER
subdirectory   add_subdirectory(<source tree>)
package        cmake --install to a temporary prefix, then find_package(mb_framemarker <version> CONFIG REQUIRED)
archive        FetchContent of a release archive (a local path as URL, with its SHA256) (only with --archive)

python marker/cpp/tests/consumer/check_consumers.py [--source <mb_framemarker source tree>] [--archive <mb-framemarker-cpp-x.y.z.tar.gz>]
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
    parser = argparse.ArgumentParser(description="Build and run the mb_framemarker consumer project in every documented way.")
    _ = parser.add_argument("--source", default=str(DEFAULT_SOURCE), help="The mb_framemarker source tree (default: marker/cpp).")
    _ = parser.add_argument("--archive", help="Also consume this release archive through FetchContent (URL + SHA256).")
    return parser.parse_args(namespace=Arguments())


def run(command: list[str]) -> None:
    print("> " + " ".join(command), flush=True)
    _ = subprocess.run(command, check=True)


def read_version(source: Path) -> str:
    # A release archive carries VERSION next to CMakeLists.txt, the repository keeps it in marker/VERSION
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


def main() -> int:
    args = parse_args()
    source = Path(args.source).resolve()
    version = read_version(source)
    major_minor = ".".join(version.split(".")[:2])
    work = Path(tempfile.mkdtemp(prefix="mb-framemarker-consumer-"))
    try:
        build_and_run(
            work,
            "fetchcontent",
            {"MB_CONSUMER_MODE": "fetchcontent", "FETCHCONTENT_SOURCE_DIR_MB_FRAMEMARKER": source.as_posix(), "MB_CONSUMER_VERSION": major_minor},
        )
        build_and_run(work, "subdirectory", {"MB_CONSUMER_MODE": "subdirectory", "MB_FRAMEMARKER_SOURCE_DIR": source.as_posix()})

        print("\n== install", flush=True)
        library = work / "library"
        prefix = work / "prefix"
        run(
            [
                "cmake",
                "-S",
                str(source),
                "-B",
                str(library),
                "-DCMAKE_BUILD_TYPE=Release",
                "-DMB_FRAMEMARKER_BUILD_TESTS=OFF",
                "-DMB_FRAMEMARKER_BUILD_TOOLS=OFF",
            ]
        )
        run(["cmake", "--build", str(library), "--config", "Release", "--parallel"])
        run(["cmake", "--install", str(library), "--config", "Release", "--prefix", str(prefix)])
        build_and_run(work, "package", {"MB_CONSUMER_MODE": "package", "CMAKE_PREFIX_PATH": prefix.as_posix(), "MB_CONSUMER_VERSION": major_minor})

        if args.archive:
            archive = Path(args.archive).resolve()
            sha256 = hashlib.sha256(archive.read_bytes()).hexdigest()
            build_and_run(
                work,
                "archive",
                {
                    "MB_CONSUMER_MODE": "fetchcontent",
                    "MB_FRAMEMARKER_URL": archive.as_posix(),
                    "MB_FRAMEMARKER_SHA256": sha256,
                    "MB_CONSUMER_VERSION": major_minor,
                },
            )
    except subprocess.CalledProcessError as error:
        print(f"\nConsumer check failed: {error}")
        return 1
    finally:
        shutil.rmtree(work, ignore_errors=True)
    print("\nConsumer check: OK (" + ", ".join(["fetchcontent", "subdirectory", "package"] + (["archive"] if args.archive else [])) + ")")
    return 0


if __name__ == "__main__":
    sys.exit(main())
