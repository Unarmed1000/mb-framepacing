# SPDX-License-Identifier: BSD-3-Clause
# Copyright (c) 2026, Mana Battery ApS

"""A run's frames CSV (run-<id>-frames.csv) and captures.csv (doc/analysis-output-format.md): a header line, then one line per presented
frame or per capture, comma separated. Columns are found by their name, so columns an older file lacks read as None and columns a newer
one adds are ignored. Times are 100 ns ticks; an empty cell is None."""

from dataclasses import dataclass
from pathlib import Path

from .analysis_files import parse_ticks
from .errors import DataFormatError


@dataclass(frozen=True)
class FrameRow:
    """One presented frame. first_seen_ticks is its display time (the capture's clock), display_delta_ticks the display time step,
    animation_error_ticks the animation time step minus the display time step. flags holds SkippedBefore, UncertainStart, Torn or Late.
    The pacing and CPU fields come from the markers (CPU start time, CPU busy, frametime and CPU wait as PresentMon names them). The last
    two are EXPERIMENTAL camera captures' only."""

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
    pacing_error_ticks: int | None
    prediction_error_ticks: int | None
    lateness_ticks: int | None
    last_seen_ticks: int | None
    cpu_start_ticks: int | None
    cpu_busy_ticks: int | None
    frame_time_ticks: int | None
    cpu_wait_ticks: int | None
    main_marker_first_seen_ticks: int | None = None
    scanout_delay_ticks: int | None = None


@dataclass(frozen=True)
class CaptureCsvRow:
    """One capture as the analysis read it. capture_ticks is None for a capture the recorder dropped (status NotRecorded); kind, run_id,
    frame_index and animation_ticks are the main marker's, when one was read; payload its bytes."""

    capture_index: int
    capture_ticks: int | None
    status: str
    kind: str | None
    run_id: int | None
    frame_index: int | None
    animation_ticks: int | None
    source_drop_before: bool
    host_ticks: int | None
    device_ticks: int | None
    payload: bytes | None
    second_zone_frame_index: int | None = None


class _Table:
    """The cells of a CSV file's lines, by column name."""

    def __init__(self, path: Path) -> None:
        with open(path, encoding="utf-8", newline=None) as file:
            lines = [line.rstrip("\n") for line in file]
        if not lines:
            raise DataFormatError(f"'{path}' is empty")
        self.columns: dict[str, int] = {name: index for index, name in enumerate(lines[0].split(","))}
        self.rows: list[list[str]] = [line.split(",") for line in lines[1:] if line]

    def cell(self, row: list[str], name: str) -> str:
        index = self.columns.get(name, -1)
        return row[index] if 0 <= index < len(row) else ""

    def integer(self, row: list[str], name: str) -> int:
        return int(self.cell(row, name))

    def optional_integer(self, row: list[str], name: str) -> int | None:
        text = self.cell(row, name)
        return int(text) if text else None

    def ticks(self, row: list[str], name: str) -> int:
        return parse_ticks(self.cell(row, name))

    def optional_ticks(self, row: list[str], name: str) -> int | None:
        text = self.cell(row, name)
        return parse_ticks(text) if text else None


def read_frames(path: str | Path) -> list[FrameRow]:
    """A run's frames CSV (its name is in summary.json: SummaryRun.frames_file)."""
    table = _Table(Path(path))
    frames: list[FrameRow] = []
    for row in table.rows:
        flags = table.cell(row, "flags")
        frames.append(
            FrameRow(
                segment=table.integer(row, "segment"),
                frame_index=table.integer(row, "frameIndex"),
                animation_ticks=table.ticks(row, "animationMs"),
                first_capture_index=table.integer(row, "firstCaptureIndex"),
                first_seen_ticks=table.ticks(row, "firstSeenMs"),
                on_screen_ticks=table.ticks(row, "onScreenMs"),
                captures=table.integer(row, "captures"),
                skipped_before=table.integer(row, "skippedBefore"),
                display_delta_ticks=table.optional_ticks(row, "displayDeltaMs"),
                animation_delta_ticks=table.optional_ticks(row, "animationDeltaMs"),
                animation_error_ticks=table.optional_ticks(row, "animationErrorMs"),
                drift_ticks=table.ticks(row, "driftMs"),
                flags=tuple(flags.split("|")) if flags else (),
                intended_display_ticks=table.optional_ticks(row, "intendedDisplayMs"),
                marker_target_ticks=table.optional_ticks(row, "markerTargetMs"),
                target_ticks=table.optional_ticks(row, "targetMs"),
                pacing_error_ticks=table.optional_ticks(row, "pacingErrorMs"),
                prediction_error_ticks=table.optional_ticks(row, "predictionErrorMs"),
                lateness_ticks=table.optional_ticks(row, "latenessMs"),
                last_seen_ticks=table.optional_ticks(row, "lastSeenMs"),
                cpu_start_ticks=table.optional_ticks(row, "cpuStartMs"),
                cpu_busy_ticks=table.optional_ticks(row, "cpuBusyMs"),
                frame_time_ticks=table.optional_ticks(row, "frameTimeMs"),
                cpu_wait_ticks=table.optional_ticks(row, "cpuWaitMs"),
                main_marker_first_seen_ticks=table.optional_ticks(row, "mainMarkerFirstSeenMs"),
                scanout_delay_ticks=table.optional_ticks(row, "scanoutDelayMs"),
            )
        )
    return frames


def read_captures(path: str | Path) -> list[CaptureCsvRow]:
    """captures.csv."""
    table = _Table(Path(path))
    captures: list[CaptureCsvRow] = []
    for row in table.rows:
        payload = table.cell(row, "payloadHex")
        captures.append(
            CaptureCsvRow(
                capture_index=table.integer(row, "captureIndex"),
                capture_ticks=table.optional_ticks(row, "captureMs"),
                status=table.cell(row, "status"),
                kind=table.cell(row, "kind") or None,
                run_id=table.optional_integer(row, "runId"),
                frame_index=table.optional_integer(row, "frameIndex"),
                animation_ticks=table.optional_ticks(row, "animationMs"),
                source_drop_before=table.cell(row, "sourceDropBefore") == "1",
                host_ticks=table.optional_ticks(row, "hostMs"),
                device_ticks=table.optional_ticks(row, "deviceMs"),
                payload=bytes.fromhex(payload) if payload else None,
                second_zone_frame_index=table.optional_integer(row, "secondZoneFrameIndex"),
            )
        )
    return captures
