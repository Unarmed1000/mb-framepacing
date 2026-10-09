# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""EXPERIMENTAL (measure/doc/perfetto.md). Where a capture's frames are on the clocks of a Perfetto trace of the same run: the
clock arithmetic of trace_augment.py, with nothing of Perfetto in it.

Three clocks meet. The capture's (the capture card's, or the host's): a frame's display time, firstSeenNs. The application's
steady clock, which the markers carry the CPU start time and the pacer's intended display time on. And the trace's, which
ties the system's clocks together with its own clock snapshots.

- The application's clock to the trace's: the application's clock is one of the system's clocks (CLOCK_MONOTONIC unless said
  otherwise), with an offset when it counts from an epoch of its own. Exact when that is true, and checked: the CPU start times
  have to lie inside the trace.
- The capture's clock to the application's: the analysis's own alignment, read back from the frames it wrote
  (firstSeenNs - intendedDisplayNs - latenessNs is one number for a run). Without a schedule in the markers, the charts' rule:
  no frame is on screen before its CPU work ended. The differences between frames are exact either way; where the whole run
  sits rests on its on-time frames, and a delay the whole run shares can not be told from the clocks' difference.
- A finer position, where the trace has the display's vertical blanks: the measured display times are whole refreshes apart,
  so all of them are moved by the one part of a refresh that puts them on the trace's vertical blanks. Which refresh still
  comes from the step above.

Every time is a whole number of nanoseconds and every result comes from integer arithmetic, so the same input gives the same
numbers everywhere."""

import bisect
from collections.abc import Sequence
from dataclasses import dataclass
from enum import Enum
from typing import Protocol

# Perfetto's ids of the system's clocks (its BuiltinClock), by the names --app-clock takes
SYSTEM_CLOCK_IDS: dict[str, int] = {"monotonic": 3, "boottime": 6, "monotonic-raw": 5, "realtime": 1}


class AlignmentError(ValueError):
    """The capture and the trace do not fit together, or the capture has nothing to place it by. The message says what to do."""


class Anchor(Enum):
    """What placed the capture's clock against the application's."""

    SCHEDULE = "the pacer's intended display times (the analysis's alignment)"
    CPU_WORK = "the CPU start and CPU busy times (no frame is on screen before its CPU work ended)"
    GIVEN = "the offset that was given"
    NOTHING = "nothing: the markers carry no times, so the capture's own clock is used as it is"


class TimedFrame(Protocol):
    """What the alignment reads of a presented frame: mb_framepacing.data's FrameRow has it."""

    @property
    def frame_index(self) -> int: ...
    @property
    def first_seen_ns(self) -> int: ...
    @property
    def intended_display_ns(self) -> int | None: ...
    @property
    def lateness_ns(self) -> int | None: ...
    @property
    def cpu_start_ns(self) -> int | None: ...
    @property
    def cpu_busy_ns(self) -> int | None: ...


@dataclass(frozen=True)
class ClockSnapshot:
    """One moment on two clocks: the trace's own time, and what a system clock read then."""

    trace_ns: int
    clock_ns: int


@dataclass(frozen=True)
class VBlankFit:
    """How the measured display times sit on the trace's vertical blanks. shift_ns was taken off every display time (it is
    within half a refresh either way); afterwards the display times of used_frames frames have a vertical blank within half a
    refresh, and the furthest of them is spread_ns from its vertical blank."""

    shift_ns: int
    used_frames: int
    spread_ns: int


@dataclass(frozen=True)
class Alignment:
    """A capture's clocks against a system clock. A time on the capture's clock less capture_minus_app_ns is on the
    application's clock; a time on the application's clock plus app_clock_offset_ns is on the system clock the application's
    clock is. vblank_shift_ns is taken off display times only (VBlankFit)."""

    anchor: Anchor
    capture_minus_app_ns: int
    app_clock_offset_ns: int = 0
    vblank_shift_ns: int = 0

    def marker_time(self, app_ns: int) -> int:
        """A time the markers carry (CPU start, intended display), on the system clock."""
        return app_ns + self.app_clock_offset_ns

    def capture_time(self, capture_ns: int) -> int:
        """A time on the capture's clock (a capture, an event of the capture), on the system clock."""
        return capture_ns - self.capture_minus_app_ns + self.app_clock_offset_ns

    def display_time(self, first_seen_ns: int) -> int:
        """A frame's display time on the system clock: its time on the capture's clock, moved onto the vertical blanks."""
        return self.capture_time(first_seen_ns) - self.vblank_shift_ns


def capture_minus_app(frames: Sequence[TimedFrame]) -> tuple[Anchor, int]:
    """The capture's clock minus the application's, from the frames of one run. With a schedule it is the analysis's own
    number, which every frame that has an intended display time and a lateness gives alike. Without one it is the smallest
    of display time minus the end of the CPU's work, so that no frame is on screen before its work ended. Without either the
    markers carry no times: the offset is zero and the anchor says so."""
    scheduled = [f for f in frames if f.intended_display_ns is not None and f.lateness_ns is not None]
    if scheduled:
        offsets = {f.first_seen_ns - (f.intended_display_ns or 0) - (f.lateness_ns or 0) for f in scheduled}
        if len(offsets) != 1:
            raise AlignmentError(
                "The frames of this run do not share one offset between the capture's clock and the pacer's "
                + f"(firstSeenNs - intendedDisplayNs - latenessNs has {len(offsets)} values): analyse the capture again."
            )
        return Anchor.SCHEDULE, offsets.pop()
    worked = [f for f in frames if f.cpu_start_ns is not None and f.cpu_busy_ns is not None]
    if worked:
        return Anchor.CPU_WORK, min(f.first_seen_ns - ((f.cpu_start_ns or 0) + (f.cpu_busy_ns or 0)) for f in worked)
    return Anchor.NOTHING, 0


