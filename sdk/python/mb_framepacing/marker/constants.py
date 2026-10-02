# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""Constants of the frame marker format and geometry. See doc/marker-format.md for the full specification."""

from .structures import SEQUENCE_ID_BYTE_COUNT, MarkerKind, packed_module_byte_count

QR_VERSION = 6
"""Every main marker (frame, start and end) is QR version 6 (41x41 modules), error correction level M, byte mode, so the marker never
changes size."""
QR_MODULE_COUNT = (4 * QR_VERSION) + 17
QR_CAPACITY_BYTES = 106
"""Version 6-M holds 106 bytes: a frame or end marker uses PAYLOAD_BYTE_COUNT of them, a start marker START_PAYLOAD_BYTE_COUNT; the rest
is room for future fields."""

SYNC_QR_VERSION = 2
"""The sync marker (MarkerKind.SYNC) is QR version 2 (25x25 modules), error correction level M: magic | format version | kind | run id
u32 | frame index u64."""
SYNC_QR_MODULE_COUNT = (4 * SYNC_QR_VERSION) + 17
SYNC_PAYLOAD_BYTE_COUNT = 16

PAYLOAD_BYTE_COUNT = 53
"""Payload header, shared by every marker kind (little endian), grouped: magic "MF" | format version | kind | run id u32 | frame index
u64 | flags u8 | animation ticks i64 | preferred frame ticks u32 | target frame ticks u32 | intended display ticks i64 | CPU start ticks
i64 | CPU busy ticks u32. Start and end markers carry the values of the frame that shows them."""
PAYLOAD_MAGIC = b"MF"
PAYLOAD_FORMAT_VERSION = 1

ON_DEMAND_FRAME_TICKS = 0xFFFF_FFFF
"""The target and preferred frame time of a renderer that presents only when something changes: there is no interval to aim for."""

START_PAYLOAD_BYTE_COUNT = PAYLOAD_BYTE_COUNT + 8 + SEQUENCE_ID_BYTE_COUNT
"""Start marker payload: header | start time UTC i64 | sequence id (16 bytes)."""
MAX_ENCODED_PAYLOAD_BYTE_COUNT = START_PAYLOAD_BYTE_COUNT
"""The longest payload of any kind: the start marker's."""

TICKS_PER_SECOND = 10_000_000
"""TimeSpan / DateTime resolution."""
UNIX_EPOCH_DATE_TIME_TICKS = 621_355_968_000_000_000
"""DateTime ticks (since 0001-01-01) at the Unix epoch."""

RECOMMENDED_INSET_PX = 32
"""Recommended distance in source pixels between the marker and the edge of the frame."""

DEFAULT_MODULE_SIZE_PX = 6
"""The module size of Options()."""
MIN_MODULE_SIZE_PX = 1
MAX_MODULE_SIZE_PX = 1024
MAX_QUIET_ZONE_MODULES = 16
RECOMMENDED_QUIET_ZONE_MODULES = 4

MAX_QUAD_COUNT = 1 + (QR_MODULE_COUNT * ((QR_MODULE_COUNT + 1) // 2))
"""Upper bound on the number of quads for any marker: one background quad plus at most one quad per dark run."""

MAX_PACKED_MODULE_BYTE_COUNT = packed_module_byte_count(QR_MODULE_COUNT)
"""The packed module matrix of the largest symbol: 211 bytes for 41x41."""

MAX_GRID_VERTEX_COUNT = 4 + ((QR_MODULE_COUNT + 1) ** 2)
"""Vertices of the main marker's static grid (grid_vertices): 1768; the sync marker's is 680. Both fit 16-bit indices."""


def qr_module_count_for(kind: MarkerKind) -> int:
    """Modules per side of a marker's symbol: the main marker (frame, start and end) or the smaller sync marker."""
    return SYNC_QR_MODULE_COUNT if kind == MarkerKind.SYNC else QR_MODULE_COUNT
