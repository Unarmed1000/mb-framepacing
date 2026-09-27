# SPDX-License-Identifier: BSD-3-Clause
# Copyright (c) 2026, Mana Battery ApS

"""The payload wire format (doc/marker-format.md): layout, round trips and rejections, as the C# library's PayloadTests."""

import unittest
from datetime import UTC, datetime, timedelta

from .. import (
    MAX_ENCODED_PAYLOAD_BYTE_COUNT,
    MAX_START_NAME_BYTES,
    PAYLOAD_BYTE_COUNT,
    START_PAYLOAD_FIXED_BYTE_COUNT,
    MarkerKind,
    Payload,
    StartMetadata,
    encode_payload,
    seconds_to_ticks,
    to_date_time_ticks,
    try_decode_payload,
)

U64_MAX = 0xFFFF_FFFF_FFFF_FFFF
I64_MAX = 0x7FFF_FFFF_FFFF_FFFF
I64_MIN = -0x8000_0000_0000_0000
U32_MAX = 0xFFFF_FFFF


class PayloadTests(unittest.TestCase):
    def test_encode_produces_the_documented_little_endian_layout(self) -> None:
        data = encode_payload(Payload(0x0102030405060708, 0x1112131415161718, 0x21222324, MarkerKind.SEQUENCE_END))
        expected = b"MF\x01\x02" + bytes([8, 7, 6, 5, 4, 3, 2, 1, 0x18, 0x17, 0x16, 0x15, 0x14, 0x13, 0x12, 0x11, 0x24, 0x23, 0x22, 0x21])
        self.assertEqual(data, expected)
        self.assertEqual(len(data), PAYLOAD_BYTE_COUNT)

    def test_negative_ticks_are_stored_as_twos_complement(self) -> None:
        self.assertEqual(encode_payload(Payload(0, -1))[12:20], b"\xff" * 8)

    def test_round_trips(self) -> None:
        for payload in (
            Payload(0, 0, 0),
            Payload(1, 166_667, 7),
            Payload(U64_MAX, I64_MAX, U32_MAX),
            Payload(7, I64_MIN, 1, MarkerKind.SEQUENCE_START),
            Payload(42, -1, 3, MarkerKind.SEQUENCE_END),
        ):
            with self.subTest(payload):
                decoded = try_decode_payload(encode_payload(payload))
                self.assertIsNotNone(decoded)
                assert decoded is not None
                self.assertEqual(decoded[0], payload)

    def test_start_metadata_round_trips(self) -> None:
        payload = Payload(10, 20, 30, MarkerKind.SEQUENCE_START)
        name = "Benchmark æøå run"
        data = encode_payload(payload, StartMetadata(638_000_000_000_000_000, name))
        self.assertEqual(len(data), START_PAYLOAD_FIXED_BYTE_COUNT + len(name.encode("utf-8")))
        self.assertEqual(try_decode_payload(data), (payload, StartMetadata(638_000_000_000_000_000, name)))

        # A truncated name is rejected; frame payloads ignore the metadata
        self.assertIsNone(try_decode_payload(data[:-1]))
        self.assertEqual(len(encode_payload(Payload(1, 2, 3), StartMetadata(5, name))), PAYLOAD_BYTE_COUNT)

    def test_encode_rejects_long_names_and_out_of_range_fields(self) -> None:
        start = Payload(1, 2, 3, MarkerKind.SEQUENCE_START)
        self.assertEqual(len(encode_payload(start, StartMetadata(0, "x" * MAX_START_NAME_BYTES))), MAX_ENCODED_PAYLOAD_BYTE_COUNT)
        with self.assertRaises(ValueError):
            _ = encode_payload(start, StartMetadata(0, "x" * (MAX_START_NAME_BYTES + 1)))
        with self.assertRaises(ValueError, msg="66 UTF-8 bytes"):
            _ = encode_payload(start, StartMetadata(0, "æ" * 33))
        self.assertEqual(len(encode_payload(start)), START_PAYLOAD_FIXED_BYTE_COUNT)
        for payload in (Payload(-1, 0), Payload(U64_MAX + 1, 0), Payload(0, I64_MAX + 1), Payload(0, 0, U32_MAX + 1)):
            with self.subTest(payload), self.assertRaises(ValueError):
                _ = encode_payload(payload)

    def test_try_decode_rejects_bad_input(self) -> None:
        data = bytearray(encode_payload(Payload(1, 2)))
        self.assertIsNone(try_decode_payload(bytes(data[:-1])), "short")
        data[0] = ord("X")
        self.assertIsNone(try_decode_payload(bytes(data)), "magic")
        data[0] = ord("M")
        data[2] = 2
        self.assertIsNone(try_decode_payload(bytes(data)), "format version")
        data[2] = 1
        data[3] = 3
        self.assertIsNone(try_decode_payload(bytes(data)), "kind")
        data[3] = 0
        self.assertIsNotNone(try_decode_payload(bytes(data)))

        # A start marker without its metadata block, and one whose name is not valid UTF-8
        start = bytearray(encode_payload(Payload(1, 2, 3, MarkerKind.SEQUENCE_START)) + b"\x00")
        self.assertIsNone(try_decode_payload(bytes(start[:PAYLOAD_BYTE_COUNT])))
        start[START_PAYLOAD_FIXED_BYTE_COUNT - 1] = 1
        start[START_PAYLOAD_FIXED_BYTE_COUNT] = 0xC3
        self.assertIsNone(try_decode_payload(bytes(start)))

    def test_ticks_match_date_time_and_time_span(self) -> None:
        time = datetime(2026, 1, 1, tzinfo=UTC)
        self.assertEqual(to_date_time_ticks(time), 639_028_224_000_000_000)
        self.assertEqual(to_date_time_ticks(time + timedelta(microseconds=1)), 639_028_224_000_000_010)
        self.assertEqual(seconds_to_ticks(1.5), 15_000_000)
        self.assertEqual(seconds_to_ticks(1.0 / 60), 166_667)


if __name__ == "__main__":
    _ = unittest.main()
