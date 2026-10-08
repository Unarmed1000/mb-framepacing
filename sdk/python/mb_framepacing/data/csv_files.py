# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""A run's frames CSV (run-<id>-frames.csv) and captures.csv (doc/analysis-output-format.md): a header line, then one line per presented
frame or per capture, comma separated. Columns are found by their name, so columns an older file lacks read as None and columns a newer
one adds are ignored. Every number is a whole one, written as its digits with a '-' in front when negative; a time is its whole
nanoseconds (the '...Ns' columns), exactly as a marker carried it. An empty cell is None. Content that is anything else raises
DataFormatError, with the file and the line. A file from before the nanoseconds named its times '...Ticks': a frames CSV from then lacks
its required columns and is refused, and a captures.csv from then reads as captures without times."""

import re
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path

from .errors import DataFormatError

ON_DEMAND_FRAME_NS = 0xFFFF_FFFF
"""The marker's target and preferred frame time of an application that presents only when something changes (the marker's 0xFFFFFFFF)."""


@dataclass(frozen=True)
class FrameRow:
    """One presented frame. first_seen_ns is its display time (the capture's clock), display_delta_ns the display time step,
    animation_error_ns the animation time step minus the display time step (None for a step from a static frame). flags holds
    SkippedBefore, UncertainStart, Torn, Late, StaticAfter, StaticBefore, UncertainStep or StaticAssumed. The pacing and CPU fields come from the markers (CPU start time, CPU busy,
    frametime and CPU wait as PresentMon names them); marker_target_ns and marker_preferred_ns are ON_DEMAND_FRAME_NS on demand,
    target_ns and preferred_ns (what the analysis measured against, in whole refreshes) None then. The last two are EXPERIMENTAL
    camera captures' only. older_frames are the captures that showed an older frame out of order while this frame was the newest:
    (frame index, capture nanoseconds) in capture order."""

    segment: int
    frame_index: int
    animation_ns: int
    first_capture_index: int
    first_seen_ns: int
    on_screen_ns: int
    captures: int
    skipped_before: int
    display_delta_ns: int | None
    animation_delta_ns: int | None
    animation_error_ns: int | None
    drift_ns: int
    flags: tuple[str, ...]
    intended_display_ns: int | None
    marker_target_ns: int | None
    target_ns: int | None
    marker_preferred_ns: int | None
    preferred_ns: int | None
    pacing_error_ns: int | None
    prediction_error_ns: int | None
    lateness_ns: int | None
    last_seen_ns: int | None
    cpu_start_ns: int | None
    cpu_busy_ns: int | None
    frame_time_ns: int | None
    cpu_wait_ns: int | None
    older_frames: tuple[tuple[int, int], ...]
    main_marker_first_seen_ns: int | None = None
    scanout_delay_ns: int | None = None


@dataclass(frozen=True)
class CaptureCsvRow:
    """One capture as the analysis read it. capture_ns is None for a capture the recorder dropped (status NotRecorded); kind, run_id,
    frame_index and animation_ns are the main marker's, when one was read; payload its bytes. source_drops_before: frames the source
    reported dropping before it; missed_before: refreshes the device clock says were missed since the previous capture (0 on the host
    clock); sync_run_id and sync_frame_index: the sync marker's, when it was read."""

    capture_index: int
    capture_ns: int | None
    capture_status: str
    kind: str | None
    run_id: int | None
    frame_index: int | None
    animation_ns: int | None
    source_drops_before: int
    missed_before: int
    sync_run_id: int | None
    sync_frame_index: int | None
    host_ns: int | None
    device_ns: int | None
    payload: bytes | None


_INT32 = (-(2**31), 2**31 - 1)
_INT64 = (-(2**63), 2**63 - 1)
_UINT32 = (0, 2**32 - 1)
_UINT64 = (0, 2**64 - 1)
_WHOLE_NUMBER = re.compile(r"-?[0-9]+", re.ASCII)
_HEX_BYTES = re.compile(r"(?:[0-9A-Fa-f]{2})*", re.ASCII)


def _whole(text: str, limits: tuple[int, int]) -> int:
    """A whole number within the limits, all of the text: digits, with a '-' in front when negative. int() alone would take '+5', ' 5',
    '1_000' and other scripts' digits."""
    if not text:
        raise DataFormatError("An empty cell where a whole number is required")
    if _WHOLE_NUMBER.fullmatch(text) is None:
        raise DataFormatError(f"'{text}' is not a whole number")
    value = int(text)
    if not limits[0] <= value <= limits[1]:
        raise DataFormatError(f"'{text}' is outside its range ({limits[0]} to {limits[1]})")
    return value


def _entries(cell: str, column: str) -> list[str]:
    """A cell's entries, separated by '|': none for an empty cell."""
    if not cell:
        return []
    entries = cell.split("|")
    if "" in entries:
        raise DataFormatError(f"An empty entry in {column} '{cell}'")
    return entries


class _Table:
    """The cells of a CSV file's lines, by column name."""

    def __init__(self, path: Path) -> None:
        try:
            with open(path, encoding="utf-8", newline=None) as file:
                lines = [line.rstrip("\n") for line in file]
        except UnicodeDecodeError as error:
            raise DataFormatError(f"'{path}' is not UTF-8 text") from error
        if not lines:
            raise DataFormatError(f"'{path}' is empty")
        self.name: str = path.name
        self.columns: dict[str, int] = {name: index for index, name in enumerate(lines[0].split(","))}
        # Each row with its line in the file (the header is line 1)
        self.rows: list[tuple[int, list[str]]] = [(number, line.split(",")) for number, line in enumerate(lines[1:], start=2) if line]

    def read[T](self, parse: Callable[["_Table", list[str]], T]) -> list[T]:
        """Every row parsed; an error gets the file and the line in front."""
        parsed: list[T] = []
        for number, row in self.rows:
            try:
                parsed.append(parse(self, row))
            except DataFormatError as error:
                raise DataFormatError(f"'{self.name}' line {number}: {error}") from error
        return parsed

    def cell(self, row: list[str], name: str) -> str:
        index = self.columns.get(name, -1)
        return row[index] if 0 <= index < len(row) else ""

    def whole(self, row: list[str], name: str, limits: tuple[int, int]) -> int:
        return _whole(self.cell(row, name), limits)

    def optional_whole(self, row: list[str], name: str, limits: tuple[int, int]) -> int | None:
        text = self.cell(row, name)
        return _whole(text, limits) if text else None

    def ns(self, row: list[str], name: str) -> int:
        """A span's or a point in time's cell: its nanoseconds."""
        return _whole(self.cell(row, name), _INT64)

    def optional_ns(self, row: list[str], name: str) -> int | None:
        return self.optional_whole(row, name, _INT64)

    def optional_ns32(self, row: list[str], name: str) -> int | None:
        """A marker's 32-bit span (0 to ON_DEMAND_FRAME_NS), as the marker carried it."""
        return self.optional_whole(row, name, _UINT32)


def read_frames(path: str | Path) -> list[FrameRow]:
    """A run's frames CSV (its name is in summary.json: SummaryRun.frames_file). Raises DataFormatError for content that is not one."""
    return _Table(Path(path)).read(_frame)


def _frame(table: _Table, row: list[str]) -> FrameRow:
    return FrameRow(
        segment=table.whole(row, "segment", _INT32),
        frame_index=table.whole(row, "frameIndex", _UINT64),
        animation_ns=table.ns(row, "animationNs"),
        first_capture_index=table.whole(row, "firstCaptureIndex", _INT64),
        first_seen_ns=table.ns(row, "firstSeenNs"),
        on_screen_ns=table.ns(row, "onScreenNs"),
        captures=table.whole(row, "captures", _INT32),
        skipped_before=table.whole(row, "skippedBefore", _UINT64),
        display_delta_ns=table.optional_ns(row, "displayDeltaNs"),
        animation_delta_ns=table.optional_ns(row, "animationDeltaNs"),
        animation_error_ns=table.optional_ns(row, "animationErrorNs"),
        drift_ns=table.ns(row, "driftNs"),
        flags=tuple(_entries(table.cell(row, "flags"), "flags")),
        intended_display_ns=table.optional_ns(row, "intendedDisplayNs"),
        marker_target_ns=table.optional_ns32(row, "markerTargetNs"),
        target_ns=table.optional_ns(row, "targetNs"),
        marker_preferred_ns=table.optional_ns32(row, "markerPreferredNs"),
        preferred_ns=table.optional_ns(row, "preferredNs"),
        pacing_error_ns=table.optional_ns(row, "pacingErrorNs"),
        prediction_error_ns=table.optional_ns(row, "predictionErrorNs"),
        lateness_ns=table.optional_ns(row, "latenessNs"),
        last_seen_ns=table.optional_ns(row, "lastSeenNs"),
        cpu_start_ns=table.optional_ns(row, "cpuStartNs"),
        cpu_busy_ns=table.optional_ns32(row, "cpuBusyNs"),
        frame_time_ns=table.optional_ns(row, "frameTimeNs"),
        cpu_wait_ns=table.optional_ns(row, "cpuWaitNs"),
        older_frames=_older_frames(table.cell(row, "olderFrames")),
        main_marker_first_seen_ns=table.optional_ns(row, "mainMarkerFirstSeenNs"),
        scanout_delay_ns=table.optional_ns(row, "scanoutDelayNs"),
    )


def _older_frames(cell: str) -> tuple[tuple[int, int], ...]:
    """The olderFrames cell: frameIndex@captureNs entries separated by |, empty when none."""
    older: list[tuple[int, int]] = []
    for entry in _entries(cell, "olderFrames"):
        index, at, capture_ns = entry.partition("@")
        if not at or not index:
            raise DataFormatError(f"Invalid olderFrames entry '{entry}'")
        older.append((_whole(index, _UINT64), _whole(capture_ns, _INT64)))
    return tuple(older)


def read_captures(path: str | Path) -> list[CaptureCsvRow]:
    """captures.csv. Raises DataFormatError for content that is not one."""
    return _Table(Path(path)).read(_capture)


def _capture(table: _Table, row: list[str]) -> CaptureCsvRow:
    payload = table.cell(row, "payloadHex")
    if _HEX_BYTES.fullmatch(payload) is None:
        raise DataFormatError(f"'{payload}' is not hexadecimal bytes")
    return CaptureCsvRow(
        capture_index=table.whole(row, "captureIndex", _INT64),
        capture_ns=table.optional_ns(row, "captureNs"),
        capture_status=table.cell(row, "status"),
        kind=table.cell(row, "kind") or None,
        run_id=table.optional_whole(row, "runId", _UINT32),
        frame_index=table.optional_whole(row, "frameIndex", _UINT64),
        animation_ns=table.optional_ns(row, "animationNs"),
        source_drops_before=table.optional_whole(row, "sourceDropsBefore", _INT64) or 0,
        missed_before=table.optional_whole(row, "missedBefore", _INT64) or 0,
        sync_run_id=table.optional_whole(row, "syncRunId", _UINT32),
        sync_frame_index=table.optional_whole(row, "syncFrameIndex", _UINT64),
        host_ns=table.optional_ns(row, "hostNs"),
        device_ns=table.optional_ns(row, "deviceNs"),
        payload=bytes.fromhex(payload) if payload else None,
    )
