# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""captures.mbcd (doc/capture-data-format.md): a 256 byte header, then one 192 byte record per capture, little endian. The header describes
the frames the markers were read from and where the markers are; a record holds a capture's times and its markers' bytes as read."""

import struct
from collections.abc import Iterator
from dataclasses import dataclass
from enum import IntEnum
from pathlib import Path
from typing import BinaryIO, cast

from ..marker import Payload, StartMetadata, try_decode_payload
from ..rectangle import Rectangle
from .errors import DataFormatError

FILE_NAME = "captures.mbcd"
MAGIC = 0x4443424D  # "MBCD" little endian
FORMAT_VERSION = 1
HEADER_SIZE = 256
RECORD_SIZE = 192
MAX_MARKERS = 4
UNKNOWN_TICKS = -(2**63)
"""A device timestamp the capture source did not give (i64 minimum)."""

MAIN_CAPACITY = 80
"""Two equal slots: either can hold any marker payload (the longest, a start marker, is 77 bytes)."""
SECOND_CAPACITY = RECORD_SIZE - 32 - MAIN_CAPACITY

_HEADER = struct.Struct("<IHHIIiiIIiiiiii8xI")
_MARKER = struct.Struct("<iiiid")
_MARKERS_OFFSET = 72
_SYNC_REGION = struct.Struct("<iiii")
_SYNC_REGION_OFFSET = 168
_RECORD = struct.Struct("<qqqIBBB")
_FRAMES_STORED = 1
_CAMERA = 2


class CaptureDataStatus(IntEnum):
    """What reading a capture's markers gave."""

    UNDECODABLE = 0
    DECODED = 1
    TORN = 2


@dataclass(frozen=True)
class MarkerLocation:
    """Where a marker is: its bounds (including the quiet zone) and module size, in stored pixels."""

    bounds: Rectangle
    module_size_px: float


@dataclass(frozen=True)
class CaptureDataHeader:
    """The frames the markers were read from (stored size, nominal frame rate as a fraction, 0/0 = unknown, source size, the stored region
    of the source, empty = all of it), where the markers are (the main marker first), whether the frames were stored too (frames.mbfc) and
    whether it is an EXPERIMENTAL camera capture. sync_region is the region of the source stored below region for the sync marker (a
    capture that stores only the markers keeps the two as one frame, the main marker's region on top); empty = none."""

    width: int
    height: int
    frame_rate_numerator: int
    frame_rate_denominator: int
    source_width: int
    source_height: int
    region: Rectangle
    markers: tuple[MarkerLocation, ...]
    frames_stored: bool
    camera: bool
    sync_region: Rectangle = Rectangle(0, 0, 0, 0)

    @staticmethod
    def parse(data: bytes) -> "CaptureDataHeader":
        """Parse a header. Raises DataFormatError for another file, a newer format version or wrong sizes."""
        if len(data) < HEADER_SIZE:
            raise DataFormatError("Not an mb-framepacing capture data file (.mbcd): too short")
        fields = cast(tuple[int, ...], _HEADER.unpack_from(data))
        magic, version, header_size, record_size, flags = fields[0], fields[1], fields[2], fields[3], fields[4]
        if magic != MAGIC:
            raise DataFormatError("Not an mb-framepacing capture data file (.mbcd)")
        if version > FORMAT_VERSION:
            raise DataFormatError(
                f"The capture data file has format version {version}, newer than this reader reads ({FORMAT_VERSION}): update the tools or the library"
            )
        if version != FORMAT_VERSION:
            raise DataFormatError(f"Unsupported capture data file format version {version}")
        if header_size != HEADER_SIZE or record_size != RECORD_SIZE:
            raise DataFormatError("Unexpected capture data header or record size")
        marker_count = fields[15]
        if marker_count > MAX_MARKERS:
            raise DataFormatError("Invalid marker count in the capture data header")
        markers: list[MarkerLocation] = []
        for i in range(marker_count):
            x, y, w, h, module = cast(tuple[int, int, int, int, float], _MARKER.unpack_from(data, _MARKERS_OFFSET + (i * _MARKER.size)))
            markers.append(MarkerLocation(Rectangle(x, y, w, h), module))
        return CaptureDataHeader(
            width=fields[5],
            height=fields[6],
            frame_rate_numerator=fields[7],
            frame_rate_denominator=fields[8],
            source_width=fields[9],
            source_height=fields[10],
            region=Rectangle(fields[11], fields[12], fields[13], fields[14]),
            markers=tuple(markers),
            frames_stored=(flags & _FRAMES_STORED) != 0,
            camera=(flags & _CAMERA) != 0,
            sync_region=Rectangle(*cast(tuple[int, int, int, int], _SYNC_REGION.unpack_from(data, _SYNC_REGION_OFFSET))),
        )


