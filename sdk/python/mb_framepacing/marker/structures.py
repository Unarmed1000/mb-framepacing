# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""The marker's data types, as in the C# library (MB.FramePacing.Marker) and the C++ library (MB::FramePacing::Marker).

Coordinates are pixels with the origin at the top-left corner, +x to the right and +y down. Every quad edge and every vertex lies on an
integer pixel edge.
"""

from dataclasses import dataclass, field, replace
from enum import IntEnum, IntFlag
from typing import Self
from uuid import UUID

from ..rectangle import Rectangle

SEQUENCE_ID_BYTE_COUNT = 16
"""A sequence id is 16 opaque bytes."""

ON_DEMAND_FRAME_NS = 0xFFFF_FFFF
"""The target and preferred frame time of a renderer that presents only when something changes: there is no interval to aim for. The
largest value the field's four bytes hold, so no frame time is that long: see MAX_FRAME_NS."""
MAX_FRAME_NS = 0xFFFF_FFFE
"""The longest target and preferred frame time a marker carries, 4.294967294 s (slower than 0.233 fps): one below ON_DEMAND_FRAME_NS. A
longer one is held as this."""
MAX_CPU_BUSY_NS = 0xFFFF_FFFF
"""The longest CPU busy a marker carries, 4.294967295 s. A longer one is held as this."""


class MarkerKind(IntEnum):
    """What a marker marks: a frame of a run, or the start or end of a run (a test sequence). SYNC is the small second marker for
    tearing checks and camera timing: it only carries the run id and the frame index."""

    FRAME = 0
    SEQUENCE_START = 1
    SEQUENCE_END = 2
    SYNC = 3


class MarkerFlags(IntFlag):
    """The payload's flags byte (doc/marker-format.md "Flags"). Bits 2 to 7 are reserved: write 0; a decoded payload keeps whatever it
    carried."""

    NO_FLAGS = 0
    STATIC_AFTER = 1
    """Nothing animates while this frame is on screen, until the next frame (the application has no pending work after it). Says nothing
    about whether this frame itself animated. The analysis does not judge the step from it to the next frame."""
    STATIC_BEFORE = 2
    """Nothing animated while the frame before this one was on screen: STATIC_AFTER of the previous frame, for an application that only
    knows it once it renders this frame."""


