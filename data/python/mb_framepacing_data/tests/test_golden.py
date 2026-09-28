# SPDX-License-Identifier: BSD-3-Clause
# Copyright (c) 2026, Mana Battery ApS

"""The golden data (test-data/data): this library reads what digest.json says, the same values as the C# library."""

import json
import unittest
from typing import cast

from .. import CAPTURE_DATA_FILE_NAME, CaptureDataReader, CaptureDataStatus
from . import digest
from .golden_data import clip_directory

CLIP = clip_directory()


@unittest.skipIf(CLIP is None, "test-data/data not found (a copy of the library outside mb-framepacing)")
class GoldenDataTests(unittest.TestCase):
    def test_the_digest_matches_the_c_sharp_library(self) -> None:
        assert CLIP is not None
        expected = cast(object, json.loads((CLIP / "digest.json").read_text(encoding="utf-8")))
        self.assertEqual(digest.compute(CLIP), expected)

    def test_decoded_records_carry_a_valid_main_payload(self) -> None:
        assert CLIP is not None
        with CaptureDataReader(CLIP / CAPTURE_DATA_FILE_NAME) as reader:
            records = reader.read_all()
        decoded = [r for r in records if r.status == CaptureDataStatus.DECODED]
        self.assertTrue(decoded)
        self.assertTrue(all(r.try_decode_main() is not None for r in decoded))
        self.assertTrue(all(r.try_decode_main() is None for r in records if r.main_bytes is None))


if __name__ == "__main__":
    _ = unittest.main()
