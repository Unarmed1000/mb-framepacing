# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""captures.mbcd: header fields at their offsets, newer and foreign files refused, a partial last record ignored."""

import struct
import tempfile
import unittest
from pathlib import Path

from ...marker import MarkerFlags, MarkerKind, Payload, SequenceId, StartMetadata, encode_payload
from .. import (
    UNKNOWN_TICKS,
    CaptureDataHeader,
    CaptureDataReader,
    CaptureDataRecord,
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
    # The sync marker's region, after the marker locations
    struct.pack_into("<iiii", data, 168, 14, 820, 240, 246)
    return data


def record_bytes(index: int, device: int, status: int, main: bytes, second: bytes) -> bytes:
    data = bytearray(RECORD_SIZE)
    struct.pack_into("<qqqIBBB", data, 0, index, index * 100, device, 3 if index == 2 else 0, status, len(main), len(second))
    data[32 : 32 + len(main)] = main
    data[144 : 144 + len(second)] = second
    return bytes(data)


class CaptureDataTests(unittest.TestCase):
    def test_the_header_fields_are_where_the_format_says(self) -> None:
        header = CaptureDataHeader.parse(bytes(header_bytes(markers=2)))
        self.assertEqual((header.width, header.height, header.frame_rate_numerator, header.frame_rate_denominator), (960, 540, 60000, 1001))
        self.assertEqual((header.source_width, header.source_height, header.region), (1920, 1080, Rectangle(8, 16, 960, 540)))
        self.assertEqual(header.sync_region, Rectangle(14, 820, 240, 246))
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
        main = bytes(range(112))
        second = bytes([0x5A] * 112)
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "captures.mbcd"
            _ = path.write_bytes(
                bytes(header_bytes()) + record_bytes(0, 200, 1, main, second) + record_bytes(2, UNKNOWN_TICKS, 0, b"", b"") + bytes(RECORD_SIZE // 2)
            )
            with CaptureDataReader(path) as reader:
                self.assertEqual(reader.record_count, 2)
                first, dropped = reader.read_all()
                self.assertEqual(reader.read_record(1), dropped)
        self.assertEqual((first.capture_index, first.host_ticks, first.device_ticks, first.capture_status), (0, 0, 200, CaptureDataStatus.DECODED))
        self.assertEqual((first.main_bytes, first.second_bytes), (main, second))
        self.assertEqual(dropped.source_drops, 3)
        self.assertFalse(dropped.has_device_ticks)
        self.assertIsNone(dropped.main_bytes)

    def test_a_header_that_is_not_the_format_is_refused(self) -> None:
        def with_field(offset: int, fmt: str, value: int) -> bytes:
            data = header_bytes()
            struct.pack_into(fmt, data, offset, value)
            return bytes(data)

        self.assertEqual(CaptureDataHeader.parse(bytes(header_bytes())).width, 960, "the bytes these cases change one field of")
        cases = {
            "a byte short": bytes(header_bytes())[:-1],
            "nothing": b"",
            "another magic": with_field(0, "<I", MAGIC + 1),
            "version 0": with_field(4, "<H", 0),
            "another header size": with_field(6, "<H", HEADER_SIZE - 1),
            "another record size": with_field(8, "<I", RECORD_SIZE - 1),
            "five markers": with_field(64, "<I", 5),
        }
        for what, data in cases.items():
            with self.subTest(what), self.assertRaises(DataFormatError):
                _ = CaptureDataHeader.parse(data)
        header = CaptureDataHeader.parse(with_field(12, "<I", 0) + b"more bytes than a header")
        self.assertEqual((header.frames_stored, header.camera), (False, False))
        self.assertEqual(CaptureDataHeader.parse(with_field(64, "<I", 0)).markers, ())

    def test_a_record_that_is_not_one_is_refused(self) -> None:
        good = record_bytes(7, 200, 2, b"main", b"second")
        record = CaptureDataRecord.parse(good)
        self.assertEqual((record.capture_index, record.capture_status, record.main_bytes, record.second_bytes), (7, CaptureDataStatus.TORN, b"main", b"second"))
        self.assertEqual(CaptureDataRecord.parse(memoryview(good + b"more")), record, "a view, and more bytes than a record")
        cases = {
            "a byte short": good[:-1],
            "an unknown status": record_bytes(7, 200, 3, b"", b""),
            "a main marker longer than its slot": record_bytes(7, 200, 1, bytes(113), b"")[:RECORD_SIZE],
            "a second marker longer than its slot": record_bytes(7, 200, 1, b"", bytes(113))[:RECORD_SIZE],
        }
        for what, data in cases.items():
            with self.subTest(what), self.assertRaises(DataFormatError):
                _ = CaptureDataRecord.parse(data)
        self.assertEqual(len(CaptureDataRecord.parse(record_bytes(7, 200, 1, bytes(112), bytes(112))).second_bytes or b""), 112)
        self.assertEqual(RECORD_SIZE, 256)

    def test_a_records_markers_decode(self) -> None:
        main = encode_payload(Payload(MarkerKind.SEQUENCE_START, 7, 12, MarkerFlags.STATIC_AFTER, 34), StartMetadata(5, SequenceId.from_text("run 7")))
        sync = encode_payload(Payload(MarkerKind.SYNC, 7, 11, MarkerFlags.NO_FLAGS, 0))
        self.assertEqual((len(main), len(sync)), (81, 20), "the longest and the shortest marker")
        record = CaptureDataRecord.parse(record_bytes(5, 200, 2, main, sync))
        decoded = record.try_decode_main()
        assert decoded is not None
        payload, metadata = decoded
        self.assertEqual((payload.kind, payload.run_id, payload.frame_index, payload.animation_ns), (MarkerKind.SEQUENCE_START, 7, 12, 34))
        assert metadata is not None
        self.assertEqual((metadata.utc_ticks, str(metadata.sequence_id)), (5, "run 7"))
        second = record.try_decode_second()
        assert second is not None
        self.assertEqual((second.kind, second.run_id, second.frame_index), (MarkerKind.SYNC, 7, 11))

        # A record without markers, and bytes that are no marker
        none = CaptureDataRecord.parse(record_bytes(5, 200, 0, b"", b""))
        self.assertEqual((none.try_decode_main(), none.try_decode_second()), (None, None))
        garbage = CaptureDataRecord.parse(record_bytes(5, 200, 1, bytes(57), b"\x01\x02\x03"))
        self.assertEqual((garbage.try_decode_main(), garbage.try_decode_second()), (None, None))

    def test_records_are_read_in_order_whatever_is_read_between_them(self) -> None:
        count = 5000  # more than one batch of records()
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "captures.mbcd"
            _ = path.write_bytes(bytes(header_bytes()) + b"".join(record_bytes(i, i * 3, 1, b"", b"") for i in range(count)))
            with CaptureDataReader(path) as reader:
                self.assertEqual(reader.record_count, count)
                indices: list[int] = []
                for record in reader.records():
                    indices.append(record.capture_index)
                    # Reading one record by its index must not move where records() reads its next batch
                    if record.capture_index in (0, 4095, 4096):
                        self.assertEqual(reader.read_record(count - 1).capture_index, count - 1)
                self.assertEqual(indices, list(range(count)))
                # Two walks at once each see every record
                pairs = list(zip(reader.records(), reader.records(), strict=True))
                self.assertEqual([(a.capture_index, b.capture_index) for a, b in pairs], [(i, i) for i in range(count)])
                self.assertEqual([r.capture_index for r in reader.read_all()], list(range(count)))
                for index in (-1, count):
                    with self.subTest(index), self.assertRaises(IndexError):
                        _ = reader.read_record(index)

    def test_what_is_no_capture_data_file_is_refused(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "captures.mbcd"
            for what, content in (("empty", b""), ("ends in its header", bytes(header_bytes())[:-1]), ("another file", bytes(HEADER_SIZE))):
                _ = path.write_bytes(content)
                with self.subTest(what), self.assertRaises(DataFormatError):
                    _ = CaptureDataReader(path)
                # A refused file is closed again: it can be replaced
                path.unlink()
            with self.assertRaises(FileNotFoundError):
                _ = CaptureDataReader(path)
            _ = path.write_bytes(bytes(header_bytes()))
            reader = CaptureDataReader(str(path))
            self.assertEqual((reader.record_count, reader.read_all()), (0, []))
            reader.close()
            path.unlink()


if __name__ == "__main__":
    _ = unittest.main()
