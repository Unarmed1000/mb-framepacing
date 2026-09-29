#!/usr/bin/env python3
# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""Check the Conan recipes (sdk/conan, the conan-center-index layout) the way users get the packages: sdk/conan as a local-recipes-index
remote, then `conan test` of each recipe's test_package, which builds the package from source and runs a program against it.

By default the recipes build this checkout: package_release.py writes the release archives of the current sdk/marker/VERSION and
sdk/data/VERSION, and a copy of sdk/conan gets those versions with the local archives (file:// URL and SHA-256). With --released the
recipes are used as they are: the versions and archives in their conandata.yml (the release workflows add the new one first with
tools/add_conan_version.py).

Everything runs in a temporary CONAN_HOME, so the user's Conan cache and remotes are never touched. Conan comes from next to the Python
that runs this script (requirements-dev.txt), otherwise from PATH.

  python tools/check_conan.py                               # both recipes, built from this checkout
  python tools/check_conan.py --library marker
  python tools/check_conan.py --released --library data --version 0.2.0
"""

import argparse
import hashlib
import os
import shutil
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RECIPES = ROOT / "sdk" / "conan"


@dataclass(frozen=True)
class Recipe:
    name: str
    library: str
    version_file: Path
    package_release: Path
    archive_prefix: str


# In dependency order: the data library needs the marker library
RECIPE_LIST = (
    Recipe("mb-framemarker", "marker", ROOT / "sdk" / "marker" / "VERSION", ROOT / "sdk" / "marker" / "cpp" / "package_release.py", "mb-framemarker-cpp"),
    Recipe("mb-framepacingdata", "data", ROOT / "sdk" / "data" / "VERSION", ROOT / "sdk" / "data" / "cpp" / "package_release.py", "mb-framepacingdata-cpp"),
)


class Arguments(argparse.Namespace):
    library: str = "all"
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


def add_local_version(index: Path, recipe: Recipe, version: str, archive: Path) -> None:
    """Add a version built from a local archive to a copy of the recipes (appended: YAML keeps the last of a repeated key)."""
    folder = index / "recipes" / recipe.name
    config = folder / "config.yml"
    text = config.read_text(encoding="utf-8").replace("versions: {}", "versions:")
    _ = config.write_text(f'{text.rstrip()}\n  "{version}":\n    folder: all\n', encoding="utf-8")
    data = folder / "all" / "conandata.yml"
    text = data.read_text(encoding="utf-8").replace("sources: {}", "sources:")
    url = archive.resolve().as_uri()
    _ = data.write_text(f'{text.rstrip()}\n  "{version}":\n    url: "{url}"\n    sha256: "{sha256(archive)}"\n', encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    _ = parser.add_argument("--library", choices=["all", *(recipe.library for recipe in RECIPE_LIST)], help="check one recipe only (default: all)")
    _ = parser.add_argument("--released", action="store_true", help="use the recipes' own versions and release archives")
    _ = parser.add_argument("--version", help="the version to test (default: the library's VERSION file)")
    args = parser.parse_args(namespace=Arguments())
    if args.version and args.library == "all":
        parser.error("--version needs --library")

    recipes = [recipe for recipe in RECIPE_LIST if args.library in ("all", recipe.library)]
    conan = tool("conan")
    with tempfile.TemporaryDirectory(prefix="mb-framepacing-conan-") as temporary:
        work = Path(temporary)
        env = {**os.environ, "CONAN_HOME": str(work / "home")}
        if args.released:
            index = RECIPES
        else:
            index = work / "index"
            _ = shutil.copytree(RECIPES, index)
            # The data recipe needs the marker package: build it from this checkout too
            for recipe in RECIPE_LIST:
                version = recipe.version_file.read_text(encoding="utf-8").strip()
                run([sys.executable, str(recipe.package_release), "--output", str(work / "dist" / recipe.library)])
                add_local_version(index, recipe, version, work / "dist" / recipe.library / f"{recipe.archive_prefix}-{version}.tar.gz")

        run([conan, "profile", "detect"], env)
        # The index first, so these recipes win over any of the same name elsewhere; ConanCenter for CMake and nlohmann/json
        run([conan, "remote", "add", "mb-framepacing", str(index), "--type", "local-recipes-index", "--index", "0"], env)
        for recipe in recipes:
            version = args.version or recipe.version_file.read_text(encoding="utf-8").strip()
            test_package = index / "recipes" / recipe.name / "all" / "test_package"
            run([conan, "test", str(test_package), f"{recipe.name}/{version}", "--build=missing", "-s", "compiler.cppstd=20"], env)
    print("Conan recipes: OK (" + ", ".join(recipe.name for recipe in recipes) + ")")
    return 0


if __name__ == "__main__":
    sys.exit(main())