@dataclass(frozen=True, slots=True)
class Payload:
    """The data every marker carries, its fields in the order of the wire format (doc/marker-format.md). The kind, run id (u32), frame
    index (u64), flags and animation time are required; the timing fields are optional (0 = unknown). Every time is a whole number of
    nanoseconds, an int: a span (the animation time, i64), a point on the frame pacer's steady clock (the intended display and CPU start
    time, i64) or a duration, which is never negative (the preferred and target frame time and CPU busy, u32). A sync marker only carries
    the kind, run id and frame index.

    The marker holds its three durations in four bytes each, so a payload holds none longer than a marker can carry: making one caps a
    longer CPU busy at MAX_CPU_BUSY_NS and a longer frame time at MAX_FRAME_NS (never an error: this runs in a frame loop). So a payload
    decodes to exactly what was encoded. Nothing else is checked here: encode_payload raises ValueError for a negative duration and for
    any other field outside its range. The flags are kept as given, reserved bits included."""

    kind: MarkerKind
    run_id: int
    """Identifies one test run. The start marker, every frame marker and the end marker of a run carry the same id."""
    frame_index: int
    """The application's own rendered-frame counter. Unrelated to the capture card's frame counter."""
    flags: MarkerFlags
    """MarkerFlags.STATIC_AFTER when nothing animates while this frame is on screen, MarkerFlags.STATIC_BEFORE when nothing animated while
    the frame before it was; the other bits are reserved (write 0, a decoded payload keeps them)."""
    animation_ns: int
    """The animation time: the time on the application's animation clock the frame's animation was evaluated for, in nanoseconds."""
    preferred_frame_ns: int = 0
    """The interval the application wants to run at, in nanoseconds: what it would aim for if nothing held it back. It differs from
    target_frame_ns only while the pacer runs slower than it wants (a pacer lowered to 30 fps: preferred 16_666_667, target 33_333_333). A
    30 fps lock or a device idle at 1 fps prefers what it runs at. 0 = unknown, ON_DEMAND_FRAME_NS = frames only when something changes;
    at most MAX_FRAME_NS otherwise."""
    target_frame_ns: int = 0
    """The interval the frame pacer aims for between the previous frame and this one, in nanoseconds: 16_666_667 for 60 fps. 0 = unknown,
    ON_DEMAND_FRAME_NS = frames only when something changes; at most MAX_FRAME_NS otherwise."""
    intended_display_ns: int = 0
    """When the frame pacer intends this frame to become visible, in nanoseconds on its steady clock (any epoch, the same clock for the
    whole run). 0 = unknown."""
    cpu_start_ns: int = 0
    """CPU start time: when the CPU started working on this frame (PresentMon's CPUStartTime), in nanoseconds on the same steady clock as
    intended_display_ns. Anywhere inside a refresh; frames can overlap. 0 = unknown."""
    cpu_busy_ns: int = 0
    """CPU busy: how long the CPU worked on this frame before presenting it (PresentMon's MsCPUBusy), from cpu_start_ns until Present is
    called, in nanoseconds. The marker is drawn last, so the application measures it as it draws the marker. It does not include the GPU's
    work. May span several refreshes. 0 = unknown; at most MAX_CPU_BUSY_NS."""

    def __post_init__(self) -> None:
        # The class is frozen for its users: this is the one place a value is put in
        if self.preferred_frame_ns > MAX_FRAME_NS and self.preferred_frame_ns != ON_DEMAND_FRAME_NS:
            object.__setattr__(self, "preferred_frame_ns", MAX_FRAME_NS)
        if self.target_frame_ns > MAX_FRAME_NS and self.target_frame_ns != ON_DEMAND_FRAME_NS:
            object.__setattr__(self, "target_frame_ns", MAX_FRAME_NS)
        if self.cpu_busy_ns > MAX_CPU_BUSY_NS:
            object.__setattr__(self, "cpu_busy_ns", MAX_CPU_BUSY_NS)

    def with_kind(self, kind: MarkerKind) -> Self:
        """The same payload with another kind: a start or end marker carries the values of the frame that shows it, a sync marker its run
        id and frame index."""
        return replace(self, kind=kind)


@dataclass(frozen=True, slots=True)
class SequenceId:
    """The start marker's sequence id: 16 opaque bytes that identify the capture sequence, any content as long as it is unique to it (a
    UUID's bytes, or a short text tag padded with zeros). All zero (the default) means no sequence id. str() shows it as text when it is
    printable ASCII, otherwise as a UUID's 8-4-4-4-12 hex form. Raises ValueError unless `data` is 16 bytes."""

    data: bytes = bytes(SEQUENCE_ID_BYTE_COUNT)

    def __post_init__(self) -> None:
        data = bytes(self.data)  # a bytearray or memoryview becomes immutable bytes
        if len(data) != SEQUENCE_ID_BYTE_COUNT:
            raise ValueError(f"a sequence id is {SEQUENCE_ID_BYTE_COUNT} bytes, not {len(data)}")
        object.__setattr__(self, "data", data)

    @property
    def is_empty(self) -> bool:
        """All zero: no sequence id."""
        return not any(self.data)

    @classmethod
    def from_uuid(cls, value: UUID) -> Self:
        """A UUID's bytes in the order its text shows them, so str() gives the same text as the UUID."""
        return cls(value.bytes)

    @classmethod
    def from_text(cls, text: str) -> Self:
        """A text tag of 1 to 16 printable ASCII characters, padded with zero bytes. Raises ValueError for any other text."""
        if not 0 < len(text) <= SEQUENCE_ID_BYTE_COUNT or not all(0x20 <= ord(character) <= 0x7E for character in text):
            raise ValueError(f"a sequence id text is 1 to {SEQUENCE_ID_BYTE_COUNT} printable ASCII characters: {text!r}")
        return cls(text.encode("ascii").ljust(SEQUENCE_ID_BYTE_COUNT, b"\x00"))

    def __str__(self) -> str:  # pyright: ignore[reportImplicitOverride]  (typing.override needs Python 3.12)
        """The text when the bytes are printable ASCII followed only by zero bytes, otherwise the 8-4-4-4-12 hex form."""
        text = self.data.rstrip(b"\x00")
        if text and all(0x20 <= byte <= 0x7E for byte in text):
            return text.decode("ascii")
        return str(UUID(bytes=self.data))


