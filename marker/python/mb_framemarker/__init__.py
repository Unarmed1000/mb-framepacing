# SPDX-License-Identifier: BSD-3-Clause
# Copyright (c) 2026, Mana Battery ApS

"""mb_framemarker: the frame marker of mb-framepacing, in Python.

Draw a QR marker with the frame index, the animation time and the run id into every frame of an application, so mb-framepacing can
measure the animation error on the real display output. It draws exactly the same pixels as the C++ and C# libraries: the tests check
it against the golden images the C++ library writes (test-data/markers). The format is specified in doc/marker-format.md.

    from mb_framemarker import Options, Payload, generate_quads, fill_quads, recommended_origin, seconds_to_ticks, MarkerSlot

    options = Options(module_size_px=3)
    origin = recommended_origin(MarkerSlot.TOP_LEFT, width, height, options)
    quads = generate_quads(Payload(frame_index, seconds_to_ticks(animation_seconds), run_id=1), options, origin)
    fill_quads(rgb24_frame, width, height, quads, channels=3)

Standard library only, Python 3.11 or later.
"""

from .marker import (
    FRAME_QR_MODULE_COUNT,
    FRAME_QR_VERSION,
    MAX_ENCODED_PAYLOAD_BYTE_COUNT,
    MAX_FRAME_QUAD_COUNT,
    MAX_MODULE_SIZE_PX,
    MAX_QR_MODULE_COUNT,
    MAX_QR_VERSION,
    MAX_QUAD_COUNT,
    MAX_QUIET_ZONE_MODULES,
    MAX_START_NAME_BYTES,
    MIN_MODULE_SIZE_PX,
    PAYLOAD_BYTE_COUNT,
    PAYLOAD_FORMAT_VERSION,
    PAYLOAD_MAGIC,
    RECOMMENDED_INSET_PX,
    RECOMMENDED_QUIET_ZONE_MODULES,
    START_PAYLOAD_FIXED_BYTE_COUNT,
    TICKS_PER_SECOND,
    UNIX_EPOCH_DATE_TIME_TICKS,
    encode_payload,
    generate_indexed,
    generate_modules,
    generate_quads,
    generate_start_indexed,
    generate_start_quads,
    generate_start_triangles,
    generate_triangles,
    is_valid,
    marker_size_px,
    max_marker_size_px,
    minimum_module_size_px,
    qr_module_count_for_version,
    quads_to_indexed,
    quads_to_triangles,
    recommend_module_size_px,
    recommended_origin,
    seconds_to_ticks,
    to_date_time_ticks,
    try_decode_payload,
)
from .raster import fill_quads
from .structures import MarkerKind, MarkerSlot, ModuleMatrix, Options, Payload, Point, Quad, StartMetadata, Vertex

__version__ = "0.1.0"

__all__ = [
    "FRAME_QR_MODULE_COUNT",
    "FRAME_QR_VERSION",
    "MAX_ENCODED_PAYLOAD_BYTE_COUNT",
    "MAX_FRAME_QUAD_COUNT",
    "MAX_MODULE_SIZE_PX",
    "MAX_QR_MODULE_COUNT",
    "MAX_QR_VERSION",
    "MAX_QUAD_COUNT",
    "MAX_QUIET_ZONE_MODULES",
    "MAX_START_NAME_BYTES",
    "MIN_MODULE_SIZE_PX",
    "PAYLOAD_BYTE_COUNT",
    "PAYLOAD_FORMAT_VERSION",
    "PAYLOAD_MAGIC",
    "RECOMMENDED_INSET_PX",
    "RECOMMENDED_QUIET_ZONE_MODULES",
    "START_PAYLOAD_FIXED_BYTE_COUNT",
    "TICKS_PER_SECOND",
    "UNIX_EPOCH_DATE_TIME_TICKS",
    "MarkerKind",
    "MarkerSlot",
    "ModuleMatrix",
    "Options",
    "Payload",
    "Point",
    "Quad",
    "StartMetadata",
    "Vertex",
    "encode_payload",
    "fill_quads",
    "generate_indexed",
    "generate_modules",
    "generate_quads",
    "generate_start_indexed",
    "generate_start_quads",
    "generate_start_triangles",
    "generate_triangles",
    "is_valid",
    "marker_size_px",
    "max_marker_size_px",
    "minimum_module_size_px",
    "qr_module_count_for_version",
    "quads_to_indexed",
    "quads_to_triangles",
    "recommend_module_size_px",
    "recommended_origin",
    "seconds_to_ticks",
    "to_date_time_ticks",
    "try_decode_payload",
]
