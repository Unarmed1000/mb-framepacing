#!/usr/bin/env python3
# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""Draw the stages of each frame of a run, as a row per frame or a lane per resource, and write the run as a trace file.

The input is a frame log of the first integration's sample (its own CSV, a row per frame with the moments of the frame on the
application's CPU clock), alone or inside a capture session's zip in pacer-captures/:

    python tools/frame_stages_chart.py <zip> --list                                  # the runs of a session
    python tools/frame_stages_chart.py <zip> --run <folder>/<name> -o stages.svg      # twelve frames from frame 120
    python tools/frame_stages_chart.py <csv> --from 300 --frames 16 -o stages.svg     # another stretch
    python tools/frame_stages_chart.py <csv> --view lanes -o lanes.svg                # a lane per resource
    python tools/frame_stages_chart.py <zip> --run <folder>/<name> --trace run.json   # the whole run for a trace viewer

Time runs from left to right, with the display's refreshes as the grid, and both views show the same stages of a frame: the
CPU's part (its update, the wait for its start, a wait for a frame slot, the acquire, its draw, the submit, a hold before the
present, the present call), the GPU's work on it, the time it waited to be shown, and how long it was on screen with its
animation error. The rows view (the default) has a row per frame. The lanes view has a lane per resource and a colour per frame,
with the pacer's expectation, the work it was given and its late frames above, and each frame by its animation time below.
A shape's <title> has its times and details: a viewer that shows an SVG's tooltips gives them under the pointer.

What a log does not have is not drawn, and what it does not know is drawn as not known: a frame reported without a display time
may or may not have been shown. Display times are what the graphics driver reported, not a measurement by the tools.

