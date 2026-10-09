# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""EXPERIMENTAL (measure/doc/perfetto.md). A capture's measurements as the tracks of a trace: what trace_augment.py adds to a
Perfetto trace, as plain values with nothing of Perfetto in it (trace_tracks.py writes them).

A track is a line of the trace viewer. Each holds one kind of thing, in time order, and none of its slices overlap:

    On screen (measured)             a slice per frame, from its display time for its time on screen
    Intended display (the pacer's)   a slice per frame from its intended display time to the next frame's
    CPU (from the markers)           a slice from a frame's CPU start time for its CPU busy time
    Animation error (ms)             a value per frame that has one, at its display time
    Display time step (ms)           the same for the display time step
    Lateness (ms)                    the same for how late a frame was against the pacer's schedule
    Frame events                     late frames, frame indices that were skipped, frames out of order, torn frames
    Capture events                   captures not decoded, dropped by the source, or missed

The names say what the capture's data says and no more (sdk/doc/vocabulary.md has the terms). Every time is on the system
clock the application's clock is (trace_alignment.Alignment), in whole nanoseconds."""

from collections.abc import Iterator, Sequence
from dataclasses import dataclass
from enum import Enum
from typing import Protocol

from trace_alignment import Alignment, TimedFrame  # pyright: ignore[reportImplicitRelativeImport] (a script next to this one: its folder is on sys.path)

NS_PER_MS = 1_000_000

ArgValue = int | float | str | bool


class TraceFormatError(ValueError):
    """A file that tracks can not be added to as it is, or a time that a trace can not hold. The message says what to do."""


class TraceReadError(RuntimeError):
    """A trace that could not be read: the trace processor could not be had or started, or it refused the file."""


class Track(Enum):
    """The tracks, in the order they are shown. The value is the track's name in the trace."""

    ON_SCREEN = "On screen (measured)"
    INTENDED = "Intended display (the pacer's)"
    CPU = "CPU (from the markers)"
    ANIMATION_ERROR = "Animation error (ms)"
    DISPLAY_STEP = "Display time step (ms)"
    LATENESS = "Lateness (ms)"
    FRAME_EVENTS = "Frame events"
    CAPTURE_EVENTS = "Capture events"


COUNTER_TRACKS = frozenset({Track.ANIMATION_ERROR, Track.DISPLAY_STEP, Track.LATENESS})

TRACK_DESCRIPTIONS: dict[Track, str] = {
    Track.ON_SCREEN: "Each frame from the moment a capture of the display first showed it for as long as it stayed. Measured by mb-framepacing.",
    Track.INTENDED: "When the application's frame pacer aimed for each frame to be shown, as its marker carried it.",
    Track.CPU: "Each frame's CPU start time and CPU busy time, as its marker carried them.",
    Track.ANIMATION_ERROR: "Animation time step minus display time step, per frame. Zero is a frame shown for exactly as long as it animated for.",
    Track.DISPLAY_STEP: "How long the frame before stayed on screen: the time from its display time to this frame's.",
    Track.LATENESS: "How long after its intended display time a frame was shown, by the run's on-time frames.",
    Track.FRAME_EVENTS: "What the capture found about the frames: late ones, frame indices that were skipped, frames out of order, torn frames.",
    Track.CAPTURE_EVENTS: "Where the capture itself has gaps: captures that were not decoded, dropped by the source or missed.",
}


class Frame(TimedFrame, Protocol):
    """What the tracks read of a presented frame: mb_framepacing.data's FrameRow has it."""

    @property
    def on_screen_ns(self) -> int: ...
    @property
    def captures(self) -> int: ...
    @property
    def skipped_before(self) -> int: ...
    @property
    def display_delta_ns(self) -> int | None: ...
    @property
    def animation_delta_ns(self) -> int | None: ...
    @property
    def animation_error_ns(self) -> int | None: ...
    @property
    def flags(self) -> tuple[str, ...]: ...
    @property
    def target_ns(self) -> int | None: ...
    @property
    def older_frames(self) -> tuple[tuple[int, int], ...]: ...


class Capture(Protocol):
    """What the tracks read of a capture: mb_framepacing.data's CaptureCsvRow has it."""

    @property
    def capture_index(self) -> int: ...
    @property
    def capture_ns(self) -> int | None: ...
    @property
    def capture_status(self) -> str: ...
    @property
    def source_drops_before(self) -> int: ...
    @property
    def missed_before(self) -> int: ...


@dataclass(frozen=True)
class Slice:
    """A stretch of time on a track. flow joins it to the slice of another track that has the same number (0: none)."""

    track: Track
    start_ns: int
    end_ns: int
    name: str
    args: tuple[tuple[str, ArgValue], ...] = ()
    flow: int = 0


@dataclass(frozen=True)
class Instant:
    """A moment on a track."""

    track: Track
    time_ns: int
    name: str
    args: tuple[tuple[str, ArgValue], ...] = ()


@dataclass(frozen=True)
class Counter:
    """A value on a counter track, from time_ns until the next one."""

    track: Track
    time_ns: int
    value: float


Event = Slice | Instant | Counter


def milliseconds(nanoseconds: int) -> float:
    """Nanoseconds as milliseconds: one division, so a whole number of nanoseconds gives the same double everywhere."""
    return nanoseconds / NS_PER_MS


def frame_name(frame_index: int) -> str:
    return f"#{frame_index}"


def in_stretch[F: Frame](frames: Sequence[F], from_ns: int | None, to_ns: int | None) -> list[F]:
    """The frames whose display time on the capture's clock is from from_ns up to to_ns (either may be None: no limit)."""
    return [f for f in frames if (from_ns is None or f.first_seen_ns >= from_ns) and (to_ns is None or f.first_seen_ns < to_ns)]


def _frame_args(frame: Frame, period_ns: int) -> tuple[tuple[str, ArgValue], ...]:
    args: list[tuple[str, ArgValue]] = [("frame index", frame.frame_index), ("on screen (ms)", milliseconds(frame.on_screen_ns)), ("captures", frame.captures)]
    if frame.display_delta_ns is not None:
        args.append(("display time step (ms)", milliseconds(frame.display_delta_ns)))
        if period_ns > 0:
            args.append(("display time step (refreshes)", (frame.display_delta_ns + (period_ns // 2)) // period_ns))
    if frame.animation_delta_ns is not None:
        args.append(("animation time step (ms)", milliseconds(frame.animation_delta_ns)))
    if frame.animation_error_ns is not None:
        args.append(("animation error (ms)", milliseconds(frame.animation_error_ns)))
    if frame.lateness_ns is not None:
        args.append(("lateness (ms)", milliseconds(frame.lateness_ns)))
    if frame.target_ns is not None:
        args.append(("target frame time (ms)", milliseconds(frame.target_ns)))
    if frame.skipped_before > 0:
        args.append(("frame indices skipped before", frame.skipped_before))
    if frame.flags:
        args.append(("flags", ", ".join(frame.flags)))
    return tuple(args)


def on_screen(frames: Sequence[Frame], alignment: Alignment, period_ns: int) -> Iterator[Slice]:
    """A slice per frame for its time on screen, cut where the next frame's begins (one track holds no two slices at once). The
    slice's flow number is the frame's index plus one, as its CPU slice's is."""
    for index, frame in enumerate(frames):
        start = alignment.display_time(frame.first_seen_ns)
        end = start + max(frame.on_screen_ns, 0)
        if (index + 1) < len(frames):
            end = min(end, alignment.display_time(frames[index + 1].first_seen_ns))
        has_cpu = frame.cpu_start_ns is not None and frame.cpu_busy_ns is not None
        yield Slice(
            Track.ON_SCREEN, start, max(end, start), frame_name(frame.frame_index), _frame_args(frame, period_ns), frame.frame_index + 1 if has_cpu else 0
        )


def intended(frames: Sequence[Frame], alignment: Alignment) -> Iterator[Slice]:
    """A slice per frame from its intended display time to the next frame's that is not before it. The last frame's is as long
    as its target frame time, or has no length. A frame whose intended display time is before the one before it (a clock that
    went back, a marker that is wrong) gets none: a track is in time order."""
    scheduled = [f for f in frames if f.intended_display_ns is not None]
    last_end: int | None = None
    for index, frame in enumerate(scheduled):
        start = alignment.marker_time(frame.intended_display_ns or 0)
        if last_end is not None and start < last_end:
            continue
        end = start + max(frame.target_ns or 0, 0)
        for later in scheduled[index + 1 :]:
            following = alignment.marker_time(later.intended_display_ns or 0)
            if following >= start:
                end = following
                break
        last_end = end
        yield Slice(Track.INTENDED, start, end, frame_name(frame.frame_index), (("frame index", frame.frame_index),))


def cpu(frames: Sequence[Frame], alignment: Alignment) -> Iterator[Slice]:
    """A slice per frame from its CPU start time for its CPU busy time, cut where the next frame's begins. A frame whose CPU
    start is before the one before it (a clock that went back, a marker that is wrong) gets none: a track is in time order."""
    worked = [f for f in frames if f.cpu_start_ns is not None and f.cpu_busy_ns is not None]
    last_end: int | None = None
    for index, frame in enumerate(worked):
        start = alignment.marker_time(frame.cpu_start_ns or 0)
        if last_end is not None and start < last_end:
            continue
        end = start + (frame.cpu_busy_ns or 0)
        for later in worked[index + 1 :]:
            following = alignment.marker_time(later.cpu_start_ns or 0)
            if following >= start:
                end = min(end, following)
                break
        last_end = end
        args: tuple[tuple[str, ArgValue], ...] = (("frame index", frame.frame_index), ("CPU busy (ms)", milliseconds(frame.cpu_busy_ns or 0)))
        yield Slice(Track.CPU, start, end, frame_name(frame.frame_index), args, frame.frame_index + 1)


def counters(frames: Sequence[Frame], alignment: Alignment) -> Iterator[Counter]:
    """The three per-frame values, each at its frame's display time."""
    for frame in frames:
        time = alignment.display_time(frame.first_seen_ns)
        if frame.animation_error_ns is not None:
            yield Counter(Track.ANIMATION_ERROR, time, milliseconds(frame.animation_error_ns))
        if frame.display_delta_ns is not None:
            yield Counter(Track.DISPLAY_STEP, time, milliseconds(frame.display_delta_ns))
        if frame.lateness_ns is not None:
            yield Counter(Track.LATENESS, time, milliseconds(frame.lateness_ns))


def frame_events(frames: Sequence[Frame], alignment: Alignment) -> list[Instant]:
    """What the capture found about the frames, in time order. A frame out of order is at the capture that showed it."""
    events: list[Instant] = []
    for frame in frames:
        time = alignment.display_time(frame.first_seen_ns)
        name = frame_name(frame.frame_index)
        index_arg: tuple[tuple[str, ArgValue], ...] = (("frame index", frame.frame_index),)
        if frame.skipped_before > 0:
            plural = "index" if frame.skipped_before == 1 else "indices"
            events.append(Instant(Track.FRAME_EVENTS, time, f"{frame.skipped_before} frame {plural} skipped before {name}", index_arg))
        if "Late" in frame.flags:
            late_args = index_arg + ((("lateness (ms)", milliseconds(frame.lateness_ns)),) if frame.lateness_ns is not None else ())
            events.append(Instant(Track.FRAME_EVENTS, time, f"late {name}", late_args))
        if "Torn" in frame.flags:
            events.append(Instant(Track.FRAME_EVENTS, time, f"torn {name}", index_arg))
        for older_index, capture_ns in frame.older_frames:
            older_time = alignment.capture_time(capture_ns) - alignment.vblank_shift_ns
            events.append(Instant(Track.FRAME_EVENTS, older_time, f"out of order: {frame_name(older_index)} while {name} was the newest", index_arg))
    events.sort(key=lambda event: event.time_ns)
    return events


def capture_events(captures: Sequence[Capture], alignment: Alignment, capture_range: tuple[int, int]) -> list[Instant]:
    """The capture's own gaps, of the captures from capture_range's first time to its last on the capture's clock, in time
    order. A capture that was not recorded has no time of its own: it is counted at the next capture that has one."""
    first, last = capture_range
    events: list[Instant] = []
    not_recorded = 0
    for capture in captures:
        if capture.capture_ns is None:
            not_recorded += 1 if capture.capture_status == "NotRecorded" else 0
            continue
        if not first <= capture.capture_ns <= last:
            not_recorded = 0
            continue
        time = alignment.capture_time(capture.capture_ns) - alignment.vblank_shift_ns
        args: tuple[tuple[str, ArgValue], ...] = (("capture index", capture.capture_index),)
        if not_recorded > 0:
            events.append(Instant(Track.CAPTURE_EVENTS, time, f"not recorded: {not_recorded} before this capture", args))
        not_recorded = 0
        if capture.source_drops_before > 0:
            events.append(Instant(Track.CAPTURE_EVENTS, time, f"dropped by the source: {capture.source_drops_before} before this capture", args))
        if capture.missed_before > 0:
            events.append(Instant(Track.CAPTURE_EVENTS, time, f"missed: {capture.missed_before} before this capture", args))
        if capture.capture_status == "Undecodable":
            events.append(Instant(Track.CAPTURE_EVENTS, time, "not decoded", args))
    events.sort(key=lambda event: event.time_ns)
    return events


def capture_range_of(frames: Sequence[Frame]) -> tuple[int, int]:
    """From the first frame's display time to the end of the last frame's time on screen, on the capture's clock."""
    if not frames:
        return (0, -1)
    return (min(f.first_seen_ns for f in frames), max(f.first_seen_ns + max(f.on_screen_ns, 0) for f in frames))


def track_events(track: Track, frames: Sequence[Frame], captures: Sequence[Capture], alignment: Alignment, period_ns: int) -> Iterator[Event]:
    """One track's events, in time order."""
    if track is Track.ON_SCREEN:
        yield from on_screen(frames, alignment, period_ns)
    elif track is Track.INTENDED:
        yield from intended(frames, alignment)
    elif track is Track.CPU:
        yield from cpu(frames, alignment)
    elif track in COUNTER_TRACKS:
        yield from (counter for counter in counters(frames, alignment) if counter.track is track)
    elif track is Track.FRAME_EVENTS:
        yield from frame_events(frames, alignment)
    else:
        yield from capture_events(captures, alignment, capture_range_of(frames))
