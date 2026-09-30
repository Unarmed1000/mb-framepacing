# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""The marker format and geometry: constants, sizing and placement, the payload wire format, encoding the marker (generate_modules) and
drawing it from the modules as quads, triangles or indexed triangles. The same API as the C# library (MB.FramePacing.Marker) and the C++ library
(MB::FramePacing::Marker), in Python's naming; the specification is doc/marker-format.md.

Every output walks the marker in the same order: the light background (symbol + quiet zone) first, then one dark quad per horizontal
run of dark modules. Draw it in that order, last in the frame (after post effects and UI), without blending, in pure black and white.
"""

import struct
from datetime import UTC, datetime, timedelta
from typing import cast

from ..point import Point
from ..rectangle import Rectangle
from .constants import (
    PAYLOAD_BYTE_COUNT,
    PAYLOAD_FORMAT_VERSION,
    PAYLOAD_MAGIC,
    QR_VERSION,
    START_PAYLOAD_BYTE_COUNT,
    SYNC_PAYLOAD_BYTE_COUNT,
    SYNC_QR_VERSION,
    TICKS_PER_SECOND,
    qr_module_count_for,
)
from .options import Options
from .structures import (
    SEQUENCE_ID_BYTE_COUNT,
    MarkerFlags,
    MarkerKind,
    MarkerQuad,
    ModuleMatrix,
    Payload,
    SequenceId,
    StartMetadata,
    Vertex,
    packed_module_byte_count,
)
from .third_party.qrcodegen import encode as _encode_qr

# Grouped: the format, which run and frame, what the frame shows, the frame pacing, the CPU's work
_HEADER = struct.Struct("<2sBBIQBqIIqqI")
_SYNC = struct.Struct("<2sBBIQ")
_START_FIELDS = struct.Struct(f"<q{SEQUENCE_ID_BYTE_COUNT}s")
_DATE_TIME_EPOCH = datetime(1, 1, 1, tzinfo=UTC)


def to_date_time_ticks(time: datetime) -> int:
    """Convert a wall clock time to DateTime UTC ticks (the StartMetadata.utc_ticks format); a naive time counts as local time."""
    return (time.astimezone(UTC) - _DATE_TIME_EPOCH) // timedelta(microseconds=1) * 10


def seconds_to_ticks(seconds: float) -> int:
    """Convert seconds (for example an animation clock) to TimeSpan ticks, rounded to the nearest tick (half to even, like the C#
    library's Math.Round)."""
    return round(seconds * TICKS_PER_SECOND)


def encode_payload(payload: Payload, metadata: StartMetadata | None = None) -> bytes:
    """Serialize the payload. Start markers append the metadata, other kinds ignore it; a sync marker is SYNC_PAYLOAD_BYTE_COUNT bytes
    (the start of the header: the run id and the frame index) and ignores the other fields. Raises ValueError when an encoded field is out of its
    range."""
    if payload.kind == MarkerKind.SYNC:
        try:
            return _SYNC.pack(PAYLOAD_MAGIC, PAYLOAD_FORMAT_VERSION, payload.kind, payload.run_id, payload.frame_index)
        except struct.error as error:
            raise ValueError(f"payload out of range: {payload}") from error
    try:
        header = _HEADER.pack(
            PAYLOAD_MAGIC,
            PAYLOAD_FORMAT_VERSION,
            payload.kind,
            payload.run_id,
            payload.frame_index,
            payload.flags,
            payload.animation_ticks,
            payload.preferred_frame_ticks,
            payload.target_frame_ticks,
            payload.intended_display_ticks,
            payload.cpu_start_ticks,
            payload.cpu_busy_ticks,
        )
    except struct.error as error:
        raise ValueError(f"payload out of range: {payload}") from error
    if payload.kind != MarkerKind.SEQUENCE_START:
        return header
    start = metadata or StartMetadata()
    try:
        fields = _START_FIELDS.pack(start.utc_ticks, start.sequence_id.data)
    except struct.error as error:
        raise ValueError(f"start time out of range: {start.utc_ticks}") from error
    return header + fields


def try_decode_payload(data: bytes) -> tuple[Payload, StartMetadata | None] | None:
    """Parse the wire format: the payload and, for a start marker, its metadata. None on a wrong length, magic, format version or an
    unknown kind. A sync payload (exactly SYNC_PAYLOAD_BYTE_COUNT bytes) decodes to its run id and frame index with the other fields 0."""
    if len(data) < SYNC_PAYLOAD_BYTE_COUNT:
        return None
    magic, version, kind, run_id, frame_index = cast(tuple[bytes, int, int, int, int], _SYNC.unpack_from(data))
    if magic != PAYLOAD_MAGIC or version != PAYLOAD_FORMAT_VERSION or kind > max(MarkerKind):
        return None
    if kind == MarkerKind.SYNC:
        return (Payload(MarkerKind.SYNC, run_id, frame_index, MarkerFlags.NONE, 0), None) if len(data) == SYNC_PAYLOAD_BYTE_COUNT else None
    if len(data) < PAYLOAD_BYTE_COUNT:
        return None
    fields = cast(tuple[bytes, int, int, int, int, int, int, int, int, int, int, int], _HEADER.unpack_from(data))
    _, _, _, _, _, flags, animation_ticks, preferred, target_frame_ticks, intended_display_ticks, cpu_start_ticks, cpu_busy_ticks = fields
    payload = Payload(
        MarkerKind(kind),
        run_id,
        frame_index,
        # Every value is accepted: bits without a name are reserved and kept
        MarkerFlags(flags),
        animation_ticks,
        preferred_frame_ticks=preferred,
        target_frame_ticks=target_frame_ticks,
        intended_display_ticks=intended_display_ticks,
        cpu_start_ticks=cpu_start_ticks,
        cpu_busy_ticks=cpu_busy_ticks,
    )
    if payload.kind != MarkerKind.SEQUENCE_START:
        return (payload, None) if len(data) == PAYLOAD_BYTE_COUNT else None
    if len(data) != START_PAYLOAD_BYTE_COUNT:
        return None
    utc_ticks, sequence_id = cast(tuple[int, bytes], _START_FIELDS.unpack_from(data, PAYLOAD_BYTE_COUNT))
    return payload, StartMetadata(utc_ticks, SequenceId(sequence_id))


def generate_modules(payload: Payload, metadata: StartMetadata | None = None) -> ModuleMatrix:
    """Encode a marker: the payload's QR symbol as a packed module matrix, the one step every drawing output starts from (the metadata is
    only used by start markers). Draw it with modules_to_quads, modules_to_triangles, modules_to_indexed or modules_to_bitmap; one matrix
    can feed several. Raises ValueError when an encoded field is out of its range."""
    data = encode_payload(payload, metadata)
    # Every kind is pinned to one version, so the symbol never changes size between frames
    version = SYNC_QR_VERSION if payload.kind == MarkerKind.SYNC else QR_VERSION
    symbol = _encode_qr(data, version, version)
    if symbol is None:  # every payload encode_payload accepts fits the version
        raise ValueError(f"the payload does not fit QR version {version}: {payload}")
    # Pack the symbol: row-major, most significant bit first, continuous across rows
    bits = bytearray(packed_module_byte_count(symbol.size))
    for index, dark in enumerate(dark for row in symbol.modules for dark in row):
        if dark:
            bits[index >> 3] |= 0x80 >> (index & 7)
    return ModuleMatrix(symbol.size, bytes(bits))


def modules_to_quads(matrix: ModuleMatrix, options: Options, origin: Point) -> list[MarkerQuad]:
    """The marker as quads, for renderers that fill rectangles: the light background (symbol + quiet zone) first, then one dark quad per
    horizontal run of dark modules."""
    return _walk(matrix, options, origin)


def modules_to_triangles(matrix: ModuleMatrix, options: Options, origin: Point) -> list[Vertex]:
    """The marker as a triangle list: 6 vertices per quad (see modules_to_quads for the order), (TL, TR, BL) (BL, TR, BR), clockwise on
    screen, every vertex on a pixel corner."""
    vertices: list[Vertex] = []
    for quad in modules_to_quads(matrix, options, origin):
        top_left, top_right, bottom_right, bottom_left = _corners(quad)
        vertices += (top_left, top_right, bottom_left, bottom_left, top_right, bottom_right)
    return vertices


def modules_to_indexed(matrix: ModuleMatrix, options: Options, origin: Point, base_vertex: int = 0) -> tuple[list[Vertex], list[int]]:
    """The marker as an indexed triangle list: 4 vertices (TL, TR, BR, BL) and 6 indices (0,1,3)(3,1,2) per quad, clockwise on screen.
    `base_vertex` is added to every index."""
    vertices: list[Vertex] = []
    indices: list[int] = []
    for index, quad in enumerate(modules_to_quads(matrix, options, origin)):
        vertices += _corners(quad)
        first = base_vertex + (index * 4)
        indices += (first, first + 1, first + 3, first + 3, first + 1, first + 2)
    return vertices, indices


def grid_vertex_count(kind: MarkerKind) -> int:
    """Vertices of a marker kind's static grid: 4 for the light background, then every module corner, (N + 1)^2."""
    return 4 + ((qr_module_count_for(kind) + 1) ** 2)


def grid_vertices(kind: MarkerKind, options: Options, origin: Point) -> list[Vertex]:
    """The marker's static grid, for drawing it with per-frame indices only (modules_to_grid_indices): the vertices stay the same while the
    kind's symbol size, the options and the origin do. Vertices 0..3 are the light background (TL, TR, BR, BL, luma 255); then the corners
    of the modules, dark (luma 0), row-major: corner (column, row) is vertex 4 + row x (N + 1) + column, N the kind's modules per side."""
    modules = qr_module_count_for(kind)
    size = options.marker_size_px(kind)
    left = origin.x + options.quiet_zone_px
    top = origin.y + options.quiet_zone_px
    vertices = [
        Vertex(origin.x, origin.y, 255),
        Vertex(origin.x + size, origin.y, 255),
        Vertex(origin.x + size, origin.y + size, 255),
        Vertex(origin.x, origin.y + size, 255),
    ]
    for row in range(modules + 1):
        for column in range(modules + 1):
            vertices.append(Vertex(left + (column * options.module_size_px), top + (row * options.module_size_px), 0))
    return vertices


def modules_to_grid_indices(matrix: ModuleMatrix, base_vertex: int = 0) -> list[int]:
    """The per-frame part of the grid drawing: the indices of the background, (0,1,3)(3,1,2), then 6 per horizontal run of dark modules,
    (TL, TR, BL) (BL, TR, BR) of the run's grid corners, clockwise on screen. Use the grid of the matrix's kind. `base_vertex` is added to
    every index."""
    corners = matrix.size + 1
    indices = [base_vertex + i for i in (0, 1, 3, 3, 1, 2)]
    # With 1 px modules at the origin, a run's quad is its columns and row
    for run in _walk(matrix, Options(1, 0), Point(0, 0))[1:]:
        top_left = base_vertex + 4 + (run.rect.top * corners) + run.rect.left
        top_right = base_vertex + 4 + (run.rect.top * corners) + run.rect.right
        indices += (top_left, top_right, top_left + corners, top_left + corners, top_right, top_right + corners)
    return indices


def _corners(quad: MarkerQuad) -> tuple[Vertex, Vertex, Vertex, Vertex]:
    """TL, TR, BR, BL."""
    luma = 0 if quad.dark else 255
    return (
        Vertex(quad.rect.left, quad.rect.top, luma),
        Vertex(quad.rect.right, quad.rect.top, luma),
        Vertex(quad.rect.right, quad.rect.bottom, luma),
        Vertex(quad.rect.left, quad.rect.bottom, luma),
    )


def _walk(matrix: ModuleMatrix, options: Options, origin: Point) -> list[MarkerQuad]:
    """The marker in draw order: the background quad, then every horizontal run of dark modules, row by row."""
    module_size = options.module_size_px
    marker_size = (matrix.size + (2 * options.quiet_zone_modules)) * module_size
    symbol_left = origin.x + options.quiet_zone_px
    symbol_top = origin.y + options.quiet_zone_px

    quads = [MarkerQuad(Rectangle(origin.x, origin.y, marker_size, marker_size), False)]
    for y in range(matrix.size):
        top = symbol_top + (y * module_size)
        x = 0
        while x < matrix.size:
            if not matrix.is_dark(x, y):
                x += 1
                continue
            run_start = x
            while x < matrix.size and matrix.is_dark(x, y):
                x += 1
            quads.append(MarkerQuad(Rectangle(symbol_left + (run_start * module_size), top, (x - run_start) * module_size, module_size), True))
    return quads
