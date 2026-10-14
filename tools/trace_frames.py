# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""What a Perfetto trace says of each presented frame of a capture: the join behind
trace_augment.py --frames, with nothing of Perfetto in it (trace_source.py reads the trace).

A line per presented frame, in display order. Every time is whole nanoseconds on the trace's clock, and a cell is empty where
the trace does not say. The columns come in groups, each there when the trace has what it needs:

    frameIndex, displayTraceNs, cpuStartTraceNs, cpuEndTraceNs
        the frame itself: its display time as measured, and the CPU start time and the end of the CPU busy time its marker has
    vblankTraceNs, displayMinusVBlankNs
        the trace's vertical blank nearest the display time (within half a refresh), and how far the display time is from it
    surfaceFrameToken, displayFrameToken, layerName, presentType, jankType, jankTag, onTimeFinish, gpuComposition,
    systemPresentTraceNs, displayMinusSystemPresentNs
        the system's own account of the frame (Android's frame timeline): the application's frame whose time the end of the
        frame's CPU work lies in, what the system says of it, when it says the frame was presented, and the measured display
        time minus that
    cpuRunningNs, cpuRunnableNs, cpuSleepingNs
        of the frame's CPU busy time, on one thread: running, waiting for a CPU, and neither

Which frame of the trace is a frame of the capture is worked out from times alone (the end of the CPU's work), not from an id
the two share: a trace and a capture have none in common. No recorded trace has been through this yet."""

import bisect
import csv
import dataclasses
import io
from collections.abc import Mapping, Sequence
from dataclasses import dataclass

from trace_alignment import (  # pyright: ignore[reportImplicitRelativeImport] (a script next to this one: its folder is on sys.path)
    Alignment,
    ClockSnapshot,
    TimedFrame,
    nearest,
    to_trace_ns,
)

# How many of the application's frames that began before a moment are looked at for the one the moment lies in
OVERLAPPING_FRAMES = 16


@dataclass(frozen=True)
class SurfaceFrame:
    """One frame of the application as the system's frame timeline has it (an actual surface frame): from when the
    application began it to when it was handed over or its GPU work ended, whichever was later."""

    start_ns: int
    end_ns: int
    surface_frame_token: int
    display_frame_token: int | None
    layer_name: str | None
    present_type: str | None
    jank_type: str | None
    jank_tag: str | None
    on_time_finish: bool | None
    gpu_composition: bool | None


@dataclass(frozen=True)
class ThreadState:
    """A stretch of one thread's time in one scheduling state, as the trace names it ('Running', 'R', 'R+', 'S', 'D', ...)."""

    start_ns: int
    end_ns: int
    state: str


@dataclass(frozen=True)
class TraceFrame:
    """A presented frame with what the trace says of it. The field order is the CSV's column order."""

    frame_index: int
    display_trace_ns: int
    cpu_start_trace_ns: int | None = None
    cpu_end_trace_ns: int | None = None
    vblank_trace_ns: int | None = None
    display_minus_vblank_ns: int | None = None
    surface_frame_token: int | None = None
    display_frame_token: int | None = None
    layer_name: str | None = None
    present_type: str | None = None
    jank_type: str | None = None
    jank_tag: str | None = None
    on_time_finish: bool | None = None
    gpu_composition: bool | None = None
    system_present_trace_ns: int | None = None
    display_minus_system_present_ns: int | None = None
    cpu_running_ns: int | None = None
    cpu_runnable_ns: int | None = None
    cpu_sleeping_ns: int | None = None


# The CSV's columns, in the order of TraceFrame's fields: named as the analysis output names its own
COLUMNS: tuple[str, ...] = (
    "frameIndex",
    "displayTraceNs",
    "cpuStartTraceNs",
    "cpuEndTraceNs",
    "vblankTraceNs",
    "displayMinusVBlankNs",
    "surfaceFrameToken",
    "displayFrameToken",
    "layerName",
    "presentType",
    "jankType",
    "jankTag",
    "onTimeFinish",
    "gpuComposition",
    "systemPresentTraceNs",
    "displayMinusSystemPresentNs",
    "cpuRunningNs",
    "cpuRunnableNs",
    "cpuSleepingNs",
)


def cells(row: TraceFrame) -> tuple[int | str | bool | None, ...]:
    """A row's values, in the order of COLUMNS."""
    return (
        row.frame_index,
        row.display_trace_ns,
        row.cpu_start_trace_ns,
        row.cpu_end_trace_ns,
        row.vblank_trace_ns,
        row.display_minus_vblank_ns,
        row.surface_frame_token,
        row.display_frame_token,
        row.layer_name,
        row.present_type,
        row.jank_type,
        row.jank_tag,
        row.on_time_finish,
        row.gpu_composition,
        row.system_present_trace_ns,
        row.display_minus_system_present_ns,
        row.cpu_running_ns,
        row.cpu_runnable_ns,
        row.cpu_sleeping_ns,
    )


def surface_frame_at(surface_frames: Sequence[SurfaceFrame], starts: Sequence[int], time_ns: int) -> SurfaceFrame | None:
    """The application's frame that time_ns lies in: of those that began at or before it and had not ended, the one that began
    last. starts are the frames' start times, ascending as the frames are."""
    index = bisect.bisect_right(starts, time_ns) - 1
    for candidate in range(index, max(index - OVERLAPPING_FRAMES, -1), -1):
        if surface_frames[candidate].end_ns >= time_ns:
            return surface_frames[candidate]
    return None


def thread_time(states: Sequence[ThreadState], starts: Sequence[int], from_ns: int, to_ns: int) -> tuple[int, int, int] | None:
    """How long a thread was running, waiting for a CPU and neither, from from_ns to to_ns. None when the trace has no state of
    the thread in that stretch. starts are the states' start times, ascending as the states are."""
    running = runnable = sleeping = 0
    found = False
    index = max(bisect.bisect_right(starts, from_ns) - 1, 0)
    while index < len(states) and states[index].start_ns < to_ns:
        state = states[index]
        overlap = min(state.end_ns, to_ns) - max(state.start_ns, from_ns)
        if overlap > 0:
            found = True
            if state.state == "Running":
                running += overlap
            elif state.state.startswith("R"):
                runnable += overlap
            else:
                sleeping += overlap
        index += 1
    return (running, runnable, sleeping) if found else None


def join(
    frames: Sequence[TimedFrame],
    alignment: Alignment,
    snapshots: Sequence[ClockSnapshot],
    period_ns: int,
    vblanks: Sequence[int] = (),
    surface_frames: Sequence[SurfaceFrame] = (),
    display_presents: Mapping[int, int] | None = None,
    thread_states: Sequence[ThreadState] = (),
) -> list[TraceFrame]:
    """A row per frame. vblanks are on the trace's clock, ascending; surface_frames and thread_states ascending by their start;
    display_presents has, for a display frame token, when the system says that frame was presented."""
    presents = display_presents or {}
    surface_starts = [s.start_ns for s in surface_frames]
    state_starts = [s.start_ns for s in thread_states]
    half = period_ns // 2
    rows: list[TraceFrame] = []
    for frame in frames:
        display = to_trace_ns(alignment.display_time(frame.first_seen_ns), snapshots)
        cpu_start = to_trace_ns(alignment.marker_time(frame.cpu_start_ns), snapshots) if frame.cpu_start_ns is not None else None
        cpu_end = cpu_start + frame.cpu_busy_ns if cpu_start is not None and frame.cpu_busy_ns is not None else None

        blank = nearest(vblanks, display)
        if blank is not None and abs(display - blank) > half:
            blank = None

        surface = surface_frame_at(surface_frames, surface_starts, cpu_end) if cpu_end is not None else None
        present = presents.get(surface.display_frame_token) if surface is not None and surface.display_frame_token is not None else None

        times = thread_time(thread_states, state_starts, cpu_start, cpu_end) if cpu_start is not None and cpu_end is not None else None

        rows.append(
            TraceFrame(
                frame_index=frame.frame_index,
                display_trace_ns=display,
                cpu_start_trace_ns=cpu_start,
                cpu_end_trace_ns=cpu_end,
                vblank_trace_ns=blank,
                display_minus_vblank_ns=display - blank if blank is not None else None,
                surface_frame_token=surface.surface_frame_token if surface is not None else None,
                display_frame_token=surface.display_frame_token if surface is not None else None,
                layer_name=surface.layer_name if surface is not None else None,
                present_type=surface.present_type if surface is not None else None,
                jank_type=surface.jank_type if surface is not None else None,
                jank_tag=surface.jank_tag if surface is not None else None,
                on_time_finish=surface.on_time_finish if surface is not None else None,
                gpu_composition=surface.gpu_composition if surface is not None else None,
                system_present_trace_ns=present,
                display_minus_system_present_ns=display - present if present is not None else None,
                cpu_running_ns=times[0] if times is not None else None,
                cpu_runnable_ns=times[1] if times is not None else None,
                cpu_sleeping_ns=times[2] if times is not None else None,
            )
        )
    return rows


def _cell(value: int | str | bool | None) -> str:
    if value is None:
        return ""
    if isinstance(value, bool):
        return "true" if value else "false"
    return str(value)


def to_csv(rows: Sequence[TraceFrame]) -> str:
    """The rows as CSV text with a header line, lines ending in a line feed."""
    text = io.StringIO()
    writer = csv.writer(text, lineterminator="\n")
    writer.writerow(COLUMNS)
    for row in rows:
        writer.writerow([_cell(value) for value in cells(row)])
    return text.getvalue()


def from_csv(text: str) -> list[TraceFrame]:
    """The rows of a CSV that to_csv wrote (or the tools' run-<id>-trace.csv, the same columns): a missing column or a cell
    that is not of its column's kind is a ValueError."""
    reader = csv.reader(io.StringIO(text))
    header = next(reader, None)
    if header is None or tuple(header) != COLUMNS:
        raise ValueError(f"not a per-frame join: its columns are {header}, not {', '.join(COLUMNS)}")
    kinds = [field.type for field in dataclasses.fields(TraceFrame)]
    rows: list[TraceFrame] = []
    for line, cells_of_row in enumerate(reader, start=2):
        if not cells_of_row:
            continue
        if len(cells_of_row) != len(COLUMNS):
            raise ValueError(f"line {line}: {len(cells_of_row)} cells, not {len(COLUMNS)}")
        values: list[int | str | bool | None] = []
        for cell, kind in zip(cells_of_row, kinds, strict=True):
            values.append(_value(cell, str(kind), line))
        rows.append(TraceFrame(*values))  # pyright: ignore[reportArgumentType] (each value is of its field's kind)
    return rows


def _value(cell: str, kind: str, line: int) -> int | str | bool | None:
    if cell == "":
        if "None" not in kind:
            raise ValueError(f"line {line}: a required cell is empty")
        return None
    if "bool" in kind:
        if cell not in ("true", "false"):
            raise ValueError(f"line {line}: '{cell}' is not true or false")
        return cell == "true"
    if "int" in kind:
        if not (cell.isdigit() or (cell.startswith("-") and cell[1:].isdigit())):
            raise ValueError(f"line {line}: '{cell}' is not a whole number")
        return int(cell)
    return cell


def summary_lines(rows: Sequence[TraceFrame]) -> list[str]:
    """What the rows have, in words: which groups the trace filled, and where the system's account and the measured one differ."""
    count = len(rows)
    lines = [f"{count} frames"]
    with_blank = [r for r in rows if r.display_minus_vblank_ns is not None]
    if with_blank:
        furthest = max(abs(r.display_minus_vblank_ns or 0) for r in with_blank)
        lines.append(f"vertical blanks: {len(with_blank)} frames have one within half a refresh, the furthest {furthest} ns from it")
    else:
        lines.append("vertical blanks: none found in the trace for these frames")
    with_system = [r for r in rows if r.surface_frame_token is not None]
    if with_system:
        presented = [r for r in with_system if r.display_minus_system_present_ns is not None]
        lines.append(f"the system's own account: {len(with_system)} frames matched to a frame of the frame timeline, {len(presented)} with a present time")
        if presented:
            differences = sorted(r.display_minus_system_present_ns or 0 for r in presented)
            middle = differences[(len(differences) - 1) // 2]
            lines.append(f"  measured display time minus the system's present time: {differences[0]} to {differences[-1]} ns, median {middle} ns")
        janky = sum(1 for r in with_system if r.jank_type not in (None, "None"))
        lines.append(f"  frames the system calls janky: {janky}")
    else:
        lines.append("the system's own account: no frame timeline found for these frames")
    with_cpu = [r for r in rows if r.cpu_running_ns is not None]
    if with_cpu:
        waited = sum(1 for r in with_cpu if (r.cpu_runnable_ns or 0) > 0)
        lines.append(f"the CPU while a frame was made: {len(with_cpu)} frames, {waited} of them waited for a CPU")
    else:
        lines.append("the CPU while a frame was made: no scheduling found for these frames")
    return lines
