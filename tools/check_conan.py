#!/usr/bin/env python3
# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""Check the Conan recipe (sdk/cpp/conan, the conan-center-index layout) the way users get the package: sdk/cpp/conan as a
local-recipes-index remote, then `conan test` of the recipe's test_package, which builds the package from source and runs a program against
it. The package is built with its default modules (the pacer is off by default), and once more with only the core and the marker module
(with_data=False: no nlohmann/json; with_pacer=False).

By default the recipe builds this checkout: sdk/cpp/package_release.py writes the release archive of the current sdk/VERSION, and a copy of
sdk/cpp/conan gets that version with the local archive (file:// URL and SHA-256). With --released the recipe is used as it is: the versions
and archives in its conandata.yml (the release workflow adds the new one first with tools/add_conan_version.py).

Everything runs in a temporary CONAN_HOME, so the user's Conan cache and remotes are never touched. Conan comes from next to the Python
that runs this script (uv run: the version pyproject.toml pins), otherwise from PATH.

  python tools/check_conan.py                               # built from this checkout
  python tools/check_conan.py --released --version 0.2.0
"""

import argparse
import hashlib
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RECIPES = ROOT / "sdk" / "cpp" / "conan"
RECIPE = "mb-framepacing"
VERSION_FILE = ROOT / "sdk" / "VERSION"
PACKAGE_RELEASE = ROOT / "sdk" / "cpp" / "package_release.py"
ARCHIVE_PREFIX = "mb-framepacing-cpp"
# The package is built and tested with its default modules, then built with the core and marker modules only (conan install --build=missing);
# the test package needs the data module too, so that second package is not run against
CORE_AND_MARKER = ("-o", f"{RECIPE}/*:with_data=False", "-o", f"{RECIPE}/*:with_pacer=False")


class Arguments(argparse.Namespace):
    released: bool = False
    version: str | None = None


def tool(name: str) -> str:
    """The tool installed next to the running Python (a venv's Scripts or bin folder), else the name for a PATH lookup."""
    exe = name + ".exe" if sys.platform == "win32" else name
    here = Path(sys.executable).parent
    for folder in (here, here / "Scripts", here / "bin"):
        if (folder / exe).is_file():
            return str(folder / exe)
    return name


def run(command: list[str], env: dict[str, str] | None = None) -> None:
    print("> " + " ".join(command), flush=True)
    _ = subprocess.run(command, check=True, env=env)


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def add_local_version(index: Path, version: str, archive: Path) -> None:
    """Add a version built from a local archive to a copy of the recipes (appended: YAML keeps the last of a repeated key)."""
    folder = index / "recipes" / RECIPE
    config = folder / "config.yml"
    text = config.read_text(encoding="utf-8").replace("versions: {}", "versions:")
    _ = config.write_text(f'{text.rstrip()}\n  "{version}":\n    folder: all\n', encoding="utf-8")
    data = folder / "all" / "conandata.yml"
    text = data.read_text(encoding="utf-8").replace("sources: {}", "sources:")
    url = archive.resolve().as_uri()
    _ = data.write_text(f'{text.rstrip()}\n  "{version}":\n    url: "{url}"\n    sha256: "{sha256(archive)}"\n', encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    _ = parser.add_argument("--released", action="store_true", help="use the recipe's own versions and release archives")
    _ = parser.add_argument("--version", help="the version to test (default: sdk/VERSION)")
    args = parser.parse_args(namespace=Arguments())

    version = args.version or VERSION_FILE.read_text(encoding="utf-8").strip()
    conan = tool("conan")
    with tempfile.TemporaryDirectory(prefix="mb-framepacing-conan-") as temporary:
        work = Path(temporary)
        env = {**os.environ, "CONAN_HOME": str(work / "home")}
        if args.released:
            index = RECIPES
        else:
            index = work / "index"
            _ = shutil.copytree(RECIPES, index)
            run([sys.executable, str(PACKAGE_RELEASE), "--output", str(work / "dist")])
            add_local_version(index, version, work / "dist" / f"{ARCHIVE_PREFIX}-{version}.tar.gz")

        run([conan, "profile", "detect"], env)
        # The index first, so this recipe wins over any of the same name elsewhere; ConanCenter for CMake and nlohmann/json
        run([conan, "remote", "add", "mb-framepacing", str(index), "--type", "local-recipes-index", "--index", "0"], env)
        reference = f"{RECIPE}/{version}"
        test_package = index / "recipes" / RECIPE / "all" / "test_package"
        run([conan, "test", str(test_package), reference, "--build=missing", "-s", "compiler.cppstd=20"], env)
        # --output-folder: conan install writes its generated files there, never into the current folder
        core_and_marker = ["--output-folder", str(work / "core-and-marker"), *CORE_AND_MARKER]
        run([conan, "install", f"--requires={reference}", "--build=missing", "-s", "compiler.cppstd=20", *core_and_marker], env)
    print(f"Conan recipe: OK ({RECIPE}/{version}: tested with the default modules, built with the core and marker modules only)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
