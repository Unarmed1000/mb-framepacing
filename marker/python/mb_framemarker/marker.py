# SPDX-License-Identifier: BSD-3-Clause
# Copyright (c) 2026, Mana Battery ApS

"""The marker format and geometry: constants, sizing and placement, the payload wire format, generating the marker as quads, triangles
or indexed triangles, and quad to vertex conversion. The same API as the C# library (MB.FrameMarker) and the C++ library
(MB::FrameMarker), in Python's naming; the specification is doc/marker-format.md.

Every output walks the marker in the same order: the light background (symbol + quiet zone) first, then one dark quad per horizontal
run of dark modules. Draw it in that order, last in the frame (after post effects and UI), without blending, in pure black and white.
"""

import struct
from datetime import UTC, datetime, timedelta
from typing import cast

from .structures import MarkerKind, MarkerSlot, ModuleMatrix, Options, Payload, Point, Quad, StartMetadata, Vertex
from .third_party.qrcodegen import encode as _encode_qr

FRAME_QR_VERSION = 2
"""Frame and end markers are fixed to QR version 2 (25x25 modules), error correction level M, byte mode."""

MAX_QR_VERSION = 6
"""Start markers carry metadata and use the smallest version in [FRAME_QR_VERSION, MAX_QR_VERSION] that fits."""

FRAME_QR_MODULE_COUNT = (4 * FRAME_QR_VERSION) + 17
MAX_QR_MODULE_COUNT = (4 * MAX_QR_VERSION) + 17

PAYLOAD_BYTE_COUNT = 24
"""Payload header, shared by every marker kind (little endian): magic "MF" | format version | kind | frame index u64 | animation
ticks i64 | run id u32."""
PAYLOAD_MAGIC = b"MF"
PAYLOAD_FORMAT_VERSION = 1

MAX_START_NAME_BYTES = 64
"""Start marker payload: header | start time UTC i64 | name length u8 | name UTF-8 (0..MAX_START_NAME_BYTES)."""
START_PAYLOAD_FIXED_BYTE_COUNT = PAYLOAD_BYTE_COUNT + 8 + 1
MAX_ENCODED_PAYLOAD_BYTE_COUNT = START_PAYLOAD_FIXED_BYTE_COUNT + MAX_START_NAME_BYTES

TICKS_PER_SECOND = 10_000_000
"""TimeSpan / DateTime resolution."""
UNIX_EPOCH_DATE_TIME_TICKS = 621_355_968_000_000_000
"""DateTime ticks (since 0001-01-01) at the Unix epoch."""

RECOMMENDED_INSET_PX = 32
"""Recommended distance in source pixels between the marker and the edge of the frame."""

MIN_MODULE_SIZE_PX = 1
MAX_MODULE_SIZE_PX = 1024
MAX_QUIET_ZONE_MODULES = 16
RECOMMENDED_QUIET_ZONE_MODULES = 4