The trace file is in the Chrome trace event format (ui.perfetto.dev opens it): a track per resource, a span per stage named by
its frame, arrows that join a frame's stages, the refreshes, and the pacer's expected against the actual.
"""

import argparse
import csv
import io
import json
import statistics
import sys
import zipfile
from dataclasses import dataclass
from pathlib import Path

from svg_card import Svg, ms  # pyright: ignore[reportImplicitRelativeImport] (a script next to this one: its folder is on sys.path)

# The last frames of a log have no display time yet: their results had not come back when the run ended
SKIP_LAST = 8

# The rule's defaults (PacerSettings), which the logs do not state: the frame window's length and the share of late frames it
# slows down beyond
FRAME_WINDOW_TICKS = 20_000_000
SLOW_DOWN_LATE_PERCENT = 10

# A moment of a frame is a time on the CPU clock: zero is "not known", as an empty cell
MOMENTS = frozenset(
    {
        "hostCpuStartTicks",
        "hostUpdateEndTicks",
        "frameWaitStartTicks",
        "acquireCallTicks",
        "acquireReturnTicks",
        "frameStartTicks",
        "markerDrawTicks",
        "submitCallTicks",
        "submitReturnTicks",
        "frameSlotWaitBeginTicks",
        "frameSlotWaitEndTicks",
        "frameWaitTargetTicks",
        "presentWaitBeginTicks",
        "presentWaitTargetTicks",
        "presentWaitEndTicks",
        "endFrameTicks",
        "presentCallTicks",
        "presentReturnTicks",
        "gpuWorkBeginTicks",
        "gpuWorkEndTicks",
        "firstPixelOutTicks",
        "feedbackDisplayTicks",
        "displayVSyncTicks",
        "intendedDisplayTicks",
        "nextFrameStartTicks",
    }
)

# The sample's OpenGL ES logs up to 2026-10-06 have what it logs at a frame's start one row early (the row is opened later in
# that loop): these columns of frame N are in the row of frame N - 1. Such a log is known by its pacer frame id, which is two
# ahead of the row's frame index where a correct log's is one ahead
ROW_EARLY = (
    "frameStartTicks",
    "frameWaitStartTicks",
    "pacerOn",
    "swapInterval",
    "preferredSwapInterval",
    "pacerChange",
    "animationStepTicks",
    "pacerWindowFrames",
    "pacerWindowLateFrames",
    "pacerWindowAverageWorkTicks",
    "pacerWindowSpanTicks",
    "pacerWindowFull",
    "pacerFrameId",
    "nextFrameStartTicks",
    "pacerFeedbackOn",
    "pacerFeedbackUsed",
    "pacerFeedbackRefused",
    "pacerFeedbackNotShown",
    "pacerFeedbackMissing",
    "pacerFeedbackLateRefreshes",
    "cpuLoadMs",
    "gpuLoadSteps",
)
ROW_EARLY_NOTE = "This log has the values of a frame's start one row early (an OpenGL ES log of the sample before its fix): they are read from the row before."

WIDTH = 1692
LEFT = 116
RIGHT = 286
TOP = 172
ROW_HEIGHT = 52
# The most rows a chart has: more is no longer a picture (the trace file has the whole run)
MAX_FRAMES = 60
# A stage narrower than this is drawn this wide, so it can be seen
MIN_WIDTH = 1.4
# The present call is what hands the frame over: a thin line through its lane at the moment of the call, an arrow head under
# it, and a line from there to the frame's GPU work. The call's own length is a box of its true width

STYLE = """
  text { pointer-events: none; }
  .row-line { stroke: #ffffff; stroke-opacity: 0.06; stroke-width: 1; }
  .vsync { stroke: #ffffff; stroke-opacity: 0.3; stroke-width: 1; stroke-dasharray: 3 4; }
  .vsync-n { font-size: 11px; fill: #6e7681; letter-spacing: 0.04em; }
  .frame-label { font-size: 12px; font-weight: 600; fill: #c9d1d9; }
  .lane-label { font-size: 10px; fill: #6e7681; }
  .update { fill: #58a6ff; fill-opacity: 0.5; }
  .draw { fill: #58a6ff; }
  .acquire { fill: #a371f7; }
  .present { fill: #d29922; }
  .gpu { fill: #2ea043; }
  .gpu-bound { fill: #2ea043; fill-opacity: 0.3; stroke: #2ea043; stroke-width: 1; stroke-dasharray: 3 3; }
  .wait { fill: url(#hatch); }
  .wait-vsync { fill: url(#bars); }
  .wait-slot { fill: url(#dots); }
  .dot { fill: #8b949e; }
  .timed-call { fill: #db61a2; stroke: #f0f6fc; stroke-width: 1.2; }
  .timed-arrow { fill: #db61a2; stroke: #0d1117; stroke-width: 0.8; }
  .unlogged { fill: #8b949e; fill-opacity: 0.1; stroke: #8b949e; stroke-width: 1; stroke-dasharray: 3 3; }
  .queue { fill: url(#cross); }
  .hatch-line { stroke: #8b949e; stroke-width: 1.4; }
  .screen-a { fill: #6e7681; }
  .screen-b { fill: #adbac7; }
  .screen-long { fill: #e5534b; }
  .marker-tick { stroke: #ffffff; stroke-opacity: 0.75; stroke-width: 1; }
  .present-arrow { fill: #d29922; stroke: #0d1117; stroke-width: 0.8; }
  .present-line { stroke: #d29922; stroke-width: 1.4; fill: none; pointer-events: none; }
  .join-line { stroke: #8b949e; stroke-width: 1; fill: none; pointer-events: none; }
  .expected { fill: #f0f6fc; fill-opacity: 0.9; }
  .timed { fill: #db61a2; }
  .numbers { font-size: 11px; fill: #c9d1d9; }
  .not-shown { font-size: 11px; font-weight: 600; fill: #d29922; }
  .no-time { fill: #d29922; fill-opacity: 0.25; stroke: #d29922; stroke-width: 1.4; }
  .unknown { fill: #8b949e; fill-opacity: 0.1; stroke: #8b949e; stroke-width: 1; stroke-dasharray: 2 3; }
  .unknown-text { font-size: 12px; font-weight: 600; fill: #8b949e; }
  .err { font-size: 10px; font-weight: 600; fill: #0d1117; }
  .err-big { font-size: 10px; font-weight: 700; fill: #ff7b72; }
  .err-back { fill: #0d1117; fill-opacity: 0.85; pointer-events: none; }
  .lane { fill: #ffffff; fill-opacity: 0.035; }
  .lane-name { font-size: 12px; fill: #c9d1d9; }
  .soft { fill-opacity: 0.5; }
  .call { stroke: #f0f6fc; stroke-width: 1.2; }
  .submit { fill: #0d1117; fill-opacity: 0.6; }
  .long { stroke: #e5534b; stroke-width: 2; }
  .on { font-size: 11px; font-weight: 600; fill: #0d1117; }
  .id-in { font-size: 10px; font-weight: 600; fill: #0d1117; }
  .id-by { font-size: 10px; font-weight: 600; fill: #8b949e; }
  .slot { fill: none; stroke: #8b949e; stroke-opacity: 0.55; stroke-width: 1; }
  .over { fill: #e5534b; }
  .late-low { fill: #8b949e; }
  .late-high { fill: #d29922; }
  .late-full { fill: #e5534b; }
  .limit-text { font-size: 11px; fill: #ff7b72; }
  .change { stroke: #f0f6fc; stroke-width: 1.5; }
  .change-text { font-size: 11px; font-weight: 600; fill: #f0f6fc; }
"""
# A colour per frame for the lanes view: its fill, its text, its expected box, its joining line, its waiting pattern
FRAME_COLOURS = ("#58a6ff", "#3fb950", "#d29922", "#a371f7", "#39c5cf", "#db61a2")
LANE_STYLE = "".join(
    "".join(
        (
            f"  .f{hue} {{ fill: {colour}; }}\n",
            f"  .t{hue} {{ font-size: 11px; font-weight: 600; fill: {colour}; }}\n",
            f"  .e{hue} {{ fill: {colour}; fill-opacity: 0.12; stroke: {colour}; stroke-width: 1; }}\n",
            f"  .l{hue} {{ stroke: {colour}; stroke-opacity: 0.6; stroke-width: 1; fill: none; pointer-events: none; }}\n",
            f"  .q{hue} {{ fill: url(#cross{hue}); }}\n",
            f"  .x{hue} {{ stroke: {colour}; stroke-width: 1.4; }}\n",
            f"  .p{hue} {{ stroke: {colour}; stroke-width: 1.6; fill: none; }}\n",
            f"  .u{hue} {{ fill: {colour}; fill-opacity: 0.1; stroke: {colour}; stroke-width: 1; stroke-dasharray: 3 3; }}\n",
            f"  .s{hue} {{ fill: {colour}; fill-opacity: 0.85; }}\n",
        )
    )
    for hue, colour in enumerate(FRAME_COLOURS)
)
HUES = len(FRAME_COLOURS)

DEFS = [
    (
        '<pattern id="hatch" width="5" height="5" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">'
        + '<line class="hatch-line" x1="0" y1="0" x2="0" y2="5"/></pattern>'
    ),
    (
        '<pattern id="cross" width="5" height="5" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">'
        + '<line class="hatch-line" x1="0" y1="0" x2="0" y2="5"/><line class="hatch-line" x1="0" y1="0" x2="5" y2="0"/></pattern>'
    ),
    '<pattern id="bars" width="4" height="4" patternUnits="userSpaceOnUse"><line class="hatch-line" x1="1" y1="0" x2="1" y2="4"/></pattern>',
    '<pattern id="dots" width="4" height="4" patternUnits="userSpaceOnUse"><circle class="dot" cx="2" cy="2" r="1"/></pattern>',
]
# The waiting pattern in each frame colour, for the lanes view
LANE_DEFS = [
    (
        f'<pattern id="cross{hue}" width="5" height="5" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">'
        + f'<line class="x{hue}" x1="0" y1="0" x2="0" y2="5"/><line class="x{hue}" x1="0" y1="0" x2="5" y2="0"/></pattern>'
    )
    for hue in range(HUES)
]


class Arguments(argparse.Namespace):
    source: Path = Path()
    run: str = ""
    first: int = 120
    frames: int = 12
    output: Path | None = None
    trace: Path | None = None
    list_runs: bool = False
    view: str = "rows"


@dataclass(frozen=True)
class Frame:
    """A row of a frame log: its number, and every whole number the row has, by column."""

    index: int
    values: dict[str, int]

    def at(self, name: str) -> int | None:
        return self.values.get(name)

    def span(self, begin: str, end: str) -> tuple[int, int] | None:
        """A stage of the frame, when the log has both of its ends in order."""
        first, last = self.at(begin), self.at(end)
        return (first, last) if first is not None and last is not None and last >= first else None

    @property
    def start(self) -> int:
        return self.values["frameStartTicks"]

    @property
    def shown(self) -> int | None:
        """When the driver reported the frame's first pixel going out."""
        first_pixel = self.at("firstPixelOutTicks")
        return first_pixel if first_pixel is not None else self.at("feedbackDisplayTicks")

    @property
    def swap_interval(self) -> int:
        return self.at("swapInterval") or 1


def read_frames(text: str) -> tuple[list[Frame], list[str]]:
    """The frames of a log that have a start: every cell that is a whole number, a moment's zero left out. And what a reader of
    the chart has to know about how the log was read."""
    reader = csv.reader(io.StringIO(text))
    header = next(reader)
    rows: list[dict[str, int]] = []
    for row in reader:
        if len(row) != len(header):
            continue
        values: dict[str, int] = {}
        for name, cell in zip(header, row, strict=True):
            if cell.lstrip("-").isdigit() and not (name in MOMENTS and int(cell) == 0):
                values[name] = int(cell)
        if "frameIndex" in values:
            rows.append(values)

    notes: list[str] = []
    ahead = [values["pacerFrameId"] - values["frameIndex"] for values in rows if "pacerFrameId" in values]
    if ahead and statistics.median(ahead) == 2:
        for position in range(len(rows) - 1, -1, -1):
            for name in ROW_EARLY:
                _ = rows[position].pop(name, None)
                if position > 0 and name in rows[position - 1]:
                    rows[position][name] = rows[position - 1][name]
        notes.append(ROW_EARLY_NOTE)
    return [Frame(values["frameIndex"], values) for values in rows if "frameStartTicks" in values], notes


def runs_in(archive: zipfile.ZipFile) -> list[str]:
    """The frame logs of a capture session, as <folder>/<name>."""
    names = [name for name in archive.namelist() if name.endswith(".csv") and not name.endswith(".events.csv") and name.count("/") == 2]
    return sorted(name.split("/", 1)[1].removesuffix(".csv") for name in names)


def read_source(source: Path, run: str) -> tuple[str, list[Frame], list[str]]:
    """The run's name, its frames and the notes on how it was read: a CSV as it is, or the run named in a session's zip."""
    if source.suffix.lower() != ".zip":
        return source.stem, *read_frames(source.read_text(encoding="utf-8"))
    with zipfile.ZipFile(source) as archive:
        matches = [name for name in archive.namelist() if name.endswith(f"/{run}.csv")]
        if len(matches) != 1:
            raise SystemExit(f"{source.name} has {len(matches)} runs named '{run}': use --list for the names")
        return run, *read_frames(archive.read(matches[0]).decode("utf-8"))


def refresh_period(frames: list[Frame]) -> int:
    """The display's refresh period as the log has it; without one, the frames' target frame time by their swap interval."""
    periods = [period for frame in frames if (period := frame.at("displayRefreshPeriodTicks"))]
    if periods:
        return int(statistics.median(periods))
    targets = [target // frame.swap_interval for frame in frames if (target := frame.at("targetFrameTimeTicks"))]
    return int(statistics.median(targets)) if targets else 0


def screen_end(frames: list[Frame], position: int, period: int) -> int | None:
    """When the frame left the screen: the next shown frame's display time, or its swap interval's time for the last one."""
    shown = frames[position].shown
    if shown is None:
        return None
    following = next((later.shown for later in frames[position + 1 :] if later.shown is not None), None)
    return following if following is not None else shown + (frames[position].swap_interval * period)


def unlogged_hold(frame: Frame) -> tuple[int, int] | None:
    """The stretch of a frame's CPU side the log has no times for: from the last thing it did before its frame start (the
    acquire, else the update) to the wait before its frame start, else the frame start. The frame is held there, and the log
    does not say by what."""
    last = max((value for key in ("acquireReturnTicks", "hostUpdateEndTicks") if (value := frame.at(key)) is not None), default=None)
    wait = frame.at("frameWaitStartTicks")
    until = wait if wait is not None else frame.start
    return (last, until) if last is not None and last < min(until, frame.start) else None


def start_wait(frame: Frame) -> tuple[int, int] | None:
    """The wait that holds a frame's start: from where it began to the wait for a frame slot where the log has one (the loop
    goes on with the slot and the acquire before its frame start), else to the frame start."""
    begin, slot = frame.at("frameWaitStartTicks"), frame.at("frameSlotWaitBeginTicks")
    end = slot if slot is not None else frame.start
    return (begin, end) if begin is not None and end >= begin else None


def present_hold(frame: Frame) -> tuple[int, int] | None:
    """The hold of the finished frame before its present: the wait the log has, else from its work done to its present call."""
    return frame.span("presentWaitBeginTicks", "presentWaitEndTicks") or frame.span("endFrameTicks", "presentCallTicks")


def cpu_span(frame: Frame) -> tuple[int, int] | None:
    """From the first thing the CPU did for the frame to the last: its whole time on the CPU thread, waits included."""
    keys = ("hostCpuStartTicks", "frameWaitStartTicks", "frameSlotWaitBeginTicks", "acquireCallTicks", "frameStartTicks", "endFrameTicks", "presentReturnTicks")
    moments = [value for key in keys if (value := frame.at(key)) is not None]
    return (min(moments), max(moments)) if len(moments) >= 2 else None


def held_longer(frame: Frame, shown: int, end: int, period: int) -> bool:
    """On screen at least half a refresh longer than its swap interval asked for."""
    return period > 0 and (end - shown) * 2 >= ((2 * frame.swap_interval) + 1) * period


# From this size on an animation error stands out in the chart: the tools' error threshold (analyze --error-threshold-ms)
ERROR_THRESHOLD_TICKS = 10_000


@dataclass(frozen=True)
class OnScreen:
    """A frame's time on screen as far as the log says: from its display time to `end`. Where frames without a display time were
    presented before the next frame that has one, `end` is the end of the frame's own swap interval, and from there to
    `unknown_end` the log does not say which of them the display showed."""

    shown: int
    end: int
    unknown_end: int | None
    unreported: tuple[Frame, ...]


def on_screen(frames: list[Frame], position: int, period: int) -> OnScreen | None:
    frame = frames[position]
    shown = frame.shown
    if shown is None:
        return None
    own = shown + (frame.swap_interval * period)
    unreported: list[Frame] = []
    for later in frames[position + 1 :]:
        following = later.shown
        if following is None:
            unreported.append(later)
        elif unreported and following > own:
            return OnScreen(shown, own, following, tuple(unreported))
        else:
            return OnScreen(shown, following, None, ())
    return OnScreen(shown, own, None, ())


def no_display_time(frame: Frame, last_reported: int) -> str:
    """Why a frame has no display time, as far as the log says. It never says the frame was not shown: only a capture does."""
    if (read_at := frame.at("resultReadAtFrame")) is not None:
        return f"The graphics driver reported this present (read at frame {read_at}) without a display time."
    if frame.at("presentTimingRequested") == 0:
        return "No display time was asked for with this present."
    if frame.index > last_reported:
        return "Its result had not come back when the run ended."
    return "The log has no result for this present."


def animation_error(frames: list[Frame], position: int) -> int | None:
    """The animation error of the step into a frame, as the tools define it: its animation time step minus its display time step,
    both from the frame presented before it. Known when both frames have a display time: here the driver's, not a capture's."""
    if position == 0:
        return None
    frame, before = frames[position], frames[position - 1]
    shown, shown_before = frame.shown, before.shown
    animation, animation_before = frame.at("animationTimeTicks"), before.at("animationTimeTicks")
    if shown is None or shown_before is None or animation is None or animation_before is None:
        return None
    return (animation - animation_before) - (shown - shown_before)


def animation_tie(rows: list[Frame]) -> tuple[Frame, int] | None:
    """What to add to an animation time to place it on the CPU clock: the two are tied at the first of these frames that has a
    display time (its animation time is put at its display time). The frame, and the offset. A chart ties at the frame before
    its first row where it can: tied at its first row, that frame would sit exactly on its display whatever its animation error."""
    for frame in rows:
        shown, animation = frame.shown, frame.at("animationTimeTicks")
        if shown is not None and animation is not None:
            return frame, shown - animation
    return None


def signed_ms(ticks: int, digits: int = 2) -> str:
    text = ms(abs(ticks), digits)
    return text if float(text) == 0 else ("+" if ticks > 0 else "-") + text


def apart(ticks: int) -> str:
    """How far one moment is from another, to go before the other's name: "0.190 ms before", "exactly at"."""
    text = ms(abs(ticks))
    return "exactly at" if float(text) == 0 else f"{text} ms {'after' if ticks > 0 else 'before'}"


# How the sample held a frame for its time (its holdMethod column): the pattern of the wait, and what the tooltip calls it. With a
# scheduled present the presentation engine holds the frame, and what the loop itself waits with is not in the log
HOLDS = {0: ("wait", "A sleep on a timer."), 1: ("wait-vsync", "A wait for the vertical blank, as the window system reports it.")}
SLOT_WAIT_TIP = "A wait for the GPU: for the frame that used the slot before, and its present."


def hold_style(frame: Frame) -> str:
    method = frame.at("holdMethod")
    return HOLDS[method][0] if method in HOLDS else "wait"


def is_timed(frame: Frame) -> bool:
    """The present was given a target time: the presentation engine holds the frame until then."""
    return bool(frame.at("presentTargetRelativeNs"))


def wait_tip(zero: int, title: str, wait: tuple[int, int], target: int | None, frame: Frame | None = None) -> str:
    aimed = f"It aimed at {ms(target - zero)} ms and woke {apart(wait[1] - target)} that." if target is not None else ""
    method = frame.at("holdMethod") if frame is not None else None
    return tip(zero, title, *wait, HOLDS[method][1] if method in HOLDS else "", aimed)


def tip(zero: int, title: str, first: int, last: int | None = None, *more: str) -> str:
    """A shape's tooltip: what it is, how long it took and when (milliseconds from the chart's first frame start, as the axis),
    and whatever else is known about it, a line each."""
    when = f"at {ms(first - zero)} ms" if last is None else f"{ms(last - first)} ms: from {ms(first - zero)} to {ms(last - zero)} ms"
    return "\n".join((title, when, *(line for line in more if line)))


def display_tip(zero: int, frame: Frame, screen: OnScreen, period: int, error: int | None, has_before: bool) -> str:
    refreshes = f"{(screen.end - screen.shown) / period:.2f} refreshes, " if period > 0 else ""
    if error is not None:
        judged = f"Animation error {signed_ms(error, 3)} ms: its animation time step minus its display time step, from the frame before."
    else:
        judged = "Animation error not known: the frame before has no display time." if has_before else ""
    return tip(
        zero,
        f"#{frame.index} on screen",
        screen.shown,
        screen.end,
        f"{refreshes}asked for a swap interval of {frame.swap_interval}.",
        f"Shown {ms(screen.shown - frame.start)} ms after its frame start.",
        judged,
        "Then a stretch the log has no display time for." if screen.unknown_end is not None else "",
        "The display time is what the graphics driver reported, not a capture.",
    )


def unknown_tip(zero: int, frame: Frame, screen: OnScreen, last_reported: int) -> str:
    names = ", ".join(f"#{later.index}" for later in screen.unreported)
    return tip(
        zero,
        f"Not known: #{frame.index} still, or {names}",
        screen.end,
        screen.unknown_end,
        *(f"#{later.index}: {no_display_time(later, last_reported)}" for later in screen.unreported),
        "What the display showed here takes a capture to know.",
    )


UNLOGGED_TIP = "From the last thing the log has of the frame (its acquire, else its update) to the wait before its start: it is held there, and the log does not say by what."
GPU_TIP = "Begin and end as the application measured them on its CPU clock."
GPU_BOUND_TIP = "A duration only, an earlier frame's, placed at this frame's present."
WAITING_TIP = "From the end of its GPU work (or its present's return, if that is later) to its display time."
ANIMATION_TIP = "The animation clock is tied to the display at #{frame}'s display time."
HOVER_NOTE = "A stage the log has no times for is not drawn. In a viewer that shows an SVG's tooltips, a shape under the pointer gives its times and details."
STAGES_NOTE = "A dashed GPU box is the GPU time the application had at that frame (an earlier frame's), placed at the present."
LANES_NOTE = (
    "The thin lines follow a frame: from the CPU thread (its submit, where the log has one) to its GPU work, from its present call down to its "
    + "row of the wait to be shown, from the end of its GPU work to the start of that wait, and from there to the screen."
)
DISPLAY_NOTE = (
    "A frame reported without a display time may or may not have been shown: the time it would have been on screen is drawn as not known, "
    + "and only a capture settles it."
)
ERROR_NOTE = (
    "The animation error of a shown frame is its animation time step minus its display time step from the frame before, "
    + "by the reported display times; it is not known after a frame without one."
)
PACER_NOTE = (
    "The pacer's lanes: the work it was given for a frame (CPU plus the newest GPU time the application had) as a length in the frame time it had, "
    + "and the late frames in its frame window as a share of the count the rule slows down at (its default settings)."
)
ANIMATION_NOTE = (
    "The lane by animation time has each frame from its animation time to the next frame's: when the animation expected it on screen. "
    + "The animation clock is tied to the display at #{frame}'s display time."
)
ANIMATION_ERROR_NOTE = "In that lane a frame's animation error is how far its box moved against its place on the display, compared with the frame before."


def gpu_tip(zero: int, frame: Frame, work: tuple[int, int]) -> str:
    measured = frame.at("gpuTimeTicks")
    return tip(zero, f"#{frame.index} GPU work", *work, GPU_TIP, f"GPU time of the frame as measured: {ms(measured)} ms." if measured is not None else "")


def acquire_tip(zero: int, frame: Frame, acquire: tuple[int, int]) -> str:
    image = frame.at("imageIndex")
    return tip(zero, f"#{frame.index} acquire (call to return)", *acquire, f"Swap chain image {image}." if image is not None else "")


def draw_tip(zero: int, frame: Frame, draw: tuple[int, int]) -> str:
    drawn, animation, step = frame.at("markerDrawTicks"), frame.at("animationTimeTicks"), frame.at("animationStepTicks")
    return tip(
        zero,
        f"#{frame.index} draw: frame start to work done",
        *draw,
        f"Marker recorded at {ms(drawn - zero)} ms." if drawn is not None else "",
        f"Animation time {ms(animation)} ms" + (f", a step of {ms(step)} ms." if step is not None else ".") if animation is not None else "",
        f"Swap interval {frame.swap_interval}.",
    )


def present_tip(zero: int, frame: Frame, present: tuple[int, int]) -> str:
    present_id, relative = frame.at("presentId"), frame.at("presentTargetRelativeNs")
    return tip(
        zero,
        f"#{frame.index} present call (call to return)",
        *present,
        f"Present id {present_id}." if present_id is not None else "",
        f"A timed present: {ms(relative // 100)} ms after the display time of the frame before." if relative else "",
    )


# ----------------------------------------------------------------------------------------------------------------------------------------
# The chart
# ----------------------------------------------------------------------------------------------------------------------------------------


def axis_step(span_ticks: int) -> int:
    """A step in ticks for the millisecond axis that gives about a dozen labels: 1, 2 or 5 times a power of ten."""
    step = 1_000
    for factor in (1, 2, 5, 10, 20, 50, 100, 200, 500, 1_000, 2_000, 5_000):
        step = 1_000 * factor
        if span_ticks // step <= 14:
            break
    return step


@dataclass(frozen=True)
class Stretch:
    """The frames a chart shows, and where a time is on its x axis."""

    frames: list[Frame]
    position: int
    rows: list[Frame]
    period: int
    time_first: int
    time_last: int
    left: float
    plot_width: float

    @property
    def scale(self) -> float:
        return self.plot_width / (self.time_last - self.time_first)

    def x(self, ticks: int) -> float:
        return self.left + ((ticks - self.time_first) * self.scale)

    def inside(self, ticks: int) -> bool:
        return self.time_first <= ticks <= self.time_last

    def before(self, offset: int) -> Frame | None:
        """The frame before the row at offset, also when it is before the chart's first row."""
        return self.frames[self.position + offset - 1] if self.position + offset > 0 else None


def stretch_of(frames: list[Frame], first: int, count: int, left: float, plot_width: float) -> Stretch:
    position = next((i for i, frame in enumerate(frames) if frame.index >= first), None)
    if position is None:
        raise SystemExit(f"The log has no frame {first} or later (its last is {frames[-1].index})")
    rows = frames[position : position + count]
    period = refresh_period(rows)
    begins = [min(value for key in ("hostCpuStartTicks", "frameWaitStartTicks", "frameStartTicks") if (value := frame.at(key)) is not None) for frame in rows]
    ends = [frame.start + period for frame in rows]
    for offset, frame in enumerate(rows):
        for key in ("presentReturnTicks", "gpuWorkEndTicks"):
            if (value := frame.at(key)) is not None:
                ends.append(value)
        if (end := screen_end(frames, position + offset, period)) is not None:
            ends.append(end)
    time_first, time_last = min(begins), max(ends)
    margin = max((time_last - time_first) // 80, 1)
    return Stretch(frames, position, rows, period, time_first - margin, time_last + margin, left, plot_width)


def subtitle_of(stretch: Stretch, view: str) -> str:
    rows, period = stretch.rows, stretch.period
    hz = f"{10_000_000 / period:.2f} Hz ({ms(period, 2)} ms a refresh)" if period else "an unknown refresh rate"
    intervals = sorted({frame.swap_interval for frame in rows})
    return (
        f"Frames {rows[0].index} to {rows[-1].index} on a display at {hz}, swap interval {' and '.join(str(i) for i in intervals)}. "
        f"{view}; times on the application's CPU clock."
    )


def draw_grid(svg: Svg, stretch: Stretch, top: float, bottom: float) -> str:
    """The refresh grid: the vertical blanks the sample read on the CPU clock, else the reported display times. Says which."""
    rows, period = stretch.rows, stretch.period
    anchor = next((value for frame in rows if (value := frame.at("displayVSyncTicks")) is not None), None)
    grid = "the vertical blank times the application read on its CPU clock"
    if anchor is None:
        anchor = next((frame.shown for frame in rows if frame.shown is not None), None)
        grid = "the display times the driver reported"
    if anchor is not None and period > 0:
        number = -((anchor - stretch.time_first) // period)
        label_every = max(1, round(26 / (period * stretch.scale)))
        while anchor + (number * period) <= stretch.time_last:
            at = stretch.x(anchor + (number * period))
            svg.line(at, top, at, bottom, "vsync")
            if number % label_every == 0:
                svg.text(at, top - 6, str(number), "vsync-n", "middle")
            number += 1
        svg.text(stretch.left - 8, top - 6, "refresh", "vsync-n", "end")
    return grid


def draw_axis(svg: Svg, stretch: Stretch, bottom: float) -> None:
    """Milliseconds on the CPU clock, from the first frame's start."""
    zero = stretch.rows[0].start
    step = axis_step(stretch.time_last - stretch.time_first)
    tick = -(((zero - stretch.time_first) // step) * step)
    while zero + tick <= stretch.time_last:
        at = stretch.x(zero + tick)
        svg.line(at, bottom, at, bottom + 5, "zero-line")
        svg.text(at, bottom + 20, ms(tick, 0), "axis", "middle")
        tick += step
    svg.text(stretch.left - 8, bottom + 20, "ms", "axis", "end")


def stages_chart(name: str, frames: list[Frame], first: int, count: int, notes: list[str]) -> str:
    stretch = stretch_of(frames, first, count, LEFT, WIDTH - LEFT - RIGHT)
    position, rows, period = stretch.position, stretch.rows, stretch.period
    time_first, time_last, scale, x = stretch.time_first, stretch.time_last, stretch.scale, stretch.x
    last_reported = frames[-1].index - SKIP_LAST
    zero = rows[0].start

    after = [HOVER_NOTE, STAGES_NOTE, DISPLAY_NOTE, ERROR_NOTE, *notes]
    height = TOP + (len(rows) * ROW_HEIGHT) + 76 + (20 * len(after))
    svg = Svg(WIDTH, height, f"Frame stages: {name}", subtitle_of(stretch, "A row per frame"), STYLE)
    svg.defs.extend(DEFS)
    svg.key(
        28,
        92,
        [
            ("update", "update"),
            ("wait", "sleep, or a wait the log does not name"),
            ("wait-vsync", "vertical blank wait"),
            ("wait-slot", "frame slot wait"),
            ("unlogged", "held, no times logged"),
            ("acquire", "acquire"),
            ("draw", "draw (to work done)"),
            ("submit", "submit"),
        ],
    )
    svg.key(
        28,
        113,
        [
            ("present", "present call (with an arrow)"),
            ("gpu", "GPU work"),
            ("queue", "waiting to be shown"),
            ("screen-b", "on screen"),
            ("screen-long", "on screen longer than asked"),
            ("unknown", "not known"),
        ],
    )
    svg.polygon([(28, 125), (38, 125), (33, 133)], "expected")
    svg.text(46, 134, "where the pacer said to start the frame (CPU) and to show it (display)", "axis")
    svg.polygon([(520, 125), (530, 125), (525, 133)], "timed-arrow")
    svg.text(538, 134, "a timed present, and (display lane) the time it asked for", "axis")
    svg.line(938, 123, 938, 135, "marker-tick")
    svg.text(946, 134, "marker recorded (CPU)", "axis")

    top, bottom = TOP - 8, TOP + (len(rows) * ROW_HEIGHT)
    grid = draw_grid(svg, stretch, top, bottom)
    draw_axis(svg, stretch, bottom)

    def identify(tag: str, first_tick: int, last_tick: int, y: float) -> None:
        """The frame's number on one of its boxes: inside it where it fits, else right after it."""
        width = max(x(last_tick) - x(first_tick), MIN_WIDTH)
        if width >= (6.2 * len(tag)) + 6:
            svg.text(x(first_tick) + (width / 2), y, tag, "id-in", "middle")
        else:
            svg.text(x(first_tick) + width + 3, y, tag, "id-by")

    def box(first_tick: int, last_tick: int, y: float, box_height: float, style: str, about: str = "") -> None:
        svg.rect(x(first_tick), y, max(x(last_tick) - x(first_tick), MIN_WIDTH), box_height, style, tip=about)

    def mark(ticks: int, y: float, style: str, about: str) -> None:
        at = x(ticks)
        svg.polygon([(at - 5, y - 8), (at + 5, y - 8), (at, y)], style, about)

    for offset, frame in enumerate(rows):
        y = TOP + (offset * ROW_HEIGHT)
        cpu, gpu, screen_y = y + 9, y + 22, y + 37
        tag = f"#{frame.index}"
        svg.line(LEFT, y + ROW_HEIGHT, WIDTH - RIGHT, y + ROW_HEIGHT, "row-line")
        svg.text(28, y + 30, tag, "frame-label")
        if offset == 0:
            for lane, label in ((cpu, "CPU"), (gpu, "GPU"), (screen_y, "display")):
                svg.text(LEFT - 8, lane + 9, label, "lane-label", "end")

        # The CPU's part: update, the wait for the frame start, acquire, draw, a hold before the present, the present call
        if (update := frame.span("hostCpuStartTicks", "hostUpdateEndTicks")) is not None:
            box(update[0], update[1], cpu, 9, "update", tip(zero, f"{tag} update", *update))
        if (wait := start_wait(frame)) is not None and (wait[1] - wait[0]) * scale >= MIN_WIDTH:
            box(wait[0], wait[1], cpu, 9, hold_style(frame), wait_tip(zero, f"{tag} wait for its frame start", wait, frame.at("frameWaitTargetTicks"), frame))
        if (slot := frame.span("frameSlotWaitBeginTicks", "frameSlotWaitEndTicks")) is not None and (slot[1] - slot[0]) * scale >= MIN_WIDTH:
            box(slot[0], slot[1], cpu, 9, "wait-slot", tip(zero, f"{tag} wait for a frame slot", *slot, SLOT_WAIT_TIP))
        if (acquire := frame.span("acquireCallTicks", "acquireReturnTicks")) is not None:
            box(acquire[0], acquire[1], cpu, 9, "acquire", acquire_tip(zero, frame, acquire))
        if (held := unlogged_hold(frame)) is not None and (held[1] - held[0]) * scale >= 3:
            box(held[0], held[1], cpu + 1.5, 6, "unlogged", tip(zero, f"{tag} held, no times logged", *held, UNLOGGED_TIP))
        if (draw := frame.span("frameStartTicks", "endFrameTicks")) is not None:
            box(draw[0], draw[1], cpu, 9, "draw", draw_tip(zero, frame, draw))
        if (submit := frame.span("submitCallTicks", "submitReturnTicks")) is not None:
            box(submit[0], submit[1], cpu, 9, "submit", tip(zero, f"{tag} submit of its GPU work (call to return)", *submit))
        if (hold := present_hold(frame)) is not None and (hold[1] - hold[0]) * scale >= MIN_WIDTH:
            box(hold[0], hold[1], cpu, 9, hold_style(frame), wait_tip(zero, f"{tag} hold before its present", hold, frame.at("presentWaitTargetTicks"), frame))
        if (present := frame.span("presentCallTicks", "presentReturnTicks")) is not None:
            at = x(present[0])
            box(present[0], present[1], cpu, 9, "present", present_tip(zero, frame, present))
            svg.line(at, cpu - 5, at, cpu + 11, "present-line")
            arrow = "timed-arrow" if is_timed(frame) else "present-arrow"
            svg.polygon([(at - 4.5, cpu + 10), (at + 4.5, cpu + 10), (at, cpu + 16)], arrow, present_tip(zero, frame, present))
        if (began := frame.at("gpuWorkBeginTicks")) is not None:
            svg.polyline([(x(submit[0] if submit is not None else began), cpu + 9), (x(began), gpu)], "join-line")
        if (drawn := frame.at("markerDrawTicks")) is not None:
            svg.line(x(drawn), cpu, x(drawn), cpu + 9, "marker-tick", tip(zero, f"{tag} marker recorded", drawn))

        # Where the pacer said to start this frame: the time the frame before was given for its next frame's start
        before = stretch.before(offset)
        if before is not None and (expected := before.at("nextFrameStartTicks")) is not None and time_first <= expected <= time_last:
            started = f"The frame started {apart(frame.start - expected)} it."
            mark(expected, cpu, "expected", tip(zero, f"{tag}: the frame start the pacer gave", expected, None, started))

        # The GPU's work, as measured on the CPU clock; without a start and an end, the measured duration from the present on, as a bound
        screen = on_screen(frames, position + offset, period)
        work = frame.span("gpuWorkBeginTicks", "gpuWorkEndTicks")
        ready = frame.at("presentReturnTicks")
        if work is not None:
            box(work[0], work[1], gpu, 12, "gpu", gpu_tip(zero, frame, work))
            identify(tag, work[0], work[1], gpu + 9.5)
            ready = max(work[1], ready or work[1])
        elif (duration := frame.at("workGpuTicks")) and (called := frame.at("presentCallTicks")) is not None:
            box(
                called,
                called + duration,
                gpu + 0.5,
                11,
                "gpu-bound",
                tip(zero, f"{tag}: the GPU time the application had", called, called + duration, GPU_BOUND_TIP),
            )
        if ready is not None and screen is not None and (screen.shown - ready) * scale >= MIN_WIDTH:
            box(ready, screen.shown, gpu + 2, 8, "queue", tip(zero, f"{tag} waiting to be shown", ready, screen.shown, WAITING_TIP))

        # On screen, from the display time the driver reported to the next frame's
        numbers = [f"CPU {ms(draw[1] - draw[0], 2)}"] if draw is not None else []
        if work is not None:
            numbers.append(f"GPU {ms(work[1] - work[0], 2)}")
        svg.text(WIDTH - RIGHT + 14, y + 24, ", ".join(numbers), "numbers")
        error = animation_error(frames, position + offset)
        if screen is not None:
            style = "screen-long" if held_longer(frame, screen.shown, screen.end, period) else ("screen-a" if offset % 2 == 0 else "screen-b")
            box(screen.shown, min(screen.end, time_last), screen_y, 12, style, display_tip(zero, frame, screen, period, error, position + offset > 0))
            identify(tag, screen.shown, min(screen.end, time_last), screen_y + 9.5)
            if screen.unknown_end is not None and screen.end < time_last:
                box(screen.end, min(screen.unknown_end, time_last), screen_y + 0.5, 11, "unknown", unknown_tip(zero, frame, screen, last_reported))
            judged = f", animation error {signed_ms(error)} ms" if error is not None else ""
            svg.text(WIDTH - RIGHT + 14, y + 38, f"shown after {ms(screen.shown - frame.start, 2)} ms{judged}", "numbers")
        elif frame.at("presentTimingRequested") == 1:
            svg.text(WIDTH - RIGHT + 14, y + 38, "no display time reported" if frame.index <= last_reported else "not reported yet", "not-shown")
        if (intended := frame.at("intendedDisplayTicks")) is not None and time_first <= intended <= time_last:
            landed = f"Shown {apart(screen.shown - intended)} it." if screen is not None else ""
            mark(intended, screen_y, "expected", tip(zero, f"{tag}: the pacer's intended display time", intended, None, landed))
        relative = frame.at("presentTargetRelativeNs")
        if relative and before is not None and before.shown is not None:
            asked = before.shown + (relative // 100)
            mark(
                asked,
                screen_y,
                "timed",
                tip(zero, f"{tag}: the time its timed present asked for", asked, None, f"{ms(relative // 100)} ms after #{before.index}'s display time."),
            )

    svg.text(28, bottom + 50, f"The refresh grid is {grid}. Display times are what the graphics driver reported, not a measurement by the tools.", "sub")
    for line, note in enumerate(after):
        svg.text(28, bottom + 70 + (20 * line), note, "sub")
    return svg.render()


LANES_LEFT = 168
LANES_TOP = 190
SCREEN_HEIGHT = 34
ANIMATION_HEIGHT = 18
# Frames wait to be shown at the same time: the waiting lane has a few rows, and a wait takes the first that is free
QUEUE_ROWS = 3
QUEUE_ROW_HEIGHT = 11


def median_ms(values: list[int]) -> str:
    return ms(int(statistics.median(values)), 2) if values else "-"


def lanes_chart(name: str, frames: list[Frame], first: int, count: int, notes: list[str]) -> str:
    """A lane per resource (the pacer's expectation, the CPU thread, the GPU, the wait to be shown, the display, the animation
    clock) and a colour per frame, the same in every lane, so a frame is followed from its start to the screen."""
    stretch = stretch_of(frames, first, count, LANES_LEFT, WIDTH - LANES_LEFT - 28)
    rows, period, scale, x = stretch.rows, stretch.period, stretch.scale, stretch.x
    last_reported = frames[-1].index - SKIP_LAST
    zero = rows[0].start

    # The pacer's lanes are there when the log has a pacer that was on, the animation lane when a frame of the chart was shown
    paced = any(frame.at("pacerOn") == 1 for frame in rows)
    row_before = stretch.before(0)
    tied = animation_tie(([row_before] if row_before is not None else []) + rows)
    expected_y = LANES_TOP
    work_y, late_y = expected_y + 18, expected_y + 40
    cpu_y = (late_y + 58) if paced else (expected_y + 34)
    gpu_y, queue_y = cpu_y + 48, cpu_y + 82
    screen_y = queue_y + (QUEUE_ROWS * QUEUE_ROW_HEIGHT) + 10
    animation_y = screen_y + SCREEN_HEIGHT + 8
    bottom = (animation_y + ANIMATION_HEIGHT + 8) if tied is not None else animation_y
    after = [HOVER_NOTE, LANES_NOTE, DISPLAY_NOTE, ERROR_NOTE]
    after += [PACER_NOTE] if paced else []
    after += [ANIMATION_NOTE.format(frame=tied[0].index), ANIMATION_ERROR_NOTE] if tied is not None else []
    after += notes
    height = bottom + 102 + (20 * len(after))
    svg = Svg(WIDTH, height, f"Frame stages: {name}", subtitle_of(stretch, "A lane per resource, a colour per frame"), STYLE + LANE_STYLE)
    svg.defs.extend(DEFS + LANE_DEFS)

    # The key, left to right in as many lines as it takes: each entry draws its sample at the place it is given
    place, key_y = 28.0, 92.0

    def entry(text: str) -> tuple[float, float]:
        """Writes an entry's text and moves on; returns where its sample goes (its left, and the text's baseline)."""
        nonlocal place, key_y
        needed = 18 + (7.2 * len(text)) + 20
        if place + needed > WIDTH - 20:
            place, key_y = 28.0, key_y + 22
        at = place
        svg.text(at + 18, key_y, text, "axis")
        place += needed
        return at, key_y

    for style, text in (
        ("f0", "one colour per frame"),
        ("f0 soft", "update, acquire"),
        ("wait", "a sleep, or a wait the log does not name"),
        ("wait-vsync", "a vertical blank wait"),
        ("wait-slot", "a frame slot wait"),
        ("u0", "held, not logged"),
        ("submit", "submit"),
    ):
        at, line = entry(text)
        svg.rect(at, line - 10, 12, 12, style)
    at, line = entry("the present call")
    svg.line(at + 5, line - 14, at + 5, line + 1, "p0")
    svg.polygon([(at, line), (at + 10, line), (at + 5, line + 6)], "f0 call")
    at, line = entry("a timed present")
    svg.polygon([(at, line - 6), (at + 10, line - 6), (at + 5, line)], "timed-call")
    for style, text in (
        ("q0", "waiting to be shown"),
        ("f0 long", "on screen longer than asked"),
        ("unknown", "not known who was on screen"),
        ("no-time", "no display time reported"),
    ):
        at, line = entry(text)
        svg.rect(at, line - 10, 12, 12, style)
    at, line = entry("animation error of 1 ms or more")
    svg.rect(at - 2, line - 11, 16, 13, "err-back", radius=3)
    svg.text(at + 6, line - 1, "±", "err-big", "middle")
    at, line = entry("the time a timed present asked for")
    svg.polygon([(at + 1, line - 8), (at + 11, line - 8), (at + 6, line)], "timed")
    at, line = entry("marker recorded (CPU)")
    svg.line(at + 6, line - 10, at + 6, line + 2, "marker-tick")

    lanes = (
        (expected_y, 12, "Pacer: expected"),
        *(((work_y, 16, "Pacer: work it was given"), (late_y, 16, "Pacer: late frames in window")) if paced else ()),
        (cpu_y, 26, "CPU, main thread"),
        (gpu_y, 26, "GPU"),
        (queue_y, QUEUE_ROWS * QUEUE_ROW_HEIGHT, "Waiting to be shown"),
        (screen_y, SCREEN_HEIGHT, "Display"),
        *(((animation_y, ANIMATION_HEIGHT, "By its animation time"),) if tied is not None else ()),
    )
    for y, lane_height, lane_name in lanes:
        svg.rect(LANES_LEFT, y, stretch.plot_width, lane_height, "lane")
        svg.text(LANES_LEFT - 12, y + (lane_height / 2) + 4, lane_name, "lane-name", "end")
    grid = draw_grid(svg, stretch, LANES_TOP - 8, bottom)
    draw_axis(svg, stretch, bottom)

    def box(first_tick: int, last_tick: int, y: float, box_height: float, style: str, about: str = "") -> float:
        """Draws the box and returns its width."""
        width = max(x(last_tick) - x(first_tick), MIN_WIDTH)
        svg.rect(x(first_tick), y, width, box_height, style, tip=about)
        return width

    def label(text: str, first_tick: int, width: float, y: float, style: str = "on") -> None:
        if width >= (7 * len(text)) + 6:
            svg.text(x(first_tick) + (width / 2), y, text, style, "middle")

    def error_label(error: int, first_tick: int, width: float, y: float) -> None:
        """The animation error in a shown frame's box, with its unit where that fits; one that stands out on a dark plate."""
        text = next((text for text in (f"{signed_ms(error)} ms", signed_ms(error)) if width >= (6 * len(text)) + 8), None)
        if text is None:
            return
        middle = x(first_tick) + (width / 2)
        if abs(error) >= ERROR_THRESHOLD_TICKS:
            plate = (6 * len(text)) + 8
            svg.rect(middle - (plate / 2), y - 10, plate, 13, "err-back", radius=3)
        svg.text(middle, y, text, "err-big" if abs(error) >= ERROR_THRESHOLD_TICKS else "err", "middle")

    free_from = [stretch.time_first] * QUEUE_ROWS

    def queue_row(first_tick: int, last_tick: int) -> float:
        """The y of the waiting lane's first row that is free at first_tick (else the one free soonest), taken until last_tick."""
        row = next((r for r in range(QUEUE_ROWS) if free_from[r] <= first_tick), min(range(QUEUE_ROWS), key=lambda r: free_from[r]))
        free_from[row] = last_tick
        return queue_y + (row * QUEUE_ROW_HEIGHT)

    spacing = (rows[-1].start - rows[0].start) * scale / max(len(rows) - 1, 1)
    name_every = max(1, round(34 / spacing)) if spacing > 0 else 1
    cpu_work: list[int] = []
    gpu_work: list[int] = []
    to_shown: list[int] = []
    errors: list[int] = []
    unreported = longer = 0
    for offset, frame in enumerate(rows):
        position = stretch.position + offset
        hue = frame.index % HUES
        tag = f"#{frame.index}"

        # What the pacer expected: from the start it gave the frame to the display time it aimed for
        before = stretch.before(offset)
        expected = before.at("nextFrameStartTicks") if before is not None else None
        intended = frame.at("intendedDisplayTicks")
        if expected is not None and intended is not None and intended > expected and stretch.inside(expected):
            started = f"The frame started {apart(frame.start - expected)} the start the pacer gave it."
            about = tip(zero, f"{tag} as the pacer expected it: from its frame start to its display time", expected, intended, started)
            _ = box(expected, min(intended, stretch.time_last), expected_y, 12, f"e{hue}", about)

        # What the pacer was told the frame needed, as a length of time from the frame's start, in the frame time it had (the box)
        frame_time = frame.at("targetFrameTimeTicks")
        work_cpu = frame.at("workCpuTicks")
        if paced and frame_time and work_cpu is not None:
            work_gpu = frame.at("workGpuTicks") or 0
            given = work_cpu + work_gpu
            about = tip(
                zero,
                f"{tag}: the work the pacer was given, {(100 * given) // frame_time} % of its frame time",
                frame.start,
                frame.start + given,
                f"CPU {ms(work_cpu)} ms + GPU {ms(work_gpu)} ms (the newest GPU time the application had: "
                + (f"frame #{measured_on}'s)." if (measured_on := frame.at("workGpuFrameIndex")) is not None else "an earlier frame's)."),
                f"Frame time {ms(frame_time)} ms (the outline).",
            )
            _ = box(frame.start, min(frame.start + frame_time, stretch.time_last), work_y, 16, "slot")
            width = box(frame.start, min(frame.start + given, stretch.time_last), work_y + 3, 10, "over" if given > frame_time else f"f{hue}", about)
            label(f"{(100 * given) // frame_time} %", frame.start, width, work_y + 12)

        # How many late frames its frame window held when this frame began, against the count that makes the rule slow down
        late = frame.at("pacerWindowLateFrames")
        following = frames[position + 1].start if position + 1 < len(frames) else frame.start + (frame_time or period)
        if paced and late is not None and period > 0:
            preferred = max(frame.at("preferredSwapInterval") or 1, 1)
            limit = (SLOW_DOWN_LATE_PERCENT * ((FRAME_WINDOW_TICKS + (period * preferred) // 2) // (period * preferred))) // 100 + 1
            style = "late-full" if late >= limit else ("late-high" if 2 * late >= limit else "late-low")
            bar = max(16 * min(late / limit, 1.0), 1.0)
            about = tip(
                zero,
                f"{tag}: {late} late frames in the pacer's frame window of {frame.at('pacerWindowFrames') or 0} frames",
                frame.start,
                None,
                f"The rule slows down at {limit} (its default settings, at this swap interval).",
            )
            svg.rect(x(frame.start), late_y + 16 - bar, max(x(min(following, stretch.time_last)) - x(frame.start), MIN_WIDTH), bar, style, tip=about)
            if offset == len(rows) - 1:
                svg.text(x(min(following, stretch.time_last)) + 6, late_y + 12, f"{late} of {limit}", "limit-text")
        change = frame.at("pacerChange")
        if paced and change:
            at = x(frame.start)
            svg.line(at, expected_y - 4, at, late_y + 34, "change")
            svg.text(at + 5, late_y + 30, f"{'slower' if change == 1 else 'faster'}: swap interval {frame.swap_interval}", "change-text")

        # The CPU thread: update, the wait for the frame start, acquire, draw, a hold before the present, the present call
        if (update := frame.span("hostCpuStartTicks", "hostUpdateEndTicks")) is not None:
            _ = box(update[0], update[1], cpu_y, 26, f"f{hue} soft", tip(zero, f"{tag} update", *update))
        if (wait := start_wait(frame)) is not None and (wait[1] - wait[0]) * scale >= MIN_WIDTH:
            about = wait_tip(zero, f"{tag} wait for its frame start", wait, frame.at("frameWaitTargetTicks"), frame)
            _ = box(wait[0], wait[1], cpu_y + 7, 12, hold_style(frame), about)
        if (slot := frame.span("frameSlotWaitBeginTicks", "frameSlotWaitEndTicks")) is not None and (slot[1] - slot[0]) * scale >= MIN_WIDTH:
            _ = box(slot[0], slot[1], cpu_y + 7, 12, "wait-slot", tip(zero, f"{tag} wait for a frame slot", *slot, SLOT_WAIT_TIP))
        if (acquire := frame.span("acquireCallTicks", "acquireReturnTicks")) is not None:
            _ = box(acquire[0], acquire[1], cpu_y, 26, f"f{hue} soft", acquire_tip(zero, frame, acquire))
        if (held := unlogged_hold(frame)) is not None and (held[1] - held[0]) * scale >= 3:
            _ = box(held[0], held[1], cpu_y + 7, 12, f"u{hue}", tip(zero, f"{tag} held, no times logged", *held, UNLOGGED_TIP))
        # The frame's whole time on the CPU thread, as a strip over the lane: it shows a frame whose work is in two parts
        if (span := cpu_span(frame)) is not None:
            _ = box(span[0], span[1], cpu_y - 4, 3, f"s{hue}", tip(zero, f"{tag} on the CPU thread, from the first thing done for it to the last", *span))
        draw = frame.span("frameStartTicks", "endFrameTicks")
        if draw is not None:
            label(tag, draw[0], box(draw[0], draw[1], cpu_y, 26, f"f{hue}", draw_tip(zero, frame, draw)), cpu_y + 17)
            cpu_work.append(draw[1] - draw[0])
        if (submit := frame.span("submitCallTicks", "submitReturnTicks")) is not None:
            _ = box(submit[0], submit[1], cpu_y, 26, "submit", tip(zero, f"{tag} submit of its GPU work (call to return)", *submit))
        if (hold := present_hold(frame)) is not None and (hold[1] - hold[0]) * scale >= MIN_WIDTH:
            about = wait_tip(zero, f"{tag} hold before its present", hold, frame.at("presentWaitTargetTicks"), frame)
            _ = box(hold[0], hold[1], cpu_y + 7, 12, hold_style(frame), about)
        handed_over: tuple[float, float] | None = None
        if (present := frame.span("presentCallTicks", "presentReturnTicks")) is not None:
            at = x(present[0])
            call = "timed-call" if is_timed(frame) else f"f{hue} call"
            _ = box(present[0], present[1], cpu_y, 26, call, present_tip(zero, frame, present))
            svg.line(at, cpu_y - 6, at, cpu_y + 30, f"p{hue}")
            svg.polygon([(at - 5.5, cpu_y + 29), (at + 5.5, cpu_y + 29), (at, cpu_y + 37)], call, present_tip(zero, frame, present))
            handed_over = (at, cpu_y + 37)
        if (drawn := frame.at("markerDrawTicks")) is not None:
            svg.line(x(drawn), cpu_y + 3, x(drawn), cpu_y + 23, "marker-tick", tip(zero, f"{tag} marker recorded", drawn))
        if offset % name_every == 0:
            svg.text(x(frame.start), cpu_y - 6, tag, f"t{hue}", "middle")

        # The GPU, and the wait to be shown. The thin lines follow the frame, by one rule each. To its GPU work: from the CPU
        # thread, at its submit, and in a log without one straight down where the GPU began. To its place in the wait to be
        # shown: from its present call, straight down, and along its row to where the wait begins. To the start of that wait:
        # from the end of its GPU work
        screen = on_screen(frames, position, period)
        work = frame.span("gpuWorkBeginTicks", "gpuWorkEndTicks")
        ready = frame.at("presentReturnTicks")
        if work is not None:
            label(tag, work[0], box(work[0], work[1], gpu_y, 26, f"f{hue}", gpu_tip(zero, frame, work)), gpu_y + 17)
            gpu_work.append(work[1] - work[0])
            ready = max(work[1], ready or work[1])
            svg.polyline([(x(submit[0] if submit is not None else work[0]), cpu_y + 26), (x(work[0]), gpu_y)], f"l{hue}")
        # The frame's row of the waiting lane, taken from its present call (it is queued from then on) to its display time; a
        # frame without a display time takes the room its mark needs
        queued = min(present[0], ready) if present is not None and ready is not None else ready
        no_time = screen is None and frame.at("presentTimingRequested") == 1 and frame.index <= last_reported
        waiting: float | None = None
        if ready is not None and queued is not None and screen is not None and (screen.shown - ready) * scale >= MIN_WIDTH:
            waiting = queue_row(queued, screen.shown)
            about = tip(zero, f"{tag} waiting to be shown", ready, screen.shown, WAITING_TIP)
            _ = box(ready, screen.shown, waiting + 1, QUEUE_ROW_HEIGHT - 2, f"q{hue}", about)
            if work is not None:
                svg.polyline([(x(work[1]), gpu_y + 26), (x(ready), waiting + 1)], f"l{hue}")
        elif no_time and ready is not None and queued is not None:
            waiting = queue_row(queued, ready + round(56 / scale))
        elif work is not None and screen is not None:
            svg.polyline([(x(work[1]), gpu_y + 26), (x(work[1]), queue_y + 1)], f"l{hue}")
        if handed_over is not None and ready is not None and (waiting is not None or screen is not None):
            middle = (waiting if waiting is not None else queue_y) + (QUEUE_ROW_HEIGHT / 2)
            svg.polyline([handed_over, (handed_over[0], middle), (max(x(ready), handed_over[0]), middle)], f"l{hue}")

        # The display: on screen from the reported display time to the next frame's, and the animation error of the step into it
        error = animation_error(frames, position)
        if screen is not None:
            is_longer = held_longer(frame, screen.shown, screen.end, period)
            longer += 1 if is_longer else 0
            about = display_tip(zero, frame, screen, period, error, position > 0)
            width = box(screen.shown, min(screen.end, stretch.time_last), screen_y, SCREEN_HEIGHT, f"f{hue} long" if is_longer else f"f{hue}", about)
            label(tag, screen.shown, width, screen_y + 14)
            if error is not None:
                error_label(error, screen.shown, width, screen_y + 28)
                errors.append(error)
            if screen.unknown_end is not None and screen.end < stretch.time_last:
                about = unknown_tip(zero, frame, screen, last_reported)
                width = box(screen.end, min(screen.unknown_end, stretch.time_last), screen_y + 0.5, SCREEN_HEIGHT - 1, "unknown", about)
                label("?", screen.end, width, screen_y + 21, "unknown-text")
            svg.polyline([(x(screen.shown), (waiting if waiting is not None else queue_y) + QUEUE_ROW_HEIGHT - 1), (x(screen.shown), screen_y)], f"l{hue}")
            to_shown.append(screen.shown - frame.start)
        elif no_time and ready is not None and waiting is not None:
            unreported += 1
            # A mark where its wait would have begun, with room for its number
            lost = waiting
            at = x(ready) + 3
            about = tip(
                zero, f"{tag}: no display time reported", ready, None, no_display_time(frame, last_reported), "Whether it was shown takes a capture to know."
            )
            svg.rect(at, lost + 1.5, 8, 8, "no-time", tip=about)
            svg.text(at + 12, lost + 9.5, tag, "not-shown")
        relative = frame.at("presentTargetRelativeNs")
        if relative and before is not None and before.shown is not None and stretch.inside(before.shown + (relative // 100)):
            asked = before.shown + (relative // 100)
            about = tip(zero, f"{tag}: the time its timed present asked for", asked, None, f"{ms(relative // 100)} ms after #{before.index}'s display time.")
            svg.polygon([(x(asked) - 5, screen_y - 8), (x(asked) + 5, screen_y - 8), (x(asked), screen_y)], "timed", about)

        # Where the animation clock has the frame: from its animation time to the next frame's
        animation = frame.at("animationTimeTicks")
        if tied is not None and animation is not None:
            later = frames[position + 1].at("animationTimeTicks") if position + 1 < len(frames) else None
            begins = animation + tied[1]
            ends = (later + tied[1]) if later is not None else begins + (frame.swap_interval * period)
            if ends > stretch.time_first and begins < stretch.time_last:
                begins_inside, ends_inside = max(begins, stretch.time_first), min(ends, stretch.time_last)
                landed = f"Shown {apart(screen.shown - begins)} this time." if screen is not None else "The log has no display time for it."
                about = tip(
                    zero,
                    f"{tag} by its animation time: when the animation expected it on screen",
                    begins,
                    ends,
                    landed,
                    ANIMATION_TIP.format(frame=tied[0].index),
                )
                label(tag, begins_inside, box(begins_inside, ends_inside, animation_y, ANIMATION_HEIGHT, f"e{hue}", about), animation_y + 13, f"t{hue}")

    large = sum(1 for error in errors if abs(error) >= ERROR_THRESHOLD_TICKS)
    summary = (
        f"In this stretch, at the median: the draw takes {median_ms(cpu_work)} ms on the CPU, the GPU works {median_ms(gpu_work)} ms on a frame, "
        f"and a frame is shown {median_ms(to_shown)} ms after its start. {longer} on screen longer than asked, {large} of {len(errors)} animation errors "
        f"of 1 ms or more, {unreported} reported without a display time."
    )
    svg.text(28, bottom + 50, summary, "axis")
    svg.text(28, bottom + 72, f"The refresh grid is {grid}. Display times are what the graphics driver reported, not a measurement by the tools.", "sub")
    for line, note in enumerate(after):
        svg.text(28, bottom + 92 + (20 * line), note, "sub")
    return svg.render()


# ----------------------------------------------------------------------------------------------------------------------------------------
# The trace
# ----------------------------------------------------------------------------------------------------------------------------------------

PROCESS = 1
CPU_TRACK = 1
EXPECTED_TRACK = 2
VSYNC_TRACK = 4
DISPLAY_TRACK = 5
ANIMATION_TRACK = 6
# Successive frames' GPU work, their waits and their way from start to shown overlap, and spans on one track may not: they go
# round a few tracks of the same name
GPU_TRACK = 10
QUEUE_TRACK = 20
ACTUAL_TRACK = 30
POOL = 4


def microseconds(ticks: int) -> float:
    """A time in the trace's microseconds, a tick being a tenth of one: written from whole numbers, so nothing is lost."""
    whole, tenth = divmod(ticks, 10)
    return float(f"{whole}.{tenth}")


def trace_file(name: str, frames: list[Frame]) -> str:
    period = refresh_period(frames)
    zero = min(min(frame.values[key] for key in ("hostCpuStartTicks", "frameWaitStartTicks", "frameStartTicks") if key in frame.values) for frame in frames)
    events: list[dict[str, object]] = [{"ph": "M", "pid": PROCESS, "name": "process_name", "args": {"name": name}}]

    def track(tid: int, title: str, order: int) -> None:
        events.append({"ph": "M", "pid": PROCESS, "tid": tid, "name": "thread_name", "args": {"name": title}})
        events.append({"ph": "M", "pid": PROCESS, "tid": tid, "name": "thread_sort_index", "args": {"sort_index": order}})

    track(EXPECTED_TRACK, "Pacer: expected", 0)
    track(CPU_TRACK, "CPU", 9)
    for slot in range(POOL):
        track(ACTUAL_TRACK + slot, "Actual: start to shown", 1 + slot)
        track(GPU_TRACK + slot, "GPU", 10 + slot)
        track(QUEUE_TRACK + slot, "Waiting to be shown", 20 + slot)
    track(DISPLAY_TRACK, "Display", 30)
    track(ANIMATION_TRACK, "By its animation time", 31)
    track(VSYNC_TRACK, "Vertical blank (as read)", 32)

    def span(tid: int, title: str, first: int, last: int, frame: Frame, arguments: dict[str, object] | None = None) -> None:
        details: dict[str, object] = {"frame": frame.index}
        details.update(arguments or {})
        events.append(
            {"ph": "X", "pid": PROCESS, "tid": tid, "name": title, "ts": microseconds(first - zero), "dur": microseconds(max(last - first, 1)), "args": details}
        )

    def flow(phase: str, tid: int, at: int, frame: Frame) -> None:
        event: dict[str, object] = {"ph": phase, "pid": PROCESS, "tid": tid, "name": "frame", "cat": "frame", "id": frame.index, "ts": microseconds(at - zero)}
        if phase == "f":
            event["bp"] = "e"
        events.append(event)

    seen: set[int] = set()
    expected_end = zero
    last_reported = frames[-1].index - SKIP_LAST
    tied = animation_tie(frames)
    for position, frame in enumerate(frames):
        label = f"#{frame.index}"
        slot = position % POOL
        draw = frame.span("frameStartTicks", "endFrameTicks")
        stages = (
            ("update", frame.span("hostCpuStartTicks", "hostUpdateEndTicks")),
            ("wait for the frame start", start_wait(frame)),
            ("wait for a frame slot", frame.span("frameSlotWaitBeginTicks", "frameSlotWaitEndTicks")),
            ("acquire", frame.span("acquireCallTicks", "acquireReturnTicks")),
            ("draw", draw),
            ("hold before the present", present_hold(frame)),
            ("present", frame.span("presentCallTicks", "presentReturnTicks")),
        )
        cpu_end = zero
        for title, stage in stages:
            # Stages on one track may not overlap: one that starts before the last one ended starts where that ended
            if stage is not None and stage[1] > max(stage[0], cpu_end):
                span(CPU_TRACK, f"{title} {label}", max(stage[0], cpu_end), stage[1], frame)
                cpu_end = stage[1]
        # The submit is inside the draw: a span within a span
        submit = frame.span("submitCallTicks", "submitReturnTicks")
        if submit is not None and draw is not None and draw[0] <= submit[0] < submit[1] <= draw[1]:
            span(CPU_TRACK, f"submit {label}", submit[0], submit[1], frame)
        screen = on_screen(frames, position, period)
        work = frame.span("gpuWorkBeginTicks", "gpuWorkEndTicks")
        ready = frame.at("presentReturnTicks")
        if work is not None:
            span(GPU_TRACK + slot, f"GPU {label}", work[0], work[1], frame)
            ready = max(work[1], ready or work[1])
            if frame.span("frameStartTicks", "endFrameTicks") is not None:
                flow("s", CPU_TRACK, frame.start, frame)
                flow("t", GPU_TRACK + slot, work[0], frame)
        if ready is not None and screen is not None and screen.shown > ready:
            span(QUEUE_TRACK + slot, f"waiting {label}", ready, screen.shown, frame)
        if screen is not None and screen.end > screen.shown:
            longer = held_longer(frame, screen.shown, screen.end, period)
            details: dict[str, object] = {"swap interval": frame.swap_interval, "display time": "as the graphics driver reported it"}
            if (error := animation_error(frames, position)) is not None:
                details["animation error (ms)"] = float(signed_ms(error, 4))
            span(DISPLAY_TRACK, f"{label} (longer than asked)" if longer else label, screen.shown, screen.end, frame, details)
            if screen.unknown_end is not None:
                # The log has no display time for the frames presented next: who was on screen is not known
                reasons: dict[str, object] = {f"#{later.index}": no_display_time(later, last_reported) for later in screen.unreported}
                span(DISPLAY_TRACK, f"not known: {label} or {', '.join(reasons)}", screen.end, screen.unknown_end, frame, reasons)
            if work is not None and frame.span("frameStartTicks", "endFrameTicks") is not None:
                flow("f", DISPLAY_TRACK, screen.shown, frame)
            span(ACTUAL_TRACK + slot, label, frame.start, screen.shown, frame)
        # Where the animation clock has the frame, tied to the display at the run's first shown frame
        animation = frame.at("animationTimeTicks")
        later_animation = frames[position + 1].at("animationTimeTicks") if position + 1 < len(frames) else None
        if tied is not None and animation is not None and later_animation is not None and later_animation > animation and animation + tied[1] >= zero:
            span(ANIMATION_TRACK, label, animation + tied[1], later_animation + tied[1], frame, {"animation time (ms)": float(ms(animation, 4))})
        before = frames[position - 1] if position > 0 else None
        expected_start = before.at("nextFrameStartTicks") if before is not None else None
        intended = frame.at("intendedDisplayTicks")
        if expected_start is not None and intended is not None:
            # One track: an expected span that begins before the one before it ended begins where that ended
            expected_start = max(expected_start, expected_end)
            if intended > expected_start:
                span(EXPECTED_TRACK, label, expected_start, intended, frame)
                expected_end = intended
        if (blank := frame.at("displayVSyncTicks")) is not None and blank >= zero and blank not in seen:
            seen.add(blank)
            events.append({"ph": "i", "pid": PROCESS, "tid": VSYNC_TRACK, "name": "vertical blank", "ts": microseconds(blank - zero), "s": "t"})
        counters = {key: frame.values[key] for key in ("swapInterval", "pacerWindowLateFrames") if key in frame.values}
        if counters:
            events.append({"ph": "C", "pid": PROCESS, "name": "pacer", "ts": microseconds(frame.start - zero), "args": counters})
    return json.dumps({"displayTimeUnit": "ms", "traceEvents": events}, separators=(",", ":")) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=(__doc__ or "").splitlines()[0])
    _ = parser.add_argument("source", type=Path, help="a frame log (CSV), or a capture session's zip")
    _ = parser.add_argument("--run", default="", help="the run in a zip, as <folder>/<name>")
    _ = parser.add_argument("--list", dest="list_runs", action="store_true", help="list the runs of a zip")
    _ = parser.add_argument("--from", dest="first", type=int, default=120, help="the first frame of the chart (default 120)")
    _ = parser.add_argument("--frames", type=int, default=12, help=f"how many frames the chart shows (default 12, at most {MAX_FRAMES})")
    _ = parser.add_argument("--view", choices=("rows", "lanes"), default="rows", help="a row per frame (default), or a lane per resource")
    _ = parser.add_argument("-o", "--output", type=Path, help="write the chart (SVG) here")
    _ = parser.add_argument("--trace", type=Path, help="write the whole run as a trace file (Chrome trace event JSON) here")
    arguments = parser.parse_args(namespace=Arguments())

    if arguments.list_runs:
        with zipfile.ZipFile(arguments.source) as archive:
            print("\n".join(runs_in(archive)))
        return 0
    if arguments.output is None and arguments.trace is None:
        parser.error("nothing to write: give -o for the chart, --trace for the trace file, or both")
    if arguments.output is not None and not 1 <= arguments.frames <= MAX_FRAMES:
        parser.error(f"the chart shows 1 to {MAX_FRAMES} frames, a row each: choose a stretch with --from, or write the whole run with --trace")
    name, frames, notes = read_source(arguments.source, arguments.run)
    for note in notes:
        print(note)
    if not frames:
        print(f"{arguments.source.name}: no frames with a start time", file=sys.stderr)
        return 1
    if arguments.output is not None:
        chart = lanes_chart if arguments.view == "lanes" else stages_chart
        _ = arguments.output.write_text(chart(name, frames, arguments.first, arguments.frames, notes), encoding="utf-8", newline="\n")
        print(f"{arguments.output}: {min(arguments.frames, len(frames))} frames of {name}")
    if arguments.trace is not None:
        _ = arguments.trace.write_text(trace_file(name, frames), encoding="utf-8", newline="\n")
        print(f"{arguments.trace}: {len(frames)} frames of {name}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