@dataclass(frozen=True, slots=True)
class StartMetadata:
    """What a start marker carries besides the payload: the start time in DateTime UTC ticks (0 = unknown) and the sequence id that
    identifies the capture sequence."""

    utc_ticks: int = 0
    sequence_id: SequenceId = SequenceId()


@dataclass(frozen=True, slots=True)
class MarkerQuad:
    """A rectangle of the marker to fill (modules_to_quads): its pixels, dark (luma 0) or light (luma 255)."""

    rect: Rectangle
    dark: bool


@dataclass(frozen=True, slots=True)
class Vertex:
    """A vertex on a pixel corner, with the luma of its quad (0 or 255)."""

    x: int
    y: int
    luma: int


# Modules per side of a main marker and of the sync marker (constants.QR_MODULE_COUNT and SYNC_QR_MODULE_COUNT, which import this module)
_MARKER_SIZES = (41, 25)


def packed_module_byte_count(size: int) -> int:
    """The bytes of a packed module matrix of size x size modules: 211 for the main marker (41), 79 for the sync marker (25)."""
    return 0 if size <= 0 else ((size * size) + 7) // 8


@dataclass(frozen=True, slots=True)
class ModuleMatrix:
    """The encoded marker: the QR symbol's modules, 1 bit each (1 = dark), packed row-major, most significant bit first, continuous across
    rows, the last byte zero padded (exactly test-data/markers/modules.csv's modulesHex). generate_modules makes it once per marker; every
    drawing output (modules_to_quads, modules_to_triangles, modules_to_indexed, modules_to_bitmap) is made from it.

    `size` modules per side (41 for the main marker, 25 for the sync marker). Building one from bits checks the size (one of those two:
    the sizes the drawing functions and the grid know) and the length (at least packed_module_byte_count(size) bytes), and ignores bits
    past the last module."""

    size: int
    bits: bytes = field(repr=False)

    def __post_init__(self) -> None:
        count = packed_module_byte_count(self.size)
        if self.size not in _MARKER_SIZES or len(self.bits) < count:
            raise ValueError(f"not a marker's packed modules: size {self.size}, {len(self.bits)} bytes")
        bits = bytearray(self.bits[:count])
        used = (self.size * self.size) % 8
        if used:
            bits[-1] &= (0xFF << (8 - used)) & 0xFF
        object.__setattr__(self, "bits", bytes(bits))

    def is_dark(self, x: int, y: int) -> bool:
        index = (y * self.size) + x
        return (self.bits[index >> 3] >> (7 - (index & 7))) & 1 == 1


class PixelFormat(IntEnum):
    """The pixels modules_to_bitmap writes. Each pixel is a run of bytes in memory order; pixels follow each other left to right, rows are
    the stride apart, top row first. Every colour channel holds the same value: 0 for a dark module, 255 for a light one.

        R8        1 byte:  [L]
        R8G8B8    3 bytes: [R, G, B]          (R = G = B = L)
        R8G8B8A8  4 bytes: [R, G, B, A]       (R = G = B = L, A = 255)

    Because R, G and B are equal, a B8G8R8 or B8G8R8A8 buffer (PIL's "BGR;24", OpenCV's default order) gets exactly the same bytes: use
    R8G8B8 or R8G8B8A8 for them. A buffer with alpha first (ARGB) is not supported."""

    R8 = 0
    R8G8B8 = 1
    R8G8B8A8 = 2

    @property
    def bytes_per_pixel(self) -> int:
        return (1, 3, 4)[self]
