#!/usr/bin/env python3
"""Create the C++ release archives of mb_framemarker and prove they work on their own.

  mb-framemarker-cpp-<version>.tar.gz and .zip, each holding one folder mb-framemarker-cpp-<version>/ with
    the library source tree (marker/cpp without build output), VERSION, LICENSE,
    licenses/ (qrcodegen: compiled in; GoogleTest: fetched by the tests) and doc/ (marker format, integration guide)
  SHA256SUMS

With --verify the extracted archive is configured, built and tested standalone, and the consumer project
(tests/consumer) is built against the tar.gz through FetchContent (URL + SHA256), exactly as the documentation shows.

  python marker/cpp/package_release.py --output <folder> [--verify]
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
MARKER_DIR = CPP_DIR.parent
REPOSITORY_ROOT = MARKER_DIR.parent
EXCLUDED_DIRECTORIES = {"build", "out", ".vs", ".vscode", "__pycache__"}
EXTRA_FILES = {
    "VERSION": MARKER_DIR / "VERSION",
    "LICENSE": MARKER_DIR / "LICENSE",
    "licenses/qrcodegen-MIT.txt": REPOSITORY_ROOT / "licenses" / "qrcodegen-MIT.txt",
    "licenses/googletest-BSD-3-Clause.txt": REPOSITORY_ROOT / "licenses" / "googletest-BSD-3-Clause.txt",
    "doc/marker-format.md": REPOSITORY_ROOT / "doc" / "marker-format.md",
    "doc/integrating.md": REPOSITORY_ROOT / "doc" / "integrating.md",
}


class Arguments(argparse.Namespace):
    """The parsed command line."""

    output: str = ""
    verify: bool = False


def parse_args() -> Arguments:
    parser = argparse.ArgumentParser(description="Create (and verify) the C++ release archives of mb_framemarker.")
    _ = parser.add_argument("--output", required=True, help="Folder for the archives and SHA256SUMS.")
    _ = parser.add_argument("--verify", action="store_true", help="Build and test the extracted archive and consume it through FetchContent.")
    return parser.parse_args(namespace=Arguments())


def run(command: list[str]) -> None:
    print("> " + " ".join(command), flush=True)
    _ = subprocess.run(command, check=True)


def stage(root: Path) -> None:
    """Copy the library source tree and the extra files into root."""
    for path in sorted(CPP_DIR.rglob("*")):
        relative = path.relative_to(CPP_DIR)
        # This script is repository tooling (it reads marker/VERSION and the repository's licenses); the archive does not need it
        if any(part in EXCLUDED_DIRECTORIES for part in relative.parts) or path.is_dir() or path == Path(__file__).resolve():
            continue
        target = root / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        _ = shutil.copy2(path, target)
    for name, source in EXTRA_FILES.items():
        target = root / name
        target.parent.mkdir(parents=True, exist_ok=True)
        _ = shutil.copy2(source, target)


def create_archives(output: Path, version: str) -> list[Path]:
    name = f"mb-framemarker-cpp-{version}"
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="mb-framemarker-stage-") as staging:
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


def verify(tar_path: Path, version: str) -> None:
    with tempfile.TemporaryDirectory(prefix="mb-framemarker-verify-") as work:
        with tarfile.open(tar_path) as archive:
            archive.extractall(work, filter="data")
        source = Path(work) / f"mb-framemarker-cpp-{version}"
        build = Path(work) / "build"
        run(["cmake", "-S", str(source), "-B", str(build), "-DCMAKE_BUILD_TYPE=Release"])
        run(["cmake", "--build", str(build), "--config", "Release", "--parallel"])
        run(["ctest", "--test-dir", str(build), "-C", "Release", "--output-on-failure"])
        run([sys.executable, str(CPP_DIR / "tests" / "consumer" / "check_consumers.py"), "--source", str(source), "--archive", str(tar_path)])


def main() -> int:
    args = parse_args()
    version = (MARKER_DIR / "VERSION").read_text(encoding="utf-8").strip()
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
