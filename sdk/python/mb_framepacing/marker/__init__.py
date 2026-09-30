# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""mb_framepacing.marker: the frame marker of mb-framepacing, in Python.

Draw a QR marker with the frame index, the animation time, the run id and (optionally) the frame pacer's intended display time and
target frame time, the CPU start time and CPU busy into every frame of an application, so mb-framepacing can measure the animation
error on the real display output. It draws exactly the same pixels as the C++ and C# libraries: the tests check
it against the golden images the C++ library writes (test-data/markers). The format is specified in doc/marker-format.md.

    from mb_framepacing.marker import MarkerFlags, MarkerKind, Options, Payload, PixelFormat, generate_modules, modules_to_bitmap, seconds_to_ticks

    options = Options(module_size_px=3)
    origin = options.recommended_origin(MarkerKind.FRAME, width, height)
    matrix = generate_modules(Payload(MarkerKind.FRAME, 1, frame_index, MarkerFlags.NONE, seconds_to_ticks(animation_seconds)))   # encode once
    modules_to_bitmap(matrix, options, origin, rgb_frame, width, height, PixelFormat.R8G8B8)       # draw it

    # Optional (required for camera capture): the small sync marker, bottom-left, with the same frame index
    sync_origin = options.recommended_origin(MarkerKind.SYNC, width, height)
    sync = generate_modules(Payload(MarkerKind.SYNC, 0, frame_index, MarkerFlags.NONE, 0))
    modules_to_bitmap(sync, options, sync_origin, rgb_frame, width, height, PixelFormat.R8G8B8)

Standard library only, Python 3.12 or later.
"""

from ..point import Point
from ..rectangle import Rectangle
from .bitmap import modules_to_bitmap
from .constants import (
    DEFAULT_MODULE_SIZE_PX,
    MAX_ENCODED_PAYLOAD_BYTE_COUNT,
    MAX_GRID_VERTEX_COUNT,
    MAX_MODULE_SIZE_PX,
    MAX_PACKED_MODULE_BYTE_COUNT,
    MAX_QUAD_COUNT,
    MAX_QUIET_ZONE_MODULES,
    MIN_MODULE_SIZE_PX,
    ON_DEMAND_FRAME_TICKS,
    PAYLOAD_BYTE_COUNT,
    PAYLOAD_FORMAT_VERSION,
    PAYLOAD_MAGIC,
    QR_CAPACITY_BYTES,
    QR_MODULE_COUNT,
    QR_VERSION,
    RECOMMENDED_INSET_PX,
    RECOMMENDED_QUIET_ZONE_MODULES,
    START_PAYLOAD_BYTE_COUNT,
    SYNC_PAYLOAD_BYTE_COUNT,
    SYNC_QR_MODULE_COUNT,
    SYNC_QR_VERSION,
    TICKS_PER_SECOND,
    UNIX_EPOCH_DATE_TIME_TICKS,
    qr_module_count_for,
)
from .marker import (
    encode_payload,
    generate_modules,
    grid_vertex_count,
    grid_vertices,
    modules_to_grid_indices,
    modules_to_indexed,
    modules_to_quads,
    modules_to_triangles,
    seconds_to_ticks,
    to_date_time_ticks,
    try_decode_payload,
)
from .options import Options
from .structures import (
    SEQUENCE_ID_BYTE_COUNT,
    MarkerFlags,
    MarkerKind,
    MarkerQuad,
    ModuleMatrix,
    Payload,
    PixelFormat,
    SequenceId,
    StartMetadata,
    Vertex,
    packed_module_byte_count,
)

__all__ = [
    "DEFAULT_MODULE_SIZE_PX",
    "MAX_ENCODED_PAYLOAD_BYTE_COUNT",
    "MAX_GRID_VERTEX_COUNT",
    "MAX_MODULE_SIZE_PX",
    "MAX_PACKED_MODULE_BYTE_COUNT",
    "MAX_QUAD_COUNT",
    "MAX_QUIET_ZONE_MODULES",
    "MIN_MODULE_SIZE_PX",
    "ON_DEMAND_FRAME_TICKS",
    "PAYLOAD_BYTE_COUNT",
    "PAYLOAD_FORMAT_VERSION",
    "PAYLOAD_MAGIC",
    "QR_CAPACITY_BYTES",
    "QR_MODULE_COUNT",
    "QR_VERSION",
    "RECOMMENDED_INSET_PX",
    "RECOMMENDED_QUIET_ZONE_MODULES",
    "SEQUENCE_ID_BYTE_COUNT",
    "START_PAYLOAD_BYTE_COUNT",
    "SYNC_PAYLOAD_BYTE_COUNT",
    "SYNC_QR_MODULE_COUNT",
    "SYNC_QR_VERSION",
    "TICKS_PER_SECOND",
    "UNIX_EPOCH_DATE_TIME_TICKS",
    "MarkerFlags",
    "MarkerKind",
    "ModuleMatrix",
    "Options",
    "Payload",
    "PixelFormat",
    "Point",
    "MarkerQuad",
    "Rectangle",
    "SequenceId",
    "StartMetadata",
    "Vertex",
    "encode_payload",
    "generate_modules",
    "grid_vertex_count",
    "grid_vertices",
    "modules_to_bitmap",
    "modules_to_grid_indices",
    "modules_to_indexed",
    "modules_to_quads",
    "modules_to_triangles",
    "packed_module_byte_count",
    "qr_module_count_for",
    "seconds_to_ticks",
    "to_date_time_ticks",
    "try_decode_payload",
]
