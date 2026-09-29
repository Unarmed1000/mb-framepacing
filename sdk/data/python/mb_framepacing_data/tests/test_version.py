# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""The package's version is the data libraries' version (sdk/data/VERSION in mb-framepacing), like the C# library's."""

import unittest
from pathlib import Path

from .. import __version__


class VersionTests(unittest.TestCase):
    def test_the_version_is_the_data_libraries(self) -> None:
        version = next((folder / "data" / "VERSION" for folder in Path(__file__).resolve().parents if (folder / "data" / "VERSION").is_file()), None)
        if version is None:
            self.skipTest("sdk/data/VERSION not found above the tests: a copy of the library outside mb-framepacing")
        self.assertEqual(__version__, version.read_text(encoding="utf-8").strip())


if __name__ == "__main__":
    _ = unittest.main()
