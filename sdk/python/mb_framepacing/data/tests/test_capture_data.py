# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""captures.mbcd: header fields at their offsets, newer and foreign files refused, a partial last record ignored."""

import struct
import tempfile
import unittest
from pathlib import Path

from .. import (
    UNKNOWN_TICKS,
    CaptureDataHeader,
    CaptureDataReader,
    CaptureDataStatus,
    DataFormatError,
    Rectangle,
)
from ..capture_data import HEADER_SIZE, MAGIC, RECORD_SIZE


def header_bytes(version: int = 1, markers: int = 1) -> bytearray:
    data = bytearray(HEADER_SIZE)
    struct.pack_into("<IHHII", data, 0, MAGIC, version, HEADER_SIZE, RECORD_SIZE, 1 | 2)
    struct.pack_into("<iiIIii", data, 16, 960, 540, 60000, 1001, 1920, 1080)
    struct.pack_into("<iiii", data, 40, 8, 16, 960, 540)
    struct.pack_into("<I", data, 64, markers)
    for i in range(markers):
        struct.pack_into("<iiiid", data, 72 + (24 * i), 40, 32 + i, 147, 147, 3.0)
    return data


def record_bytes(index: int, device: int, status: int, main: bytes, second: bytes) -> bytes:
    data = bytearray(RECORD_SIZE)
    struct.pack_into("<qqqIBBB", data, 0, index, index * 100, device, 3 if index == 2 else 0, status, len(main), len(second))
    data[32 : 32 + len(main)] = main
    data[112 : 112 + len(second)] = second
    return bytes(data)


class CaptureDataTests(unittest.TestCase):
    def test_the_header_fields_are_where_the_format_says(self) -> None:
        header = CaptureDataHeader.parse(bytes(header_bytes(markers=2)))
        self.assertEqual((header.width, header.height, header.frame_rate_numerator, header.frame_rate_denominator), (960, 540, 60000, 1001))
        self.assertEqual((header.source_width, header.source_height, header.region), (1920, 1080, Rectangle(8, 16, 960, 540)))
        self.assertEqual([m.bounds.y for m in header.markers], [32, 33])
        self.assertTrue(header.frames_stored and header.camera)

    def test_newer_and_foreign_files_are_refused(self) -> None:
        with self.assertRaisesRegex(DataFormatError, "update"):
            _ = CaptureDataHeader.parse(bytes(header_bytes(version=2)))
        with self.assertRaises(DataFormatError):
            _ = CaptureDataHeader.parse(bytes(HEADER_SIZE))
        with self.assertRaises(DataFormatError):
            _ = CaptureDataHeader.parse(bytes(header_bytes(markers=5)))

    def test_records_read_back_and_a_partial_last_record_is_ignored(self) -> None:
        main = bytes(range(80))
        second = bytes([0x5A] * 80)
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "captures.mbcd"
            _ = path.write_bytes(
                bytes(header_bytes()) + record_bytes(0, 200, 1, main, second) + record_bytes(2, UNKNOWN_TICKS, 0, b"", b"") + bytes(RECORD_SIZE // 2)
            )
            with CaptureDataReader(path) as reader:
                self.assertEqual(reader.record_count, 2)
                first, dropped = reader.read_all()
                self.assertEqual(reader.read_record(1), dropped)
        self.assertEqual((first.capture_index, first.host_ticks, first.device_ticks, first.status), (0, 0, 200, CaptureDataStatus.DECODED))
        self.assertEqual((first.main_bytes, first.second_bytes), (main, second))
        self.assertEqual(dropped.source_drops, 3)
        self.assertFalse(dropped.has_device_ticks)
        self.assertIsNone(dropped.main_bytes)


if __name__ == "__main__":
    _ = unittest.main()