@dataclass(frozen=True)
class CaptureDataRecord:
    """One capture: the source's frame counter (gaps are captures the recorder dropped), when it arrived on the host's steady clock and the
    device's timestamp (100 ns ticks since the capture started; UNKNOWN_TICKS when the device gave none), how many frames the source
    reported dropping since the previous record, the status, and the main and second markers' bytes as read (None when not read)."""

    capture_index: int
    host_ticks: int
    device_ticks: int
    source_drops: int
    capture_status: CaptureDataStatus
    main_bytes: bytes | None
    second_bytes: bytes | None

    @property
    def has_device_ticks(self) -> bool:
        return self.device_ticks != UNKNOWN_TICKS

    def try_decode_main(self) -> "tuple[Payload, StartMetadata | None] | None":
        """The main marker's payload (and a start marker's metadata), decoded with mb_framepacing.marker; None when there is none or it is not valid."""
        return _decode(self.main_bytes)

    def try_decode_second(self) -> "Payload | None":
        """The second marker's payload (a sync marker), decoded with mb_framepacing.marker."""
        decoded = _decode(self.second_bytes)
        return decoded[0] if decoded is not None else None

    @staticmethod
    def parse(data: bytes | memoryview) -> "CaptureDataRecord":
        """Parse a record. Raises DataFormatError for bytes that are not one: fewer than 192, an unknown status, or a marker longer than
        its slot."""
        if len(data) < RECORD_SIZE:
            raise DataFormatError("A capture data record is 192 bytes")
        capture_index, host, device, source_drops, status, main_length, second_length = cast(
            tuple[int, int, int, int, int, int, int], _RECORD.unpack_from(data)
        )
        if status > CaptureDataStatus.TORN or main_length > MAIN_CAPACITY or second_length > SECOND_CAPACITY:
            raise DataFormatError("Invalid capture data record")
        main_offset = 32
        second_offset = main_offset + MAIN_CAPACITY
        return CaptureDataRecord(
            capture_index=capture_index,
            host_ticks=host,
            device_ticks=device,
            source_drops=source_drops,
            capture_status=CaptureDataStatus(status),
            main_bytes=bytes(data[main_offset : main_offset + main_length]) if main_length > 0 else None,
            second_bytes=bytes(data[second_offset : second_offset + second_length]) if second_length > 0 else None,
        )


class CaptureDataReader:
    """Reads captures.mbcd: the header, then the records by index or in order. A partial last record (a capture stopped mid-write) is
    ignored. Use it as a context manager, or call close()."""

    def __init__(self, path: str | Path) -> None:
        self._file: BinaryIO = open(path, "rb")  # noqa: SIM115 (closed by close() / __exit__)
        try:
            self.header: CaptureDataHeader = CaptureDataHeader.parse(self._file.read(HEADER_SIZE))
            _ = self._file.seek(0, 2)
            self.record_count: int = (self._file.tell() - HEADER_SIZE) // RECORD_SIZE
        except BaseException:
            self._file.close()
            raise

    def read_record(self, index: int) -> CaptureDataRecord:
        """The record at index (0 to record_count - 1; another index raises IndexError)."""
        if index < 0 or index >= self.record_count:
            raise IndexError(f"record {index} of {self.record_count}")
        _ = self._file.seek(HEADER_SIZE + (index * RECORD_SIZE))
        return CaptureDataRecord.parse(self._file.read(RECORD_SIZE))

    def records(self) -> Iterator[CaptureDataRecord]:
        """Every record, in file order. read_record, or another walk, may be used while this one is under way."""
        batch = 4096
        for first in range(0, self.record_count, batch):
            count = min(batch, self.record_count - first)
            # Each batch from its own place: the file's position is shared with read_record and other walks
            _ = self._file.seek(HEADER_SIZE + (first * RECORD_SIZE))
            view = memoryview(self._file.read(count * RECORD_SIZE))
            for i in range(count):
                yield CaptureDataRecord.parse(view[i * RECORD_SIZE : (i + 1) * RECORD_SIZE])

    def read_all(self) -> list[CaptureDataRecord]:
        return list(self.records())

    def close(self) -> None:
        self._file.close()

    def __enter__(self) -> "CaptureDataReader":
        return self

    def __exit__(self, *_: object) -> None:
        self.close()


def _decode(data: bytes | None) -> "tuple[Payload, StartMetadata | None] | None":
    if data is None:
        return None
    return try_decode_payload(data)
