# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""A run's frames CSV (run-<id>-frames.csv) and captures.csv (doc/analysis-output-format.md): a header line, then one line per presented
frame or per capture, comma separated. Columns are found by their name, so columns an older file lacks read as None and columns a newer
one adds are ignored. Every number is a whole one, written as its digits with a '-' in front when negative; a time is its 100 ns ticks,
exactly as a marker carried it. An empty cell is None. Content that is anything else raises DataFormatError, with the file and the line."""

import re
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path

from .errors import DataFormatError

ON_DEMAND_FRAME_TICKS = 0xFFFF_FFFF
"""The marker's target and preferred frame time of an application that presents only when something changes (the marker's 0xFFFFFFFF)."""


@dataclass(frozen=True)
class FrameRow:
    """One presented frame. first_seen_ticks is its display time (the capture's clock), display_delta_ticks the display time step,
    animation_error_ticks the animation time step minus the display time step (None for a step from a static frame). flags holds
    SkippedBefore, UncertainStart, Torn, Late, StaticAfter, StaticBefore, UncertainStep or StaticAssumed. The pacing and CPU fields come from the markers (CPU start time, CPU busy,
    frametime and CPU wait as PresentMon names them); marker_target_ticks and marker_preferred_ticks are ON_DEMAND_FRAME_TICKS on demand,
    target_ticks and preferred_ticks (what the analysis measured against, in whole refreshes) None then. The last two are EXPERIMENTAL
    camera captures' only. older_frames are the captures that showed an older frame out of order while this frame was the newest:
    (frame index, capture ticks) in capture order."""

    segment: int
    frame_index: int
    animation_ticks: int
    first_capture_index: int
    first_seen_ticks: int
    on_screen_ticks: int
    captures: int
    skipped_before: int
    display_delta_ticks: int | None
    animation_delta_ticks: int | None
    animation_error_ticks: int | None
    drift_ticks: int
    flags: tuple[str, ...]
    intended_display_ticks: int | None
    marker_target_ticks: int | None
    target_ticks: int | None
    marker_preferred_ticks: int | None
    preferred_ticks: int | None
    pacing_error_ticks: int | None
    prediction_error_ticks: int | None
    lateness_ticks: int | None
    last_seen_ticks: int | None
    cpu_start_ticks: int | None
    cpu_busy_ticks: int | None
    frame_time_ticks: int | None
    cpu_wait_ticks: int | None
    older_frames: tuple[tuple[int, int], ...]
    main_marker_first_seen_ticks: int | None = None
    scanout_delay_ticks: int | None = None


@dataclass(frozen=True)
class CaptureCsvRow:
    """One capture as the analysis read it. capture_ticks is None for a capture the recorder dropped (status NotRecorded); kind, run_id,
    frame_index and animation_ticks are the main marker's, when one was read; payload its bytes. source_drops_before: frames the source
    reported dropping before it; missed_before: refreshes the device clock says were missed since the previous capture (0 on the host
    clock); sync_run_id and sync_frame_index: the sync marker's, when it was read."""

    capture_index: int
    capture_ticks: int | None
    capture_status: str
    kind: str | None
    run_id: int | None
    frame_index: int | None
    animation_ticks: int | None
    source_drops_before: int
    missed_before: int
    sync_run_id: int | None
    sync_frame_index: int | None
    host_ticks: int | None
    device_ticks: int | None
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

    def ticks(self, row: list[str], name: str) -> int:
        """A span's or a point in time's cell: its ticks."""
        return _whole(self.cell(row, name), _INT64)

    def optional_ticks(self, row: list[str], name: str) -> int | None:
        return self.optional_whole(row, name, _INT64)

    def optional_ticks32(self, row: list[str], name: str) -> int | None:
        """A marker's 32-bit span (0 to ON_DEMAND_FRAME_TICKS), as the marker carried it."""
        return self.optional_whole(row, name, _UINT32)


def read_frames(path: str | Path) -> list[FrameRow]:
    """A run's frames CSV (its name is in summary.json: SummaryRun.frames_file). Raises DataFormatError for content that is not one."""
    return _Table(Path(path)).read(_frame)


def _frame(table: _Table, row: list[str]) -> FrameRow:
    return FrameRow(
        segment=table.whole(row, "segment", _INT32),
        frame_index=table.whole(row, "frameIndex", _UINT64),
        animation_ticks=table.ticks(row, "animationTicks"),
        first_capture_index=table.whole(row, "firstCaptureIndex", _INT64),
        first_seen_ticks=table.ticks(row, "firstSeenTicks"),
        on_screen_ticks=table.ticks(row, "onScreenTicks"),
        captures=table.whole(row, "captures", _INT32),
        skipped_before=table.whole(row, "skippedBefore", _UINT64),
        display_delta_ticks=table.optional_ticks(row, "displayDeltaTicks"),
        animation_delta_ticks=table.optional_ticks(row, "animationDeltaTicks"),
        animation_error_ticks=table.optional_ticks(row, "animationErrorTicks"),
        drift_ticks=table.ticks(row, "driftTicks"),
        flags=tuple(_entries(table.cell(row, "flags"), "flags")),
        intended_display_ticks=table.optional_ticks(row, "intendedDisplayTicks"),
        marker_target_ticks=table.optional_ticks32(row, "markerTargetTicks"),
        target_ticks=table.optional_ticks(row, "targetTicks"),
        marker_preferred_ticks=table.optional_ticks32(row, "markerPreferredTicks"),
        preferred_ticks=table.optional_ticks(row, "preferredTicks"),
        pacing_error_ticks=table.optional_ticks(row, "pacingErrorTicks"),
        prediction_error_ticks=table.optional_ticks(row, "predictionErrorTicks"),
        lateness_ticks=table.optional_ticks(row, "latenessTicks"),
        last_seen_ticks=table.optional_ticks(row, "lastSeenTicks"),
        cpu_start_ticks=table.optional_ticks(row, "cpuStartTicks"),
        cpu_busy_ticks=table.optional_ticks32(row, "cpuBusyTicks"),
        frame_time_ticks=table.optional_ticks(row, "frameTimeTicks"),
        cpu_wait_ticks=table.optional_ticks(row, "cpuWaitTicks"),
        older_frames=_older_frames(table.cell(row, "olderFrames")),
        main_marker_first_seen_ticks=table.optional_ticks(row, "mainMarkerFirstSeenTicks"),
        scanout_delay_ticks=table.optional_ticks(row, "scanoutDelayTicks"),
    )


def _older_frames(cell: str) -> tuple[tuple[int, int], ...]:
    """The olderFrames cell: frameIndex@captureTicks entries separated by |, empty when none."""
    older: list[tuple[int, int]] = []
    for entry in _entries(cell, "olderFrames"):
        index, at, ticks = entry.partition("@")
        if not at or not index:
            raise DataFormatError(f"Invalid olderFrames entry '{entry}'")
        older.append((_whole(index, _UINT64), _whole(ticks, _INT64)))
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
        capture_ticks=table.optional_ticks(row, "captureTicks"),
        capture_status=table.cell(row, "status"),
        kind=table.cell(row, "kind") or None,
        run_id=table.optional_whole(row, "runId", _UINT32),
        frame_index=table.optional_whole(row, "frameIndex", _UINT64),
        animation_ticks=table.optional_ticks(row, "animationTicks"),
        source_drops_before=table.optional_whole(row, "sourceDropsBefore", _INT64) or 0,
        missed_before=table.optional_whole(row, "missedBefore", _INT64) or 0,
        sync_run_id=table.optional_whole(row, "syncRunId", _UINT32),
        sync_frame_index=table.optional_whole(row, "syncFrameIndex", _UINT64),
        host_ticks=table.optional_ticks(row, "hostTicks"),
        device_ticks=table.optional_ticks(row, "deviceTicks"),
        payload=bytes.fromhex(payload) if payload else None,
    )
