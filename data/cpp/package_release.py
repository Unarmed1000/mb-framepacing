#!/usr/bin/env python3
# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause
"""Create the C++ release archives of mb_framepacingdata and prove they work on their own.

  mb-framepacingdata-cpp-<version>.tar.gz and .zip, each holding one folder mb-framepacingdata-cpp-<version>/ with
    the library source tree (data/cpp without build output), VERSION, LICENSE,
    licenses/ (nlohmann/json: compiled in; GoogleTest: fetched by the tests), doc/ (the data formats) and
    test-data/data/ (the golden data its tests read)
  SHA256SUMS

The archive needs the marker library (mb_framemarker, released separately) installed or added first. With --verify the marker library of
this checkout is installed into a temporary prefix; the extracted archive is then configured against it, built, tested and installed, and
two consumer projects are built: one with find_package(mb_framepacingdata), one with FetchContent of the tar.gz (URL + SHA256).

  python data/cpp/package_release.py --output <folder> [--verify]
"""

import argparse
import hashlib
import shutil
import subprocess
import sys
import tarfile
import tempfile
import zipfile
from pathlib import Path

CPP_DIR = Path(__file__).resolve().parent
DATA_DIR = CPP_DIR.parent
REPOSITORY_ROOT = DATA_DIR.parent
EXCLUDED_DIRECTORIES = {"build", "out", ".vs", ".vscode", "__pycache__"}
EXTRA_FILES = {
    "VERSION": DATA_DIR / "VERSION",
    "LICENSE": DATA_DIR / "LICENSE",
    "licenses/nlohmann-json-MIT.txt": REPOSITORY_ROOT / "licenses" / "nlohmann-json-MIT.txt",
    "licenses/googletest-BSD-3-Clause.txt": REPOSITORY_ROOT / "licenses" / "googletest-BSD-3-Clause.txt",
    "doc/capture-data-format.md": REPOSITORY_ROOT / "doc" / "capture-data-format.md",
    "doc/analysis-output-format.md": REPOSITORY_ROOT / "doc" / "analysis-output-format.md",
}
EXTRA_FOLDERS = {"test-data/data": REPOSITORY_ROOT / "test-data" / "data"}

CONSUMER_SOURCE = """#include <mb/framepacingdata/FramePacingData.hpp>
#include <cstdio>

int main()
{
  std::printf("mb_framepacingdata %s: %lld ticks\\n", MB::FramePacingData::GetLibraryVersion().Text.data(),
              static_cast<long long>(MB::FramePacingData::ParseTicks("16.6667")));
  return MB::FramePacingData::ParseTicks("16.6667") == 166'667 ? 0 : 1;
}
"""


class Arguments(argparse.Namespace):
    """The parsed command line."""

    output: str = ""
    verify: bool = False


def parse_args() -> Arguments:
    parser = argparse.ArgumentParser(description="Create (and verify) the C++ release archives of mb_framepacingdata.")
    _ = parser.add_argument("--output", required=True, help="Folder for the archives and SHA256SUMS.")
    _ = parser.add_argument("--verify", action="store_true", help="Build, test and consume the extracted archive against this checkout's marker library.")
    return parser.parse_args(namespace=Arguments())


def run(command: list[str]) -> None:
    print("> " + " ".join(command), flush=True)
    _ = subprocess.run(command, check=True)


def stage(root: Path) -> None:
    """Copy the library source tree and the extra files into root."""
    for path in sorted(CPP_DIR.rglob("*")):
        relative = path.relative_to(CPP_DIR)
        # This script is repository tooling (it reads data/VERSION and the repository's licenses); the archive does not need it
        if any(part in EXCLUDED_DIRECTORIES for part in relative.parts) or path.is_dir() or path == Path(__file__).resolve():
            continue
        target = root / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        _ = shutil.copy2(path, target)
    for name, source in EXTRA_FILES.items():
        target = root / name
        target.parent.mkdir(parents=True, exist_ok=True)
        _ = shutil.copy2(source, target)
    for name, source in EXTRA_FOLDERS.items():
        _ = shutil.copytree(source, root / name)


