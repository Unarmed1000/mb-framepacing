# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""The SDK's golden capture (sdk/test-data/data/60-busy-full-rate: a 60 Hz test clip imported and analysed, 388 frames with the
pacer's schedule in their markers), read with the SDK's own Python reader, and frames made up for a test."""

import sys
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
sys.path.insert(0, str(ROOT / "sdk" / "python"))
from mb_framepacing.data import (  # noqa: E402
    AnalysisSummary,
    CaptureCsvRow,
    FrameRow,
    find_analysis,
    read_captures,
    read_frames,
    read_summary,
)

CAPTURE = ROOT / "sdk" / "test-data" / "data" / "60-busy-full-rate"
PERIOD_NS = 16_666_667
# What the analysis lined the two clocks up by: the capture's clock minus the pacer's
CAPTURE_MINUS_PACER_NS = 50_000_000


def analysis_folder() -> Path:
    folder = find_analysis(CAPTURE)
    assert folder is not None, CAPTURE
    return folder


def summary() -> AnalysisSummary:
    return read_summary(analysis_folder() / "summary.json")


def frames() -> list[FrameRow]:
    return read_frames(analysis_folder() / summary().runs[0].frames_file)


def captures() -> list[CaptureCsvRow]:
    return read_captures(analysis_folder() / "captures.csv")


@dataclass(frozen=True)
class MadeFrame:
    """A frame made up for a test: what the tracks and the alignment read of one, with nothing a test does not set."""

    frame_index: int
    first_seen_ns: int
    on_screen_ns: int = PERIOD_NS
    captures: int = 1
    skipped_before: int = 0
    display_delta_ns: int | None = None
    animation_delta_ns: int | None = None
    animation_error_ns: int | None = None
    flags: tuple[str, ...] = ()
    intended_display_ns: int | None = None
    target_ns: int | None = None
    lateness_ns: int | None = None
    cpu_start_ns: int | None = None
    cpu_busy_ns: int | None = None
    older_frames: tuple[tuple[int, int], ...] = ()
    frame_time_ns: int | None = None


@dataclass(frozen=True)
class MadeCapture:
    """A capture made up for a test."""

    capture_index: int
    capture_ns: int | None
    capture_status: str = "Decoded"
    source_drops_before: int = 0
    missed_before: int = 0