MAX_QUAD_COUNT = 1 + (MAX_QR_MODULE_COUNT * ((MAX_QR_MODULE_COUNT + 1) // 2))
"""Upper bound on the number of quads for any marker: one background quad plus at most one quad per dark run."""
MAX_FRAME_QUAD_COUNT = 1 + (FRAME_QR_MODULE_COUNT * ((FRAME_QR_MODULE_COUNT + 1) // 2))
"""Upper bound on the number of quads for a frame or end marker."""

_HEADER = struct.Struct("<2sBBQqI")
_START_FIELDS = struct.Struct("<qB")
_DATE_TIME_EPOCH = datetime(1, 1, 1, tzinfo=UTC)


def qr_module_count_for_version(version: int) -> int:
    return (4 * version) + 17


def is_valid(options: Options) -> bool:
    return MIN_MODULE_SIZE_PX <= options.module_size_px <= MAX_MODULE_SIZE_PX and 0 <= options.quiet_zone_modules <= MAX_QUIET_ZONE_MODULES


def marker_size_px(options: Options, module_count: int = FRAME_QR_MODULE_COUNT) -> int:
    """Width and height in source pixels of a marker (symbol + quiet zone) with the given symbol size; by default a frame or end
    marker."""
    return (module_count + (2 * options.quiet_zone_modules)) * options.module_size_px


def max_marker_size_px(options: Options) -> int:
    """Largest possible start marker (a 64 byte name). Keep this area free around the marker origin while the start marker shows."""
    return marker_size_px(options, MAX_QR_MODULE_COUNT)


def minimum_module_size_px(source_height: int, stored_height: int) -> int:
    """Hard minimum module size: 2 stored pixels per module after all scaling (source -> capture -> stored)."""
    return _module_size_for_stored_px(2, source_height, stored_height)


def recommend_module_size_px(source_height: int, stored_height: int, mjpeg: bool = False) -> int:
    """Recommended module size: 3 stored pixels per module, or 4 when the capture card delivers MJPEG."""
    return _module_size_for_stored_px(4 if mjpeg else 3, source_height, stored_height)


def recommended_origin(slot: MarkerSlot, source_width: int, source_height: int, options: Options, align_px: int = 1) -> Point:
    """Recommended marker origin for the given slot. `align_px` should be the integer downscale ratio (1 if none) so module edges land
    on stored pixel edges."""
    del source_width  # the marker sits at the left edge; the width is part of the API for other slots
    size = marker_size_px(options)
    inset = _align_up(RECOMMENDED_INSET_PX, align_px)
    match slot:
        case MarkerSlot.MIDDLE_LEFT:
            return Point(inset, _align_down(_divide(source_height - size, 2), align_px))
        case MarkerSlot.BOTTOM_LEFT:
            return Point(inset, _align_down(source_height - inset - size, align_px))
        case _:
            return Point(inset, inset)


def to_date_time_ticks(time: datetime) -> int:
    """Convert a wall clock time to DateTime UTC ticks (the StartMetadata.utc_ticks format); a naive time counts as local time."""
    return (time.astimezone(UTC) - _DATE_TIME_EPOCH) // timedelta(microseconds=1) * 10


def seconds_to_ticks(seconds: float) -> int:
    """Convert seconds (for example an animation clock) to TimeSpan ticks, rounded to the nearest tick (half to even, like the C#
    library's Math.Round)."""
    return round(seconds * TICKS_PER_SECOND)


def encode_payload(payload: Payload, metadata: StartMetadata | None = None) -> bytes:
    """Serialize the payload. Start markers append the metadata, other kinds ignore it. Raises ValueError when the name is longer than
    MAX_START_NAME_BYTES bytes as UTF-8, or a field is out of its range."""
    try:
        header = _HEADER.pack(PAYLOAD_MAGIC, PAYLOAD_FORMAT_VERSION, payload.kind, payload.frame_index, payload.animation_ticks, payload.run_id)
    except struct.error as error:
        raise ValueError(f"payload out of range: {payload}") from error
    if payload.kind != MarkerKind.SEQUENCE_START:
        return header
    start = metadata or StartMetadata()
    name = start.name.encode("utf-8")
    if len(name) > MAX_START_NAME_BYTES:
        raise ValueError(f"the start name is {len(name)} bytes as UTF-8, more than {MAX_START_NAME_BYTES}")
    try:
        fields = _START_FIELDS.pack(start.utc_ticks, len(name))
    except struct.error as error:
        raise ValueError(f"start time out of range: {start.utc_ticks}") from error
    return header + fields + name


def try_decode_payload(data: bytes) -> tuple[Payload, StartMetadata | None] | None:
    """Parse the wire format: the payload and, for a start marker, its metadata. None on a wrong length, magic, format version, an
    unknown kind or (start markers) a name that is not valid UTF-8."""
    if len(data) < PAYLOAD_BYTE_COUNT:
        return None
    magic, version, kind, frame_index, animation_ticks, run_id = cast(tuple[bytes, int, int, int, int, int], _HEADER.unpack_from(data))
    if magic != PAYLOAD_MAGIC or version != PAYLOAD_FORMAT_VERSION or kind > MarkerKind.SEQUENCE_END:
        return None
    payload = Payload(frame_index, animation_ticks, run_id, MarkerKind(kind))
    if payload.kind != MarkerKind.SEQUENCE_START:
        return (payload, None) if len(data) == PAYLOAD_BYTE_COUNT else None
    if len(data) < START_PAYLOAD_FIXED_BYTE_COUNT:
        return None
    utc_ticks, name_length = cast(tuple[int, int], _START_FIELDS.unpack_from(data, PAYLOAD_BYTE_COUNT))
    if name_length > MAX_START_NAME_BYTES or len(data) != START_PAYLOAD_FIXED_BYTE_COUNT + name_length:
        return None
    try:
        name = data[START_PAYLOAD_FIXED_BYTE_COUNT:].decode("utf-8")
    except UnicodeDecodeError:
        return None
    return payload, StartMetadata(utc_ticks, name)


def generate_modules(payload: Payload, metadata: StartMetadata | None = None) -> ModuleMatrix:
    """Build the QR module matrix for the payload. The metadata is only used by start markers. Raises ValueError if the start name is
    too long."""
    data = encode_payload(payload, metadata)
    # Frame and end markers are pinned to one version so the symbol never changes size between frames
    max_version = MAX_QR_VERSION if payload.kind == MarkerKind.SEQUENCE_START else FRAME_QR_VERSION
    symbol = _encode_qr(data, FRAME_QR_VERSION, max_version)
    if symbol is None:  # every payload encode_payload accepts fits its version range
        raise ValueError(f"the payload does not fit QR version {max_version}: {payload}")
    return ModuleMatrix(symbol.size, tuple(tuple(row) for row in symbol.modules))


def generate_quads(payload: Payload, options: Options, origin: Point) -> list[Quad]:
    """The marker as quads, for renderers that fill rectangles: the light background first, then one dark quad per horizontal run of
    dark modules. A start marker made this way carries empty metadata. Raises ValueError if the options are invalid."""
    return _walk(_build_matrix(payload, None, False, options), options, origin)


def generate_start_quads(payload: Payload, metadata: StartMetadata, options: Options, origin: Point) -> list[Quad]:
    """generate_quads for a start marker carrying metadata (the payload's kind is forced to SEQUENCE_START). The marker is at most
    max_marker_size_px wide and high."""
    return _walk(_build_matrix(payload, metadata, True, options), options, origin)


def generate_triangles(payload: Payload, options: Options, origin: Point) -> list[Vertex]:
    """The marker as a triangle list: 6 vertices per quad, (TL, TR, BL) (BL, TR, BR), clockwise on screen, every vertex on a pixel
    corner."""
    return quads_to_triangles(generate_quads(payload, options, origin))


def generate_start_triangles(payload: Payload, metadata: StartMetadata, options: Options, origin: Point) -> list[Vertex]:
    """generate_triangles for a start marker carrying metadata (the payload's kind is forced to SEQUENCE_START)."""
    return quads_to_triangles(generate_start_quads(payload, metadata, options, origin))


def generate_indexed(payload: Payload, options: Options, origin: Point, base_vertex: int = 0) -> tuple[list[Vertex], list[int]]:
    """The marker as an indexed triangle list: 4 vertices (TL, TR, BR, BL) and 6 indices (0,1,3)(3,1,2) per quad, clockwise on screen.
    `base_vertex` is added to every index."""
    return quads_to_indexed(generate_quads(payload, options, origin), base_vertex)


def generate_start_indexed(payload: Payload, metadata: StartMetadata, options: Options, origin: Point, base_vertex: int = 0) -> tuple[list[Vertex], list[int]]:
    """generate_indexed for a start marker carrying metadata (the payload's kind is forced to SEQUENCE_START)."""
    return quads_to_indexed(generate_start_quads(payload, metadata, options, origin), base_vertex)


def quads_to_triangles(quads: list[Quad]) -> list[Vertex]:
    """Convert quads to a triangle list: 6 vertices per quad, (TL, TR, BL) (BL, TR, BR), clockwise on screen (+y down)."""
    vertices: list[Vertex] = []
    for quad in quads:
        top_left, top_right, bottom_right, bottom_left = _corners(quad)
        vertices += (top_left, top_right, bottom_left, bottom_left, top_right, bottom_right)
    return vertices


def quads_to_indexed(quads: list[Quad], base_vertex: int = 0) -> tuple[list[Vertex], list[int]]:
    """Convert quads to an indexed triangle list: 4 vertices (TL, TR, BR, BL) and 6 indices (0,1,3)(3,1,2) per quad, clockwise on
    screen. `base_vertex` is added to every index."""
    vertices: list[Vertex] = []
    indices: list[int] = []
    for index, quad in enumerate(quads):
        vertices += _corners(quad)
        first = base_vertex + (index * 4)
        indices += (first, first + 1, first + 3, first + 3, first + 1, first + 2)
    return vertices, indices


def _corners(quad: Quad) -> tuple[Vertex, Vertex, Vertex, Vertex]:
    """TL, TR, BR, BL."""
    luma = 0 if quad.dark else 255
    return (
        Vertex(quad.left, quad.top, luma),
        Vertex(quad.right, quad.top, luma),
        Vertex(quad.right, quad.bottom, luma),
        Vertex(quad.left, quad.bottom, luma),
    )


def _build_matrix(payload: Payload, metadata: StartMetadata | None, force_start: bool, options: Options) -> ModuleMatrix:
    if not is_valid(options):
        raise ValueError(f"invalid options: {options}")
    if not force_start:
        return generate_modules(payload)
    return generate_modules(payload.with_kind(MarkerKind.SEQUENCE_START), metadata)


def _walk(matrix: ModuleMatrix, options: Options, origin: Point) -> list[Quad]:
    """The marker in draw order: the background quad, then every horizontal run of dark modules, row by row."""
    module_size = options.module_size_px
    marker_size = marker_size_px(options, matrix.size)
    symbol_left = origin.x + (options.quiet_zone_modules * module_size)
    symbol_top = origin.y + (options.quiet_zone_modules * module_size)

    quads = [Quad(origin.x, origin.y, origin.x + marker_size, origin.y + marker_size, False)]
    for y, row in enumerate(matrix.rows):
        top = symbol_top + (y * module_size)
        x = 0
        while x < matrix.size:
            if not row[x]:
                x += 1
                continue
            run_start = x
            while x < matrix.size and row[x]:
                x += 1
            quads.append(Quad(symbol_left + (run_start * module_size), top, symbol_left + (x * module_size), top + module_size, True))
    return quads


def _divide(numerator: int, denominator: int) -> int:
    """Integer division truncating toward zero, as in C# and C++ (Python's // floors)."""
    quotient = abs(numerator) // abs(denominator)
    return quotient if (numerator >= 0) == (denominator > 0) else -quotient


def _ceil_divide(numerator: int, denominator: int) -> int:
    return _divide(numerator + denominator - 1, denominator)


def _align_down(value: int, alignment: int) -> int:
    return value if alignment <= 1 else _divide(value, alignment) * alignment


def _align_up(value: int, alignment: int) -> int:
    return value if alignment <= 1 else _ceil_divide(value, alignment) * alignment


def _module_size_for_stored_px(stored_px_per_module: int, source_height: int, stored_height: int) -> int:
    if source_height <= 0 or stored_height <= 0:
        return stored_px_per_module
    # module_size_px = ceil(stored_px_per_module / s) where s = stored_height / source_height
    return max(stored_px_per_module, _ceil_divide(stored_px_per_module * source_height, stored_height))