def create_archives(output: Path, version: str) -> list[Path]:
    name = f"mb-framepacingdata-cpp-{version}"
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="mb-framepacingdata-stage-") as staging:
        root = Path(staging) / name
        stage(root)
        tar_path = output / f"{name}.tar.gz"
        with tarfile.open(tar_path, "w:gz") as archive:
            archive.add(root, arcname=name)
        zip_path = output / f"{name}.zip"
        with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as archive:
            for file in sorted(root.rglob("*")):
                if file.is_file():
                    archive.write(file, f"{name}/{file.relative_to(root).as_posix()}")
    return [tar_path, zip_path]


def write_checksums(output: Path, archives: list[Path]) -> Path:
    lines = [f"{hashlib.sha256(path.read_bytes()).hexdigest()}  {path.name}" for path in archives]
    checksums = output / "SHA256SUMS"
    _ = checksums.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    return checksums


def build_and_install(source: Path, build: Path, prefix: Path, *options: str) -> None:
    run(
        [
            "cmake",
            "-S",
            str(source),
            "-B",
            str(build),
            "-DCMAKE_BUILD_TYPE=Release",
            f"-DCMAKE_PREFIX_PATH={prefix}",
            f"-DCMAKE_INSTALL_PREFIX={prefix}",
            *options,
        ]
    )
    run(["cmake", "--build", str(build), "--config", "Release", "--parallel"])
    run(["cmake", "--install", str(build), "--config", "Release"])


def build_consumer(folder: Path, prefix: Path, cmake: str) -> None:
    folder.mkdir(parents=True)
    _ = (folder / "main.cpp").write_text(CONSUMER_SOURCE, encoding="utf-8")
    _ = (folder / "CMakeLists.txt").write_text(cmake, encoding="utf-8")
    build = folder / "build"
    run(["cmake", "-S", str(folder), "-B", str(build), "-DCMAKE_BUILD_TYPE=Release", f"-DCMAKE_PREFIX_PATH={prefix}"])
    run(["cmake", "--build", str(build), "--config", "Release", "--parallel"])


def verify(tar_path: Path, version: str) -> None:
    with tempfile.TemporaryDirectory(prefix="mb-framepacingdata-verify-") as temporary:
        work = Path(temporary)
        prefix = work / "prefix"
        # The marker library the archive needs, installed as a user would
        build_and_install(
            REPOSITORY_ROOT / "marker" / "cpp", work / "marker-build", prefix, "-DMB_FRAMEMARKER_BUILD_TESTS=OFF", "-DMB_FRAMEMARKER_BUILD_TOOLS=OFF"
        )

        with tarfile.open(tar_path) as archive:
            archive.extractall(work, filter="data")
        source = work / f"mb-framepacingdata-cpp-{version}"
        build = work / "data-build"
        build_and_install(source, build, prefix, "-DMB_FRAMEPACINGDATA_BUILD_TESTS=ON")
        run(["ctest", "--test-dir", str(build), "-C", "Release", "--output-on-failure"])

        project = "cmake_minimum_required(VERSION 4.0)\nproject(consumer LANGUAGES CXX)\n"
        link = "add_executable(consumer main.cpp)\ntarget_link_libraries(consumer PRIVATE mb::framepacingdata)\n"
        build_consumer(work / "find-package", prefix, project + f"find_package(mb_framepacingdata {version.rsplit('.', 1)[0]} CONFIG REQUIRED)\n" + link)
        digest = hashlib.sha256(tar_path.read_bytes()).hexdigest()
        fetch = (
            "include(FetchContent)\n"
            f'FetchContent_Declare(mb_framepacingdata URL "{tar_path.resolve().as_posix()}" URL_HASH SHA256={digest})\n'
            "FetchContent_MakeAvailable(mb_framepacingdata)\n"
        )
        build_consumer(work / "fetch-content", prefix, project + "find_package(mb_framemarker 0.1 CONFIG REQUIRED)\n" + fetch + link)


def main() -> int:
    args = parse_args()
    version = (DATA_DIR / "VERSION").read_text(encoding="utf-8").strip()
    output = Path(args.output).resolve()
    archives = create_archives(output, version)
    checksums = write_checksums(output, archives)
    print(checksums.read_text(encoding="utf-8"), end="")
    if args.verify:
        try:
            verify(archives[0], version)
        except subprocess.CalledProcessError as error:
            print(f"Verification failed: {error}")
            return 1
        print(f"Release archives verified: {', '.join(path.name for path in archives)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
