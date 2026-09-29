# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""The package's version is the marker libraries' version (sdk/marker/VERSION in mb-framepacing, spelled as PEP 440 does), like the C++ and C# libraries'."""

import tomllib
import unittest
from pathlib import Path
from typing import cast

from .. import __version__

# PEP 440 spells a pre-release without the dash: 0.2.0-beta.1 is 0.2.0b1
PEP440_LABELS = {"alpha": "a", "beta": "b", "rc": "rc"}


def python_version(text: str) -> str:
    """A VERSION file's version (MAJOR.MINOR.PATCH, optionally -alpha.N, -beta.N or -rc.N) as Python spells it."""
    numbers, _, prerelease = text.partition("-")
    if not prerelease:
        return numbers
    label, _, number = prerelease.partition(".")
    return f"{numbers}{PEP440_LABELS[label]}{number}"


class VersionTests(unittest.TestCase):
    def test_a_prerelease_is_spelled_as_pep_440_does(self) -> None:
        self.assertEqual(python_version("0.2.0"), "0.2.0")
        self.assertEqual(python_version("0.2.0-alpha.1"), "0.2.0a1")
        self.assertEqual(python_version("0.2.0-beta.2"), "0.2.0b2")
        self.assertEqual(python_version("1.0.0-rc.10"), "1.0.0rc10")

    def test_the_version_is_the_marker_libraries(self) -> None:
        version = next((folder / "marker" / "VERSION" for folder in Path(__file__).resolve().parents if (folder / "marker" / "VERSION").is_file()), None)
        if version is None:
            self.skipTest("sdk/marker/VERSION not found above the tests: a copy of the library outside mb-framepacing")
        self.assertEqual(__version__, python_version(version.read_text(encoding="utf-8").strip()))

    def test_the_package_metadata_has_the_same_version(self) -> None:
        pyproject = Path(__file__).resolve().parents[2] / "pyproject.toml"
        if not pyproject.is_file():
            self.skipTest("pyproject.toml not found: an installed copy of the library")
        with pyproject.open("rb") as file:
            project = cast(dict[str, object], tomllib.load(file)["project"])
        self.assertEqual(project["version"], __version__)


if __name__ == "__main__":
    _ = unittest.main()
