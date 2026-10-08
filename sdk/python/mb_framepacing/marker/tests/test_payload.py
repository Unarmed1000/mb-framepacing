# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""The payload wire format (doc/marker-format.md): layout, round trips and rejections, as the C# library's PayloadTests; and the
sequence id."""

import binascii
import struct
import unittest
import uuid
from datetime import UTC, datetime, timedelta
from typing import cast

from .. import (
    CRC_BYTE_COUNT,
    HEADER_BYTE_COUNT,
    MAX_CPU_BUSY_NS,
    MAX_ENCODED_PAYLOAD_BYTE_COUNT,
    MAX_FRAME_NS,
    NS_PER_SECOND,
    ON_DEMAND_FRAME_NS,
    PAYLOAD_BYTE_COUNT,
    SEQUENCE_ID_BYTE_COUNT,
    START_PAYLOAD_BYTE_COUNT,
    SYNC_PAYLOAD_BYTE_COUNT,
    MarkerFlags,
    MarkerKind,
    Payload,
    SequenceId,
    StartMetadata,
    encode_payload,
    seconds_to_ns,
    to_date_time_ticks,
    try_decode_payload,
)

U64_MAX = 0xFFFF_FFFF_FFFF_FFFF
I64_MAX = 0x7FFF_FFFF_FFFF_FFFF
I64_MIN = -0x8000_0000_0000_0000
U32_MAX = 0xFFFF_FFFF


def with_crc(fields: bytes | bytearray) -> bytes:
    """The payload of fields, its CRC put right: a payload changed on purpose that the CRC does not give away."""
    return bytes(fields) + struct.pack("<I", binascii.crc32(fields))


class PayloadTests(unittest.TestCase):
    def test_sizes(self) -> None:
        self.assertEqual(HEADER_BYTE_COUNT, 53)
        self.assertEqual(CRC_BYTE_COUNT, 4)
        self.assertEqual(PAYLOAD_BYTE_COUNT, 57)
        self.assertEqual(SEQUENCE_ID_BYTE_COUNT, 16)
        self.assertEqual(START_PAYLOAD_BYTE_COUNT, 81)
        self.assertEqual(ON_DEMAND_FRAME_NS, U32_MAX)
        self.assertEqual(MAX_FRAME_NS, U32_MAX - 1)
        self.assertEqual(MAX_CPU_BUSY_NS, U32_MAX)
        self.assertEqual(NS_PER_SECOND, 1_000_000_000)
        self.assertEqual(MAX_ENCODED_PAYLOAD_BYTE_COUNT, START_PAYLOAD_BYTE_COUNT)
        self.assertEqual(SYNC_PAYLOAD_BYTE_COUNT, 20)

    def test_encode_produces_the_documented_little_endian_layout(self) -> None:
        payload = Payload(
            MarkerKind.SEQUENCE_END,
            0x21222324,
            0x0102030405060708,
            MarkerFlags.STATIC_AFTER,
            0x1112131415161718,
            preferred_frame_ns=0x71727374,
            target_frame_ns=0x41424344,
            intended_display_ns=0x3132333435363738,
            cpu_start_ns=0x5152535455565758,
            cpu_busy_ns=0x61626364,
        )
        data = encode_payload(payload)
        expected = (
            b"MF\x01\x02"
            + bytes([0x24, 0x23, 0x22, 0x21])  # run id
            + bytes([8, 7, 6, 5, 4, 3, 2, 1])  # frame index
            + bytes([0x01])  # flags
            + bytes([0x18, 0x17, 0x16, 0x15, 0x14, 0x13, 0x12, 0x11])  # animation time
            + bytes([0x74, 0x73, 0x72, 0x71])  # preferred frame time
            + bytes([0x44, 0x43, 0x42, 0x41])  # target frame time
            + bytes([0x38, 0x37, 0x36, 0x35, 0x34, 0x33, 0x32, 0x31])  # intended display time
            + bytes([0x58, 0x57, 0x56, 0x55, 0x54, 0x53, 0x52, 0x51])  # CPU start time
            + bytes([0x64, 0x63, 0x62, 0x61])  # CPU busy
            + bytes([0x3D, 0x6A, 0xD1, 0x7E])  # CRC: 0x7ED16A3D, the same bytes the C++ and C# tests expect
        )
        self.assertEqual(data, expected)
        self.assertEqual(len(data), PAYLOAD_BYTE_COUNT)

    def test_start_marker_layout(self) -> None:
        payload = Payload(
            MarkerKind.SEQUENCE_START,
            3,
            1,
            MarkerFlags.STATIC_AFTER,
            2,
            preferred_frame_ns=9,
            target_frame_ns=5,
            intended_display_ns=4,
            cpu_start_ns=6,
            cpu_busy_ns=7,
        )
        sequence_id = SequenceId(bytes(range(0xA0, 0xB0)))
        data = encode_payload(payload, StartMetadata(0x0102030405060708, sequence_id))
        self.assertEqual(len(data), START_PAYLOAD_BYTE_COUNT)
        # The same 53 byte header as the other kinds, then the start time and the sequence id, and the CRC of all of it
        end_header = encode_payload(payload.with_kind(MarkerKind.SEQUENCE_END))
        self.assertEqual(data[:3] + data[4:HEADER_BYTE_COUNT], end_header[:3] + end_header[4:HEADER_BYTE_COUNT])
        self.assertEqual(data[3], MarkerKind.SEQUENCE_START)
        self.assertEqual(data[16], 1)
        self.assertEqual(data[25:29], bytes([9, 0, 0, 0]))
        self.assertEqual(data[41:49], bytes([6, 0, 0, 0, 0, 0, 0, 0]))
        self.assertEqual(data[49:53], bytes([7, 0, 0, 0]))
        self.assertEqual(data[53:61], bytes([8, 7, 6, 5, 4, 3, 2, 1]))
        # The sequence id is stored as its bytes, in order
        self.assertEqual(data[61:77], bytes(range(0xA0, 0xB0)))
        self.assertEqual(data[77:], struct.pack("<I", binascii.crc32(data[:77])))

    def test_a_negative_time_is_stored_as_twos_complement(self) -> None:
        self.assertEqual(encode_payload(Payload(MarkerKind.FRAME, 0, 0, MarkerFlags.NO_FLAGS, -1))[17:25], b"\xff" * 8)
        self.assertEqual(encode_payload(Payload(MarkerKind.FRAME, 0, 0, MarkerFlags.NO_FLAGS, 0, intended_display_ns=-2))[33:41], b"\xfe" + (b"\xff" * 7))
        self.assertEqual(encode_payload(Payload(MarkerKind.FRAME, 0, 0, MarkerFlags.NO_FLAGS, 0, cpu_start_ns=-3))[41:49], b"\xfd" + (b"\xff" * 7))

    def test_the_optional_fields_default_to_unknown(self) -> None:
        payload = Payload(MarkerKind.SEQUENCE_END, 3, 1, MarkerFlags.NO_FLAGS, 2)
        self.assertEqual(
            (
                payload.intended_display_ns,
                payload.target_frame_ns,
                payload.cpu_start_ns,
                payload.cpu_busy_ns,
                payload.preferred_frame_ns,
                payload.flags,
            ),
            (0, 0, 0, 0, 0, MarkerFlags.NO_FLAGS),
        )
        self.assertEqual(encode_payload(payload)[24:HEADER_BYTE_COUNT], bytes(29))
        self.assertEqual(StartMetadata(), StartMetadata(0, SequenceId(bytes(16))))

    def test_round_trips(self) -> None:
        for payload in (
            Payload(MarkerKind.FRAME, 0, 0, MarkerFlags.NO_FLAGS, 0),
            Payload(MarkerKind.FRAME, 7, 1, MarkerFlags.NO_FLAGS, 166_667),
            Payload(
                MarkerKind.FRAME,
                U32_MAX,
                U64_MAX,
                MarkerFlags.NO_FLAGS,
                I64_MAX,
                target_frame_ns=U32_MAX,
                intended_display_ns=I64_MAX,
                cpu_start_ns=I64_MAX,
                cpu_busy_ns=U32_MAX,
            ),
            Payload(
                MarkerKind.SEQUENCE_START,
                1,
                7,
                MarkerFlags.NO_FLAGS,
                I64_MIN,
                target_frame_ns=0,
                intended_display_ns=I64_MIN,
                cpu_start_ns=I64_MIN,
                cpu_busy_ns=0,
            ),
            Payload(
                MarkerKind.SEQUENCE_END,
                3,
                42,
                MarkerFlags.NO_FLAGS,
                -1,
                target_frame_ns=33_333_333,
                intended_display_ns=-1,
                cpu_start_ns=-1,
                cpu_busy_ns=1,
            ),
            Payload(MarkerKind.FRAME, 7, 5, MarkerFlags.NO_FLAGS, 6, target_frame_ns=16_666_667),
            Payload(MarkerKind.FRAME, 7, 5, MarkerFlags.NO_FLAGS, 6, cpu_start_ns=123_456_789_012),
            Payload(MarkerKind.FRAME, 7, 5, MarkerFlags.NO_FLAGS, 6, cpu_busy_ns=81_234),
            Payload(MarkerKind.FRAME, 7, 5, MarkerFlags.NO_FLAGS, 6, cpu_busy_ns=U32_MAX),
            Payload(MarkerKind.FRAME, 7, 5, MarkerFlags.NO_FLAGS, 6, cpu_start_ns=123_456_789_012, cpu_busy_ns=500_000),
        ):
            with self.subTest(payload):
                decoded = try_decode_payload(encode_payload(payload))
                self.assertIsNotNone(decoded)
                assert decoded is not None
                self.assertEqual(decoded[0], payload)

    def test_sync_marker_carries_only_the_run_id_and_the_frame_index(self) -> None:
        payload = Payload(
            MarkerKind.SYNC,
            4,
            0x0102030405060708,
            MarkerFlags.NO_FLAGS,
            123,
            target_frame_ns=6,
            intended_display_ns=5,
            cpu_start_ns=7,
            cpu_busy_ns=8,
        )
        data = encode_payload(payload, StartMetadata(7, SequenceId.from_text("ignored")))
        # The header's first 16 bytes and their CRC (0xC0A3D4F2, the same bytes the C++ and C# tests expect)
        self.assertEqual(data, b"MF\x01\x03" + bytes([4, 0, 0, 0]) + bytes([8, 7, 6, 5, 4, 3, 2, 1]) + bytes([0xF2, 0xD4, 0xA3, 0xC0]))
        self.assertEqual(len(data), SYNC_PAYLOAD_BYTE_COUNT)
        self.assertEqual(try_decode_payload(data), (Payload(MarkerKind.SYNC, payload.run_id, payload.frame_index, MarkerFlags.NO_FLAGS, 0), None))

        # Exactly 20 bytes: longer (for example a full header with kind 3) or shorter is rejected, with a CRC that is right too
        self.assertIsNone(try_decode_payload(data + b"\x00"))
        self.assertIsNone(try_decode_payload(data[:-1]))
        self.assertIsNone(try_decode_payload(data[:16]), "without its CRC")
        self.assertIsNone(
            try_decode_payload(with_crc(encode_payload(Payload(MarkerKind.FRAME, 3, 1, MarkerFlags.NO_FLAGS, 2))[:3] + b"\x03" + bytes(HEADER_BYTE_COUNT - 4)))
        )
        # A 20 byte payload of another kind is too short
        self.assertIsNone(try_decode_payload(with_crc(b"MF\x01\x00" + bytes(12))))
        self.assertIsNotNone(try_decode_payload(with_crc(b"MF\x01\x03" + bytes(12))))

    def test_sync_round_trips_and_range(self) -> None:
        for frame_index, run_id in ((0, 0), (1, 7), (42, U32_MAX), (U64_MAX, 1)):
            with self.subTest(frame_index=frame_index, run_id=run_id):
                payload = Payload(MarkerKind.SYNC, run_id, frame_index, MarkerFlags.NO_FLAGS, 0)
                self.assertEqual(try_decode_payload(encode_payload(payload)), (payload, None))
        # Only the run id and the frame index are range checked: the other fields are not encoded
        self.assertEqual(
            len(
                encode_payload(
                    Payload(
                        MarkerKind.SYNC,
                        0,
                        0,
                        MarkerFlags.NO_FLAGS,
                        I64_MAX + 1,
                        target_frame_ns=-1,
                        intended_display_ns=I64_MIN - 1,
                        cpu_start_ns=I64_MAX + 1,
                        cpu_busy_ns=-1,
                    )
                )
            ),
            SYNC_PAYLOAD_BYTE_COUNT,
        )
        for frame_index, run_id in ((-1, 0), (U64_MAX + 1, 0), (0, -1), (0, U32_MAX + 1)):
            with self.subTest(frame_index=frame_index, run_id=run_id), self.assertRaises(ValueError):
                _ = encode_payload(Payload(MarkerKind.SYNC, run_id, frame_index, MarkerFlags.NO_FLAGS, 0))

    def test_start_metadata_round_trips(self) -> None:
        payload = Payload(
            MarkerKind.SEQUENCE_START, 30, 10, MarkerFlags.NO_FLAGS, 20, target_frame_ns=50, intended_display_ns=40, cpu_start_ns=60, cpu_busy_ns=70
        )
        for metadata in (
            StartMetadata(638_000_000_000_000_000, SequenceId.from_text("benchmark-run-01")),
            StartMetadata(0, SequenceId.from_uuid(uuid.UUID("0f8fad5b-d9cb-469f-a165-70867728950e"))),
            StartMetadata(-1, SequenceId(bytes([0xFF]) * 16)),
            StartMetadata(I64_MAX, SequenceId()),
        ):
            with self.subTest(metadata):
                data = encode_payload(payload, metadata)
                self.assertEqual(len(data), START_PAYLOAD_BYTE_COUNT)
                self.assertEqual(try_decode_payload(data), (payload, metadata))

        # A start marker without metadata carries an unknown time and an empty sequence id; frame payloads ignore the metadata
        self.assertEqual(try_decode_payload(encode_payload(payload)), (payload, StartMetadata()))
        self.assertEqual(
            len(encode_payload(Payload(MarkerKind.FRAME, 3, 1, MarkerFlags.NO_FLAGS, 2), StartMetadata(5, SequenceId.from_text("x")))), PAYLOAD_BYTE_COUNT
        )

    def test_encode_rejects_out_of_range_fields(self) -> None:
        start = Payload(MarkerKind.SEQUENCE_START, 3, 1, MarkerFlags.NO_FLAGS, 2)
        for metadata in (StartMetadata(I64_MAX + 1), StartMetadata(I64_MIN - 1)):
            with self.subTest(metadata), self.assertRaises(ValueError):
                _ = encode_payload(start, metadata)
        for payload in (
            Payload(MarkerKind.FRAME, 0, -1, MarkerFlags.NO_FLAGS, 0),
            Payload(MarkerKind.FRAME, 0, U64_MAX + 1, MarkerFlags.NO_FLAGS, 0),
            Payload(MarkerKind.FRAME, 0, 0, MarkerFlags.NO_FLAGS, I64_MAX + 1),
            Payload(MarkerKind.FRAME, U32_MAX + 1, 0, MarkerFlags.NO_FLAGS, 0),
            Payload(MarkerKind.FRAME, 0, 0, MarkerFlags.NO_FLAGS, 0, intended_display_ns=I64_MAX + 1),
            Payload(MarkerKind.FRAME, 0, 0, MarkerFlags.NO_FLAGS, 0, intended_display_ns=I64_MIN - 1),
            Payload(MarkerKind.FRAME, 0, 0, MarkerFlags.NO_FLAGS, I64_MIN - 1),
            Payload(MarkerKind.FRAME, 0, 0, MarkerFlags.NO_FLAGS, 0, target_frame_ns=-1, intended_display_ns=0),
            Payload(MarkerKind.FRAME, 0, 0, MarkerFlags.NO_FLAGS, 0, target_frame_ns=0, intended_display_ns=0, cpu_start_ns=I64_MAX + 1),
            Payload(MarkerKind.FRAME, 0, 0, MarkerFlags.NO_FLAGS, 0, target_frame_ns=0, intended_display_ns=0, cpu_start_ns=I64_MIN - 1),
            Payload(MarkerKind.FRAME, 0, 0, MarkerFlags.NO_FLAGS, 0, cpu_busy_ns=-1),
            Payload(MarkerKind.SEQUENCE_START, 0, 0, MarkerFlags.NO_FLAGS, 0, cpu_busy_ns=-1),
            Payload(MarkerKind.SEQUENCE_END, 0, 0, MarkerFlags.NO_FLAGS, 0, cpu_busy_ns=-1),
            Payload(MarkerKind.SEQUENCE_END, 0, 0, MarkerFlags.NO_FLAGS, 0, cpu_start_ns=I64_MAX + 1),
            Payload(MarkerKind.FRAME, 0, 0, MarkerFlags.NO_FLAGS, 0, preferred_frame_ns=-1),
            Payload(MarkerKind.FRAME, 0, 0, MarkerFlags(0x100), 0),
        ):
            with self.subTest(payload), self.assertRaises(ValueError):
                _ = encode_payload(payload)
        # A negative duration is kept as given (the payload shows what was wrong): it is encode that refuses it
        self.assertEqual(Payload(MarkerKind.FRAME, 0, 0, MarkerFlags.NO_FLAGS, 0, cpu_busy_ns=-1).cpu_busy_ns, -1)
        # A duration longer than its four bytes is no error: the payload holds the longest a marker carries, in every kind
        for kind in (MarkerKind.FRAME, MarkerKind.SEQUENCE_START, MarkerKind.SEQUENCE_END):
            for payload in (
                Payload(kind, 0, 0, MarkerFlags.NO_FLAGS, 0, preferred_frame_ns=U32_MAX + 1),
                Payload(kind, 0, 0, MarkerFlags.NO_FLAGS, 0, target_frame_ns=U32_MAX + 1),
                Payload(kind, 0, 0, MarkerFlags.NO_FLAGS, 0, cpu_busy_ns=U32_MAX + 1),
                Payload(kind, 0, 0, MarkerFlags.NO_FLAGS, 0, preferred_frame_ns=I64_MAX, target_frame_ns=I64_MAX, cpu_busy_ns=I64_MAX),
                Payload(kind, 0, 0, MarkerFlags.NO_FLAGS, 0, preferred_frame_ns=1 << 80, target_frame_ns=1 << 80, cpu_busy_ns=1 << 80),
            ):
                with self.subTest(payload):
                    decoded = try_decode_payload(encode_payload(payload))
                    assert decoded is not None
                    self.assertEqual(decoded[0], payload)

    def test_a_duration_longer_than_its_four_bytes_is_held_as_the_longest_a_marker_carries(self) -> None:
        # The marker's three durations are four unsigned bytes of nanoseconds each: 4.294967295 s at most
        self.assertEqual((MAX_CPU_BUSY_NS, ON_DEMAND_FRAME_NS, MAX_FRAME_NS), (0xFFFF_FFFF, 0xFFFF_FFFF, 0xFFFF_FFFE))
        ten_seconds = 10 * NS_PER_SECOND
        longest = I64_MAX  # the longest duration the C++ and C# libraries hold

        # A longer one is capped where the payload is made (never an error: this runs in a frame loop), so a payload holds what a marker can
        capped = Payload(MarkerKind.FRAME, 1, 2, MarkerFlags.NO_FLAGS, 3, preferred_frame_ns=ten_seconds, target_frame_ns=longest, cpu_busy_ns=ten_seconds)
        self.assertEqual((capped.preferred_frame_ns, capped.target_frame_ns, capped.cpu_busy_ns), (MAX_FRAME_NS, MAX_FRAME_NS, MAX_CPU_BUSY_NS))
        self.assertEqual(capped.cpu_busy_ns, 4_294_967_295)
        self.assertEqual((capped.kind, capped.run_id, capped.frame_index, capped.animation_ns), (MarkerKind.FRAME, 1, 2, 3), "nothing else changes")
        self.assertEqual((capped.intended_display_ns, capped.cpu_start_ns), (0, 0))

        # The longest that fit are kept, and so is on demand: only on demand reads as on demand
        kept = Payload(
            MarkerKind.FRAME, 1, 2, MarkerFlags.NO_FLAGS, 3, preferred_frame_ns=MAX_FRAME_NS, target_frame_ns=ON_DEMAND_FRAME_NS, cpu_busy_ns=MAX_CPU_BUSY_NS
        )
        self.assertEqual((kept.preferred_frame_ns, kept.target_frame_ns, kept.cpu_busy_ns), (MAX_FRAME_NS, ON_DEMAND_FRAME_NS, MAX_CPU_BUSY_NS))
        # One nanosecond past on demand is a frame time again, and too long
        past_on_demand = Payload(MarkerKind.FRAME, 1, 2, MarkerFlags.NO_FLAGS, 3, preferred_frame_ns=4_294_967_296)
        self.assertEqual(past_on_demand.preferred_frame_ns, MAX_FRAME_NS)
        # Another kind of the same payload holds the same values
        end = Payload(
            MarkerKind.SEQUENCE_END, 1, 2, MarkerFlags.NO_FLAGS, 3, preferred_frame_ns=MAX_FRAME_NS, target_frame_ns=MAX_FRAME_NS, cpu_busy_ns=MAX_CPU_BUSY_NS
        )
        self.assertEqual(capped.with_kind(MarkerKind.SEQUENCE_END), end)

        # On the wire: 0xFFFFFFFE for a capped frame time, 0xFFFFFFFF for on demand and for a capped CPU busy
        one_below = bytes([0xFE, 0xFF, 0xFF, 0xFF])
        largest = bytes([0xFF, 0xFF, 0xFF, 0xFF])
        capped_bytes = encode_payload(capped)
        self.assertEqual((capped_bytes[25:29], capped_bytes[29:33], capped_bytes[49:53]), (one_below, one_below, largest))
        kept_bytes = encode_payload(kept)
        self.assertEqual((kept_bytes[25:29], kept_bytes[29:33], kept_bytes[49:53]), (one_below, largest, largest))

        # So a payload decodes to exactly what was encoded, a capped one too
        self.assertEqual(try_decode_payload(capped_bytes), (capped, None))
        self.assertEqual(try_decode_payload(kept_bytes), (kept, None))

    def test_try_decode_rejects_bad_input(self) -> None:
        payload = encode_payload(Payload(MarkerKind.FRAME, 0, 1, MarkerFlags.NO_FLAGS, 2))
        self.assertEqual(len(payload), 57)
        self.assertIsNone(try_decode_payload(payload[:-1]), "short: 56 bytes")
        self.assertIsNone(try_decode_payload(payload[:HEADER_BYTE_COUNT]), "the header alone, as it was before the CRC")
        self.assertIsNone(try_decode_payload(payload[:44]), "an older 44 byte header")
        self.assertIsNone(try_decode_payload(payload[:48]), "an older 48 byte header")
        self.assertIsNone(try_decode_payload(payload + b"\x00"), "long: 58 bytes")
        # Each with the CRC put right, so it is the magic, the format version and the kind that are refused
        data = bytearray(payload[:HEADER_BYTE_COUNT])
        data[0] = ord("X")
        self.assertIsNone(try_decode_payload(with_crc(data)), "magic")
        data[0] = ord("M")
        data[2] = 2
        self.assertIsNone(try_decode_payload(with_crc(data)), "format version")
        data[2] = 1
        data[3] = 4
        self.assertIsNone(try_decode_payload(with_crc(data)), "kind")
        data[3] = 3
        self.assertIsNone(try_decode_payload(with_crc(data)), "a sync kind needs exactly 20 bytes")
        data[3] = 0
        self.assertIsNotNone(try_decode_payload(with_crc(data)))
        self.assertEqual(with_crc(data), payload)

        # A start marker must be exactly 81 bytes: without its metadata, truncated or longer is rejected
        start = encode_payload(Payload(MarkerKind.SEQUENCE_START, 3, 1, MarkerFlags.NO_FLAGS, 2), StartMetadata(5, SequenceId.from_text("run")))
        self.assertEqual(len(start), 81)
        self.assertIsNotNone(try_decode_payload(start))
        for length in (HEADER_BYTE_COUNT, PAYLOAD_BYTE_COUNT, PAYLOAD_BYTE_COUNT + 9, START_PAYLOAD_BYTE_COUNT - CRC_BYTE_COUNT, START_PAYLOAD_BYTE_COUNT - 1):
            with self.subTest(length):
                self.assertIsNone(try_decode_payload(start[:length]))
                self.assertIsNone(try_decode_payload(with_crc(start[: length - CRC_BYTE_COUNT])), "with a CRC that is right")
        self.assertIsNone(try_decode_payload(start + b"\x00"), "long: 82 bytes")
        older = with_crc(start[:48] + start[HEADER_BYTE_COUNT:-CRC_BYTE_COUNT])
        self.assertIsNone(try_decode_payload(older), "the older 72 byte start marker, with a CRC")

    def test_the_crc_is_the_standard_one(self) -> None:
        # The check value every description of the CRC-32 of zlib, PNG and Ethernet gives, and the values the C++ and C# tests pin
        self.assertEqual(binascii.crc32(b"123456789"), 0xCBF43926)
        self.assertEqual(binascii.crc32(bytes(32)), 0x190A55AD)
        self.assertEqual(binascii.crc32(b"\xff" * 32), 0xFF6CAB0B)
        payload = encode_payload(Payload(MarkerKind.FRAME, 7, 1000, MarkerFlags.NO_FLAGS, 166_667))
        self.assertEqual(payload[-CRC_BYTE_COUNT:], struct.pack("<I", binascii.crc32(payload[:-CRC_BYTE_COUNT])), "little endian, over all before it")

    def test_a_changed_bit_is_refused(self) -> None:
        # Every single bit of every kind's payload, the CRC's own bits too: none decodes, and the payload put back decodes again
        fields = Payload(
            MarkerKind.FRAME,
            0x21222324,
            0x0102030405060708,
            MarkerFlags.STATIC_AFTER,
            0x1112131415161718,
            preferred_frame_ns=0x71727374,
            target_frame_ns=0x41424344,
            intended_display_ns=0x3132333435363738,
            cpu_start_ns=0x5152535455565758,
            cpu_busy_ns=0x61626364,
        )
        metadata = StartMetadata(638_000_000_000_000_000, SequenceId.from_text("a changed bit"))
        for kind in MarkerKind:
            data = bytearray(encode_payload(fields.with_kind(kind), metadata))
            self.assertIsNotNone(try_decode_payload(bytes(data)), kind)
            for bit in range(len(data) * 8):
                data[bit // 8] ^= 1 << (bit % 8)
                self.assertIsNone(try_decode_payload(bytes(data)), f"{kind!r}, bit {bit}")
                data[bit // 8] ^= 1 << (bit % 8)
            self.assertIsNotNone(try_decode_payload(bytes(data)), kind)

    def test_a_field_changed_without_its_crc_is_refused(self) -> None:
        # What a QR decoder's error correction can hand back for a symbol that mixes two frames: a well-formed payload of bytes that
        # were never drawn. Only the CRC tells
        data = bytearray(encode_payload(Payload(MarkerKind.FRAME, 7, 1000, MarkerFlags.NO_FLAGS, 166_667)))
        data[8] = 0xE9
        self.assertIsNone(try_decode_payload(bytes(data)))
        decoded = try_decode_payload(with_crc(data[:-CRC_BYTE_COUNT]))
        assert decoded is not None
        self.assertEqual(decoded[0].frame_index, 1001)

    def test_preferred_frame_time_and_flags_round_trip(self) -> None:
        for payload in (
            Payload(
                MarkerKind.FRAME,
                3,
                1,
                MarkerFlags.NO_FLAGS,
                2,
                preferred_frame_ns=16_666_667,
                target_frame_ns=33_333_333,
                intended_display_ns=4,
                cpu_start_ns=6,
                cpu_busy_ns=7,
            ),
            Payload(
                MarkerKind.FRAME,
                3,
                1,
                MarkerFlags.STATIC_AFTER,
                2,
                preferred_frame_ns=ON_DEMAND_FRAME_NS,
                target_frame_ns=ON_DEMAND_FRAME_NS,
                intended_display_ns=0,
                cpu_start_ns=0,
                cpu_busy_ns=0,
            ),
            Payload(
                MarkerKind.SEQUENCE_END,
                3,
                1,
                MarkerFlags.STATIC_AFTER,
                2,
                preferred_frame_ns=10_000_000,
                target_frame_ns=5,
                intended_display_ns=4,
                cpu_start_ns=6,
                cpu_busy_ns=7,
            ),
            Payload(
                MarkerKind.FRAME,
                3,
                1,
                MarkerFlags.STATIC_BEFORE,
                2,
                preferred_frame_ns=16_666_667,
                target_frame_ns=5,
                intended_display_ns=4,
                cpu_start_ns=6,
                cpu_busy_ns=7,
            ),
            Payload(
                MarkerKind.FRAME,
                3,
                1,
                MarkerFlags.STATIC_AFTER | MarkerFlags.STATIC_BEFORE,
                2,
                preferred_frame_ns=16_666_667,
                target_frame_ns=5,
                intended_display_ns=4,
                cpu_start_ns=6,
                cpu_busy_ns=7,
            ),
            # A reserved bit survives the round trip
            Payload(MarkerKind.FRAME, 3, 1, MarkerFlags(0x81), 2),
        ):
            with self.subTest(payload):
                decoded = try_decode_payload(encode_payload(payload))
                self.assertIsNotNone(decoded)
                assert decoded is not None
                self.assertEqual(decoded[0], payload)
                self.assertEqual(int(decoded[0].flags), int(payload.flags))

    def test_the_start_time_is_in_date_time_ticks(self) -> None:
        time = datetime(2026, 1, 1, tzinfo=UTC)
        self.assertEqual(to_date_time_ticks(time), 639_028_224_000_000_000)
        self.assertEqual(to_date_time_ticks(time + timedelta(microseconds=1)), 639_028_224_000_000_010)
        self.assertEqual(to_date_time_ticks(datetime(2026, 1, 1)), to_date_time_ticks(datetime(2026, 1, 1).astimezone()), "a naive time is local")

    def test_seconds_are_truncated_to_a_nanosecond_as_in_cpp_and_csharp(self) -> None:
        self.assertEqual(seconds_to_ns(1.5), 1_500_000_000)
        self.assertEqual(seconds_to_ns(1.0 / 60), 16_666_666, "NanosecondTimeSpan's FromSeconds gives this nanosecond in C++ and C#")
        self.assertEqual(seconds_to_ns(-1.0 / 60), -16_666_666, "toward zero")
        self.assertEqual(seconds_to_ns(0.999_999_99e-9), 0)
        self.assertEqual(seconds_to_ns(-0.999_999_99e-9), 0)
        self.assertEqual(seconds_to_ns(2), 2_000_000_000)
        self.assertIs(type(seconds_to_ns(0.25)), int)
        with self.assertRaises(ValueError):
            _ = seconds_to_ns(float("nan"))
        with self.assertRaises(OverflowError):
            _ = seconds_to_ns(float("inf"))

    def test_a_kind_that_is_not_a_marker_kind_is_not_encoded(self) -> None:
        def with_kind(kind: int) -> Payload:
            # A number where the type says MarkerKind: what a caller without a type checker can pass
            return Payload(cast(MarkerKind, cast(object, kind)), 1, 2, MarkerFlags.NO_FLAGS, 3)

        for kind in (4, 7, 255, -1):
            with self.subTest(kind), self.assertRaises(ValueError):
                _ = encode_payload(with_kind(kind))
        # The plain number of a kind is that kind
        self.assertEqual(encode_payload(with_kind(0)), encode_payload(Payload(MarkerKind.FRAME, 1, 2, MarkerFlags.NO_FLAGS, 3)))
        self.assertEqual(len(encode_payload(with_kind(3))), 20)


class SequenceIdTests(unittest.TestCase):
    def test_holds_exactly_16_bytes(self) -> None:
        self.assertEqual(SequenceId().data, bytes(16))
        self.assertTrue(SequenceId().is_empty)
        self.assertFalse(SequenceId(bytes(15) + b"\x01").is_empty)
        for length in (0, 15, 17):
            with self.subTest(length), self.assertRaises(ValueError):
                _ = SequenceId(bytes(length))

    def test_from_text_pads_with_zero_bytes(self) -> None:
        self.assertEqual(SequenceId.from_text("run 7").data, b"run 7" + bytes(11))
        self.assertEqual(SequenceId.from_text("~" * 16).data, b"~" * 16)
        self.assertEqual(SequenceId.from_text(" ").data, b" " + bytes(15))

    def test_from_text_rejects_anything_but_1_to_16_printable_ascii_characters(self) -> None:
        for text in ("", "x" * 17, "æ", "tab\there", "nul\x00", "del\x7f"):
            with self.subTest(text), self.assertRaises(ValueError):
                _ = SequenceId.from_text(text)

    def test_from_uuid_stores_the_bytes_in_the_order_of_the_text(self) -> None:
        value = uuid.UUID("0f8fad5b-d9cb-469f-a165-70867728950e")
        sequence_id = SequenceId.from_uuid(value)
        self.assertEqual(sequence_id.data, bytes.fromhex("0f8fad5bd9cb469fa16570867728950e"))
        self.assertEqual(str(sequence_id), "0f8fad5b-d9cb-469f-a165-70867728950e")

    def test_display_form(self) -> None:
        # Printable ASCII followed only by zero bytes shows as text, anything else in the 8-4-4-4-12 form
        self.assertEqual(str(SequenceId.from_text("benchmark-run-01")), "benchmark-run-01")
        self.assertEqual(str(SequenceId.from_text("run 7")), "run 7")
        self.assertEqual(str(SequenceId(b"ab\x00c" + bytes(12))), "61620063-0000-0000-0000-000000000000")
        self.assertEqual(str(SequenceId(b"\x00run" + bytes(12))), "0072756e-0000-0000-0000-000000000000")
        self.assertEqual(str(SequenceId()), "00000000-0000-0000-0000-000000000000")
        self.assertEqual(str(SequenceId(bytes([0xFF]) * 16)), "ffffffff-ffff-ffff-ffff-ffffffffffff")
        self.assertEqual(str(SequenceId(bytes(range(0xA0, 0xB0)))), "a0a1a2a3-a4a5-a6a7-a8a9-aaabacadaeaf")


if __name__ == "__main__":
    _ = unittest.main()