def align(frames: Sequence[TimedFrame], app_clock_offset_ns: int = 0, given_capture_minus_app_ns: int | None = None) -> Alignment:
    """The alignment of a run's frames, before any vertical blanks: by the offset that was given, or else by the frames."""
    if given_capture_minus_app_ns is not None:
        return Alignment(Anchor.GIVEN, given_capture_minus_app_ns, app_clock_offset_ns)
    anchor, offset = capture_minus_app(frames)
    return Alignment(anchor, offset, app_clock_offset_ns)


def to_trace_ns(clock_ns: int, snapshots: Sequence[ClockSnapshot]) -> int:
    """A time on a system clock, on the trace's clock: by the newest snapshot at or before it (the first one for a time before
    them all), as the trace's own reader converts. snapshots are ascending by clock_ns. Without any, the system clock is taken
    to be the trace's clock."""
    if not snapshots:
        return clock_ns
    index = bisect.bisect_right([s.clock_ns for s in snapshots], clock_ns) - 1
    snapshot = snapshots[max(index, 0)]
    return snapshot.trace_ns + (clock_ns - snapshot.clock_ns)


def check_inside_trace(frames: Sequence[TimedFrame], alignment: Alignment, snapshots: Sequence[ClockSnapshot], trace_range: tuple[int, int]) -> None:
    """Fails when the markers' CPU start times, taken as times on the system clock, are not inside the trace: the application's
    clock is then not the clock that was named, or the trace is of another run. A trace may begin after the run did or end
    before it: at least half of the frames have to be inside."""
    starts = [to_trace_ns(alignment.marker_time(f.cpu_start_ns), snapshots) for f in frames if f.cpu_start_ns is not None]
    if not starts:
        return
    first, last = trace_range
    inside = sum(1 for start in starts if first <= start <= last)
    if (inside * 2) < len(starts):
        raise AlignmentError(
            f"Only {inside} of {len(starts)} CPU start times of the markers lie inside the trace "
            + f"(the markers have {min(starts)} to {max(starts)} ns on the trace's clock, the trace runs from {first} to {last}). "
            + "Either the trace is of another run, or the application's clock is not the one named: "
            + "see --app-clock and --app-clock-offset-ns."
        )


def nearest(sorted_times: Sequence[int], time: int) -> int | None:
    """The one of sorted_times nearest to time (the earlier of two that are as near), None when there is none."""
    if not sorted_times:
        return None
    index = bisect.bisect_left(sorted_times, time)
    if index == 0:
        return sorted_times[0]
    if index == len(sorted_times):
        return sorted_times[-1]
    before, after = sorted_times[index - 1], sorted_times[index]
    return before if (time - before) <= (after - time) else after


def fit_vblanks(display_times: Sequence[int], vblanks: Sequence[int], period_ns: int, min_frames: int = 8) -> VBlankFit | None:
    """How far the display times are off the vertical blanks, as one part of a refresh for all of them. display_times and
    vblanks are on the same clock, vblanks ascending. Each display time's distance past the vertical blank at or before it is
    a point on a circle of one refresh; the points of a run lie together, so the circle is cut in its widest empty stretch
    and the shift is the median of what is then a line. None when fewer than min_frames display times have a vertical blank
    within a refresh before them: the trace has no vertical blanks for the run."""
    if period_ns <= 0:
        raise AlignmentError(f"The refresh period has to be more than zero nanoseconds: {period_ns}.")
    phases: list[int] = []
    for time in display_times:
        index = bisect.bisect_right(vblanks, time) - 1
        if index >= 0 and (time - vblanks[index]) < period_ns:
            phases.append(time - vblanks[index])
    if len(phases) < min_frames:
        return None
    phases.sort()
    # The widest stretch of the circle that no point is in: between two neighbours, or across the end of the refresh
    cut = 0
    widest = phases[0] + period_ns - phases[-1]
    for index in range(1, len(phases)):
        if (phases[index] - phases[index - 1]) > widest:
            widest = phases[index] - phases[index - 1]
            cut = index
    line = phases[cut:] + [phase + period_ns for phase in phases[:cut]]
    median = line[(len(line) - 1) // 2] % period_ns
    # Past the middle of a refresh, the display times are a little before the next vertical blank, not long after this one
    shift = median if (median * 2) <= period_ns else median - period_ns

    half = period_ns // 2
    used = 0
    spread = 0
    for time in display_times:
        blank = nearest(vblanks, time - shift)
        if blank is not None and abs(time - shift - blank) <= half:
            used += 1
            spread = max(spread, abs(time - shift - blank))
    return VBlankFit(shift, used, spread)
