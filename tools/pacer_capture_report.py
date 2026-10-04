#!/usr/bin/env python3
# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""Extract what a pacer capture session shows: a row per run, the charts, and the tables of its document.

A capture session is one zip in pacer-captures/ (frame logs of the experimental pacer on real swap chains, written by the first
integration's sample). This reads the zip in place, works every run out frame by frame, and writes next to it, in a folder named as
the zip:

  runs.csv   one row per run: what was set, the work, the frame starts, what the display did, what the pacer decided
  *.svg      the charts the session's document embeds

    python tools/pacer_capture_report.py pacer-captures/2026-10-04-windows-hold.zip                # runs.csv and the charts
    python tools/pacer_capture_report.py pacer-captures/2026-10-04-windows-hold.zip --update-doc   # and the document's tables
    python tools/pacer_capture_report.py pacer-captures/2026-10-04-windows-hold.zip --check        # the files equal the zip's data

The files hold whole numbers only (times in ticks of 100 ns, counts): a share or a millisecond value is worked out where it is shown.
The first frames of a run (start-up) and its last ones (their display times had not come back) are left out of every count.
The document is the zip's name with .md; its tables are the blocks between "<!-- pacer-capture:<name> ... -->" comments.
"""

import argparse
import csv
import io
import itertools
import statistics
import sys
import zipfile
from dataclasses import dataclass
from pathlib import Path

# Frames left out at the start of a run and at its end
SKIP_FIRST = 60
SKIP_LAST = 8

# The rule's defaults (PacerSettings): the frame window's length and the share of late frames it slows down beyond
FRAME_WINDOW_TICKS = 20_000_000
SLOW_DOWN_LATE_PERCENT = 10

TICKS_PER_MS = 10_000
GENERATOR = "tools/pacer_capture_report.py"


class Arguments(argparse.Namespace):
    capture: Path = Path()
    update_doc: bool = False
    check: bool = False


@dataclass(frozen=True)
class Frame:
    """A frame of a run's log. Times in ticks of the application's clock."""

    index: int
    start: int
    # When the display showed it; None: not timed, or never shown
    shown: int | None
    timed: bool
    swap_interval: int
    work: int
    window_late: int


@dataclass(frozen=True)
class Run:
    """A run: what runs.csv holds of it (columns), and its frames for the charts."""

    folder: str
    name: str
    refresh_period: int
    swapchain_refresh_ns: int
    # How often the swap chain reported its refresh, and how often as a fixed one (its interval equal to its duration)
    swapchain_reads: int
    swapchain_reads_fixed: int
    gsync: str
    load: str
    pacer: str
    hold: str
    vsync_source: str
    vsync_phase_percent: int
    target_swap_interval: int
    frames_in_flight: int
    present_timing: int
    present_feedback: int
    work_median: int
    start_step_median: int
    start_step_p1: int
    start_step_p99: int
    frames: int
    frames_timed: int
    exact: int
    longer: int
    shorter: int
    never_shown: int
    refreshes_lost: int
    first_slower_frame: int
    final_swap_interval: int
    most_late_frames: int
    late_frames_to_slow_down: int
    feedback_used: int
    feedback_refused: int
    feedback_not_shown: int
    feedback_late_refreshes: int
    log: tuple[Frame, ...]

    @property
    def steps(self) -> int:
        """The display time steps that were judged: a frame shown, and the next frame shown."""
        return self.exact + self.longer + self.shorter

    @property
    def off(self) -> int:
        """The steps that were not the frame's swap interval."""
        return self.longer + self.shorter

    @property
    def refresh_hz(self) -> int:
        return round(10_000_000 / self.refresh_period)

    @property
    def fps(self) -> int:
        return round(10_000_000 / (self.refresh_period * self.target_swap_interval))

    def columns(self) -> list[tuple[str, str | int]]:
        return [
            ("folder", self.folder),
            ("run", self.name),
            ("refreshPeriodTicks", self.refresh_period),
            ("swapchainRefreshNs", self.swapchain_refresh_ns),
            ("swapchainRefreshReads", self.swapchain_reads),
            ("swapchainRefreshReadsFixed", self.swapchain_reads_fixed),
            ("gsync", self.gsync),
            ("load", self.load),
            ("pacer", self.pacer),
            ("holdMethod", self.hold),
            ("vsyncSource", self.vsync_source),
            ("vsyncPhasePercent", self.vsync_phase_percent),
            ("targetSwapInterval", self.target_swap_interval),
            ("framesInFlight", self.frames_in_flight),
            ("presentTiming", self.present_timing),
            ("presentFeedback", self.present_feedback),
            ("workMedianTicks", self.work_median),
            ("startStepMedianTicks", self.start_step_median),
            ("startStepP1Ticks", self.start_step_p1),
            ("startStepP99Ticks", self.start_step_p99),
            ("frames", self.frames),
            ("framesTimed", self.frames_timed),
            ("shownForSwapInterval", self.exact),
            ("shownLonger", self.longer),
            ("shownShorter", self.shorter),
            ("neverShown", self.never_shown),
            ("refreshesLost", self.refreshes_lost),
            ("firstSlowerFrame", self.first_slower_frame),
            ("finalSwapInterval", self.final_swap_interval),
            ("mostLateFramesInWindow", self.most_late_frames),
            ("lateFramesToSlowDown", self.late_frames_to_slow_down),
            ("feedbackUsed", self.feedback_used),
            ("feedbackRefused", self.feedback_refused),
            ("feedbackNotShown", self.feedback_not_shown),
            ("feedbackLateRefreshes", self.feedback_late_refreshes),
        ]


# ----------------------------------------------------------------------------------------------------------------------------------------
# Reading the zip
# ----------------------------------------------------------------------------------------------------------------------------------------


def number(text: str | None) -> int:
    return int(text) if text else 0


def percentile(values: list[int], percent: int) -> int:
    ordered = sorted(values)
    return ordered[(len(ordered) - 1) * percent // 100] if ordered else 0


def details(text: str) -> dict[str, str]:
    """An event's details: key=value pairs, joined by ';'."""
    pairs = (part.split("=", 1) for part in text.split(";") if "=" in part)
    return {pair[0]: pair[1] for pair in pairs}


def read_text(archive: zipfile.ZipFile, name: str) -> str:
    return archive.read(name).decode("utf-8")


def read_frames(text: str) -> tuple[tuple[Frame, ...], int, list[str]]:
    """A frame log: its frames, the swap interval the application prefers, and its last row's feedback counters."""
    reader = csv.reader(io.StringIO(text))
    header = next(reader)
    at = {name: index for index, name in enumerate(header)}
    rows = [row for row in reader if len(row) == len(header)]

    def cell(row: list[str], name: str) -> str:
        return row[at[name]] if name in at else ""

    log = tuple(
        Frame(
            index=number(cell(row, "frameIndex")),
            start=number(cell(row, "frameStartTicks")),
            shown=number(cell(row, "firstPixelOutTicks")) if cell(row, "firstPixelOutTicks") else None,
            timed=cell(row, "presentTimingRequested") == "1",
            swap_interval=number(cell(row, "swapInterval")) or 1,
            work=number(cell(row, "workCpuTicks")) + number(cell(row, "workGpuTicks")),
            window_late=number(cell(row, "pacerWindowLateFrames")),
        )
        for row in rows
    )
    preferred = number(cell(rows[min(SKIP_FIRST, len(rows) - 1)], "preferredSwapInterval")) or 1
    counters = ["pacerFeedbackUsed", "pacerFeedbackRefused", "pacerFeedbackNotShown", "pacerFeedbackLateRefreshes"]
    return log, preferred, [cell(rows[-1], name) for name in counters]


def read_run(archive: zipfile.ZipFile, base: str, folder: str, name: str) -> Run:
    prefix = f"{base}/{folder}/{name}"
    # The events: the facts of the run, and what was set
    facts: dict[str, str] = {}
    config: dict[str, str] = {}
    swapchain: dict[str, str] = {}
    timing: dict[str, str] = {}
    refresh_period = 0
    swapchain_refresh: list[int] = []
    swapchain_fixed = 0
    for row in csv.reader(io.StringIO(read_text(archive, f"{prefix}.events.csv"))):
        if len(row) < 4 or row[0] == "frameIndex":
            continue
        event, text = row[2], row[3]
        if event == "fact":
            key, _, value = text.partition("=")
            facts[key] = value
        elif event == "pacerConfig":
            config = details(text)
        elif event == "swapchainCreated":
            swapchain = details(text)
        elif event == "presentTiming":
            timing = details(text)
        elif event == "display":
            refresh_period = number(details(text).get("refreshIntervalTicks"))
        elif event == "refreshProperties":
            reported = details(text)
            swapchain_refresh.append(number(reported.get("refreshDurationNs")))
            swapchain_fixed += 1 if reported.get("refreshIntervalNs") == reported.get("refreshDurationNs") else 0
    notes_name = f"{prefix}.run.txt"
    notes = read_text(archive, notes_name) if notes_name in archive.namelist() else ""
    gsync = next((line.split(":", 1)[1] for line in notes.splitlines() if line.strip().startswith("gsync:")), "")

    log, preferred, counters = read_frames(read_text(archive, f"{prefix}.csv"))
    counted = log[SKIP_FIRST : len(log) - SKIP_LAST]

    # What the display did: the whole refreshes from one frame shown to the next one shown, against the swap intervals between
    longer = shorter = exact = lost = never = 0
    planned = 0
    previous: Frame | None = None
    for frame in counted:
        planned += frame.swap_interval
        if frame.shown is None:
            never += 1 if frame.timed else 0
            continue
        if previous is not None and previous.shown is not None:
            refreshes = (2 * (frame.shown - previous.shown) + refresh_period) // (2 * refresh_period)
            lost += max(0, refreshes - planned)
            if frame.index == previous.index + 1:
                exact += 1 if refreshes == planned else 0
                longer += 1 if refreshes > planned else 0
                shorter += 1 if refreshes < planned else 0
        previous = frame
        planned = 0

    start_steps = [after.start - before.start for before, after in itertools.pairwise(counted)]
    slower = next((after.index for before, after in itertools.pairwise(log) if after.swap_interval > before.swap_interval), 0)
    full_window = (FRAME_WINDOW_TICKS + (refresh_period * preferred) // 2) // (refresh_period * preferred)
    pacer_on = config.get("on") == "1"
    load = name.rsplit("_", 1)[-1]
    return Run(
        folder=folder,
        name=name,
        refresh_period=refresh_period,
        swapchain_refresh_ns=statistics.median_low(swapchain_refresh) if swapchain_refresh else 0,
        swapchain_reads=len(swapchain_refresh),
        swapchain_reads_fixed=swapchain_fixed,
        gsync=gsync.split("(")[0].strip(),
        load=load if load in ("idle", "loaded") else "",
        pacer=("adaptive" if config.get("adaptive") == "1" else "fixed") if pacer_on else "off",
        hold={"wait": "sleep", "vsync": "vsync"}.get(config.get("hold", ""), "") if pacer_on else "",
        vsync_source=facts.get("window.vsyncSource", ""),
        vsync_phase_percent=number(config.get("vsyncPhasePercent")),
        target_swap_interval=preferred,
        frames_in_flight=number(swapchain.get("framesInFlight")),
        present_timing=number(timing.get("enabled")),
        present_feedback=number(config.get("presentFeedback")),
        work_median=percentile([frame.work for frame in counted], 50),
        start_step_median=percentile(start_steps, 50),
        start_step_p1=percentile(start_steps, 1),
        start_step_p99=percentile(start_steps, 99),
        frames=len(log),
        frames_timed=sum(1 for frame in counted if frame.shown is not None),
        exact=exact,
        longer=longer,
        shorter=shorter,
        never_shown=never,
        refreshes_lost=lost,
        first_slower_frame=slower,
        final_swap_interval=log[-1].swap_interval,
        most_late_frames=max(frame.window_late for frame in log),
        late_frames_to_slow_down=(SLOW_DOWN_LATE_PERCENT * full_window) // 100 + 1,
        feedback_used=number(counters[0]),
        feedback_refused=number(counters[1]),
        feedback_not_shown=number(counters[2]),
        feedback_late_refreshes=number(counters[3]),
        log=log,
    )


def read_runs(archive: zipfile.ZipFile) -> list[Run]:
    names = archive.namelist()
    base = names[0].split("/")[0]
    runs: list[Run] = []
    for name in sorted(names):
        parts = name.split("/")
        if len(parts) == 3 and parts[2].endswith(".events.csv"):
            runs.append(read_run(archive, base, parts[1], parts[2].removesuffix(".events.csv")))
    return runs


def runs_csv(runs: list[Run]) -> str:
    lines = [",".join(name for name, _ in runs[0].columns())]
    lines.extend(",".join(str(value) for _, value in run.columns()) for run in runs)
    return "\n".join(lines) + "\n"


# ----------------------------------------------------------------------------------------------------------------------------------------
# Selections the charts and the tables share
# ----------------------------------------------------------------------------------------------------------------------------------------

HOLDS = [("sleep", "idle"), ("sleep", "loaded"), ("vsync", "idle"), ("vsync", "loaded")]
HOLD_NAMES = {"sleep": "Timer sleep", "vsync": "Vsync wait"}


@dataclass(frozen=True)
class HoldCell:
    """The runs of one refresh rate, frame rate, hold method and load, added up."""

    off: int
    steps: int
    # The frame starts' spread around their median: the widest of the runs
    below: int
    above: int


def hold_groups(runs: list[Run]) -> list[tuple[int, int, dict[tuple[str, str], HoldCell]]]:
    """Fixed frame rates with light work, G-SYNC off, on the primary display: per refresh rate and frame rate, sleep against vsync wait."""
    chosen = [
        run
        for run in runs
        if run.pacer == "fixed"
        and run.present_timing == 1
        and run.gsync == "off"
        and run.vsync_source == "dxgi"
        and run.name.startswith("fps")
        and run.load != ""
        and not run.folder.startswith("second-display")
    ]
    groups: list[tuple[int, int, dict[tuple[str, str], HoldCell]]] = []
    for hz, fps in sorted({(run.refresh_hz, run.fps) for run in chosen}, reverse=True):
        cells: dict[tuple[str, str], HoldCell] = {}
        for hold, load in HOLDS:
            members = [run for run in chosen if (run.refresh_hz, run.fps, run.hold, run.load) == (hz, fps, hold, load)]
            if members:
                cells[(hold, load)] = HoldCell(
                    off=sum(run.off for run in members),
                    steps=sum(run.steps for run in members),
                    below=max(run.start_step_median - run.start_step_p1 for run in members),
                    above=max(run.start_step_p99 - run.start_step_median for run in members),
                )
        groups.append((hz, fps, cells))
    return groups


def placement_runs(runs: list[Run]) -> dict[tuple[int, int, str], Run]:
    """The placement sweep: by refresh rate, the place of the present in percent, and the load."""
    return {(run.refresh_hz, run.vsync_phase_percent, run.load): run for run in runs if run.folder.endswith("phase-sweep")}


def find(runs: list[Run], folder: str, name: str) -> Run:
    return next(run for run in runs if run.folder == folder and run.name == name)


def ms(ticks: int, digits: int = 3) -> str:
    return f"{ticks / TICKS_PER_MS:.{digits}f}"


def around(below: int, above: int) -> str:
    """A spread around a median, in milliseconds: "-0.143 to +0.132", and "0.000" for what rounds to nothing."""
    low = f"-{ms(below)}" if below >= 5 else "0.000"
    high = f"+{ms(above)}" if above >= 5 else "0.000"
    return f"{low} to {high}"


# ----------------------------------------------------------------------------------------------------------------------------------------
# SVG
# ----------------------------------------------------------------------------------------------------------------------------------------

# The look of the repository's charts (measure/libs/MB.FramePacing.Charts/source/SvgMarkup.cs): the dark card, its text and its colours
STYLE = """
  text { font-family: "Segoe UI Variable Text", "Segoe UI", Inter, system-ui, -apple-system, "Helvetica Neue", Arial, sans-serif;
         font-size: 13px; fill: #e6edf3; font-variant-numeric: tabular-nums; }
  .card { fill: #1f242b; stroke: #8b949e; stroke-opacity: 0.25; stroke-width: 1; }
  .title { font-size: 20px; font-weight: 600; letter-spacing: -0.01em; }
  .sub { fill: #8b949e; }
  .label { font-size: 11px; font-weight: 600; letter-spacing: 0.08em; fill: #8b949e; }
  .axis { font-size: 12px; fill: #c9d1d9; }
  .value { font-size: 11px; fill: #c9d1d9; }
  .grid { stroke: #ffffff; stroke-opacity: 0.1; stroke-width: 1; }
  .zero-line { stroke: #ffffff; stroke-opacity: 0.45; stroke-width: 1; }
  .band { fill: #ffffff; fill-opacity: 0.06; }
  .green { fill: #2ea043; }
  .green-faint { fill: #2ea043; fill-opacity: 0.45; }
  .blue { fill: #58a6ff; }
  .blue-faint { fill: #58a6ff; fill-opacity: 0.45; }
  .red { fill: #e5534b; }
  .amber { fill: #d29922; }
  .line { fill: none; stroke-width: 1.6; stroke-linejoin: round; }
  .line-blue { stroke: #58a6ff; }
  .line-green { stroke: #2ea043; }
  .line-amber { stroke: #d29922; }
  .line-pink { stroke: #db61a2; }
  .threshold { stroke: #e5534b; stroke-width: 1.5; stroke-dasharray: 6 4; }
  .threshold-text { fill: #ff7b72; font-size: 12px; }
  .mark { stroke: #ffffff; stroke-opacity: 0.45; stroke-width: 1; stroke-dasharray: 2 4; }
"""


def coordinate(value: float) -> str:
    text = f"{value:.1f}"
    return text.removesuffix(".0")


def escape(text: str) -> str:
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


class Svg:
    """A chart on the repository's dark card: a title, a line under it, and what is drawn into it."""

    def __init__(self, width: int, height: int, title: str, subtitle: str) -> None:
        self.width: int = width
        self.height: int = height
        self.title: str = title
        self.parts: list[str] = []
        self.rect(0.5, 0.5, width - 1, height - 1, "card", radius=10)
        self.text(28, 40, title, "title")
        self.text(28, 62, subtitle, "sub")

    def rect(self, x: float, y: float, width: float, height: float, style: str, radius: float = 0) -> None:
        corner = f' rx="{coordinate(radius)}"' if radius else ""
        self.parts.append(f'<rect class="{style}" x="{coordinate(x)}" y="{coordinate(y)}" width="{coordinate(width)}" height="{coordinate(height)}"{corner}/>')

    def line(self, x1: float, y1: float, x2: float, y2: float, style: str) -> None:
        self.parts.append(f'<line class="{style}" x1="{coordinate(x1)}" y1="{coordinate(y1)}" x2="{coordinate(x2)}" y2="{coordinate(y2)}"/>')

    def text(self, x: float, y: float, text: str, style: str = "", anchor: str = "start") -> None:
        classes = f' class="{style}"' if style else ""
        align = f' text-anchor="{anchor}"' if anchor != "start" else ""
        self.parts.append(f'<text{classes} x="{coordinate(x)}" y="{coordinate(y)}"{align}>{escape(text)}</text>')

    def polyline(self, points: list[tuple[float, float]], style: str) -> None:
        path = " ".join(f"{coordinate(x)},{coordinate(y)}" for x, y in points)
        self.parts.append(f'<polyline class="{style}" points="{path}"/>')

    def key(self, x: float, y: float, entries: list[tuple[str, str]]) -> None:
        """Colour swatches with their names, left to right from x."""
        for style, name in entries:
            self.rect(x, y - 10, 12, 12, style, radius=2)
            self.text(x + 18, y, name, "axis")
            x += 18 + 7.2 * len(name) + 22

    def line_key(self, x: float, y: float, entries: list[tuple[str, str]]) -> None:
        for style, name in entries:
            self.line(x, y - 4, x + 18, y - 4, f"line {style}")
            self.text(x + 24, y, name, "axis")
            x += 24 + 7.2 * len(name) + 22

    def render(self) -> str:
        head = (
            f'<svg xmlns="http://www.w3.org/2000/svg" width="{self.width}" height="{self.height}" '
            f'viewBox="0 0 {self.width} {self.height}" role="img" aria-label="{escape(self.title)}">\n'
            f"<title>{escape(self.title)}</title>\n<style>{STYLE}</style>\n"
        )
        return head + "\n".join(self.parts) + "\n</svg>\n"


def hold_chart(runs: list[Run]) -> str:
    groups = hold_groups(runs)
    svg = Svg(
        1180,
        470,
        "Holding a frame for more than one refresh: a timer sleep against a wait on the vsync",
        "Frames per 1000 that were not shown for exactly their swap interval. Above each bar: those frames, counted. G-SYNC off.",
    )
    styles = {("sleep", "idle"): "green", ("sleep", "loaded"): "green-faint", ("vsync", "idle"): "blue", ("vsync", "loaded"): "blue-faint"}
    svg.key(28, 92, [(styles[hold], f"{HOLD_NAMES[hold[0]]}, {hold[1]}") for hold in HOLDS])
    left, right, top, bottom, most = 70.0, 1150.0, 120.0, 390.0, 15
    for tick in range(0, most + 1, 5):
        y = bottom - (bottom - top) * tick / most
        svg.line(left, y, right, y, "zero-line" if tick == 0 else "grid")
        svg.text(left - 10, y + 4, str(tick), "axis", "end")
    svg.text(28, top - 12, "PER 1000 FRAMES", "label")
    slot = (right - left) / len(groups)
    for index, (hz, fps, cells) in enumerate(groups):
        centre = left + slot * (index + 0.5)
        for bar, hold in enumerate(HOLDS):
            cell = cells.get(hold)
            if cell is None:
                continue
            x = centre - 46 + bar * 24
            height = max(2.0, (bottom - top) * (1000 * cell.off / cell.steps) / most)
            svg.rect(x, bottom - height, 20, height, styles[hold])
            svg.text(x + 10, bottom - height - 6, str(cell.off), "value", "middle")
        svg.text(centre, bottom + 22, f"{hz} Hz", "axis", "middle")
        svg.text(centre, bottom + 40, f"{fps} fps", "sub", "middle")
    steps = [cell.steps for _, _, cells in groups for cell in cells.values()]
    svg.text(28, 452, f"A bar stands for {min(steps)} to {max(steps)} frames. Every frame rate is a whole number of refreshes.", "sub")
    return svg.render()


def spread_chart(runs: list[Run]) -> str:
    groups = hold_groups(runs)
    svg = Svg(
        1180,
        150 + 44 * len(groups),
        "How evenly the frames start: a timer sleep against a wait on the vsync",
        "The time from one frame start to the next, from its 1st to its 99th percentile, against its median. G-SYNC off.",
    )
    styles = {("sleep", "idle"): "green", ("sleep", "loaded"): "green-faint", ("vsync", "idle"): "blue", ("vsync", "loaded"): "blue-faint"}
    svg.key(28, 92, [(styles[hold], f"{HOLD_NAMES[hold[0]]}, {hold[1]}") for hold in HOLDS])
    left, right, top = 190.0, 1150.0, 116.0
    low, high = -2000, 8000  # ticks around the median

    def at(ticks: int) -> float:
        return left + (right - left) * (min(max(ticks, low), high) - low) / (high - low)

    bottom = top + 44 * len(groups)
    for tick in range(low, high + 1, 1000):
        svg.line(at(tick), top, at(tick), bottom, "zero-line" if tick == 0 else "grid")
        svg.text(at(tick), bottom + 18, f"{tick / TICKS_PER_MS:+.1f} ms" if tick else "median", "axis", "middle")
    for index, (hz, fps, cells) in enumerate(groups):
        y = top + 44 * index
        svg.text(28, y + 26, f"{hz} Hz, {fps} fps", "axis")
        for bar, hold in enumerate(HOLDS):
            cell = cells.get(hold)
            if cell is not None:
                x = at(-cell.below)
                svg.rect(x, y + 5 + 9 * bar, max(2.0, at(cell.above) - x), 6, styles[hold], radius=1)
    return svg.render()


def placement_chart(runs: list[Run]) -> str:
    sweep = placement_runs(runs)
    rates = sorted({hz for hz, _, _ in sweep}, reverse=True)
    phases = sorted({phase for _, phase, _ in sweep})
    svg = Svg(
        1180,
        400,
        "Where in the refresh the present is placed, with the wait on the vsync",
        "Frames not shown for exactly two refreshes, by the place of the present: percent of a refresh before the refresh aimed at.",
    )
    svg.key(28, 92, [("blue", "Idle"), ("blue-faint", "Loaded"), ("band", "The sample's default, 65 %")])
    top, bottom, most = 130.0, 320.0, 4
    width = (1180 - 28 - 28 - 3 * 24) / 4
    for panel, hz in enumerate(rates):
        left = 28 + panel * (width + 24) + 26
        right = 28 + panel * (width + 24) + width
        svg.text(left, top - 12, f"{hz} HZ, {hz // 2} FPS", "label")
        slot = (right - left) / len(phases)
        for tick in range(most + 1):
            y = bottom - (bottom - top) * tick / most
            svg.line(left, y, right, y, "zero-line" if tick == 0 else "grid")
            svg.text(left - 8, y + 4, str(tick), "axis", "end")
        for index, phase in enumerate(phases):
            centre = left + slot * (index + 0.5)
            if phase == 65:
                svg.rect(centre - slot / 2, top, slot, bottom - top, "band")
            for bar, (load, style) in enumerate([("idle", "blue"), ("loaded", "blue-faint")]):
                run = sweep.get((hz, phase, load))
                if run is None:
                    continue
                height = max(2.0, (bottom - top) * min(run.off, most) / most)
                svg.rect(centre - 9 + bar * 9, bottom - height, 8, height, style)
            svg.text(centre, bottom + 18, str(phase), "axis", "middle")
        svg.text((left + right) / 2, bottom + 38, "% of a refresh", "sub", "middle")
    counted = sorted({run.steps for run in sweep.values()})
    svg.text(28, 384, f"Frames off, of {counted[0]} to {counted[-1]} in a run. The pacer holds every frame for two refreshes.", "sub")
    return svg.render()


def work_chart(runs: list[Run], folder: str) -> str:
    panels = [
        (
            "ONE FRAME IN FLIGHT",
            [
                ("w90_idle", "line-blue", "Idle"),
                ("w90_flight1_idle", "line-green", "Idle, again"),
                ("w90_loaded", "line-amber", "Loaded"),
                ("w90_flight1_loaded", "line-pink", "Loaded, again"),
            ],
        ),
        ("TWO FRAMES IN FLIGHT", [("w90_flight2_idle", "line-blue", "Idle"), ("w90_flight2_loaded", "line-amber", "Loaded")]),
    ]
    first = find(runs, folder, "w90_idle")
    svg = Svg(
        1180,
        470,
        f"Work of {round(100 * first.work_median / first.refresh_period)} % of a refresh at {first.refresh_hz} Hz: the rule at its threshold",
        "The late frames in the pacer's frame window (the last 2 s) over a run. It slows down when they pass the dashed line.",
    )
    top, bottom, most = 130.0, 400.0, 60
    width = (1180 - 28 - 28 - 40) / 2
    for panel, (title, lines) in enumerate(panels):
        left = 28 + panel * (width + 40) + 30
        right = 28 + panel * (width + 40) + width
        frames = max(find(runs, folder, name).frames for name, _, _ in lines)
        svg.text(left, top - 34, title, "label")
        svg.line_key(left, top - 12, [(style, label) for _, style, label in lines])
        for tick in range(0, most + 1, 10):
            y = bottom - (bottom - top) * tick / most
            svg.line(left, y, right, y, "zero-line" if tick == 0 else "grid")
            svg.text(left - 8, y + 4, str(tick), "axis", "end")
        for tick in range(0, frames + 1, 400):
            x = left + (right - left) * tick / frames
            svg.text(x, bottom + 18, str(tick), "axis", "middle")
        svg.text((left + right) / 2, bottom + 38, "frame", "sub", "middle")
        threshold = first.late_frames_to_slow_down - 1
        y = bottom - (bottom - top) * threshold / most
        svg.line(left, y, right, y, "threshold")
        svg.text(right, y - 6, f"more than {threshold} late frames: slower", "threshold-text", "end")
        for name, style, _ in lines:
            run = find(runs, folder, name)
            # The most late frames of every four frames: a point per pixel is enough
            points = [
                (left + (right - left) * start / frames, bottom - (bottom - top) * max(frame.window_late for frame in run.log[start : start + 4]) / most)
                for start in range(0, len(run.log), 4)
            ]
            svg.polyline(points, f"line {style}")
            if run.first_slower_frame:
                x = left + (right - left) * run.first_slower_frame / frames
                svg.line(x, top, x, bottom, "mark")
                svg.text(x + 6, top + 12, f"two refreshes from frame {run.first_slower_frame}", "value")
    svg.text(28, 456, "After a change of swap interval the frame window starts empty, so the count falls to zero.", "sub")
    return svg.render()


def gsync_rows(runs: list[Run]) -> list[tuple[str, Run]]:
    """The same frame rates and hold methods with G-SYNC off and on, at 240 Hz."""
    rows: list[tuple[str, Run]] = []
    for fps in (120, 60):
        for hold, short in (("sleep", "wait"), ("vsync", "vsync")):
            off = find(runs, "240hz-plain-vulkan", f"fps{fps}_{short}_idle")
            on = find(runs, "240hz-gsync-windowed-and-fullscreen-hold", f"fps{fps}_{short}")
            rows.append((f"{fps} fps, {HOLD_NAMES[hold].lower()}, G-SYNC off", off))
            rows.append((f"{fps} fps, {HOLD_NAMES[hold].lower()}, G-SYNC on", on))
    return rows


def gsync_chart(runs: list[Run]) -> str:
    rows = gsync_rows(runs)
    svg = Svg(
        1180,
        150 + 34 * len(rows),
        "G-SYNC on against off at 240 Hz: what a hold method still holds",
        "Left: frames by how long they were shown against their swap interval. Right: the frame starts, 1st to 99th percentile around the median.",
    )
    svg.key(28, 92, [("green", "Shown for its swap interval"), ("red", "Longer"), ("amber", "Shorter"), ("blue", "Frame start spread")])
    top = 116.0
    share_left, share_right = 300.0, 700.0
    spread_left, spread_right = 760.0, 1150.0
    low, high = -30_000, 30_000
    bottom = top + 34 * len(rows)

    def at(ticks: int) -> float:
        return spread_left + (spread_right - spread_left) * (min(max(ticks, low), high) - low) / (high - low)

    for tick in range(0, 101, 25):
        x = share_left + (share_right - share_left) * tick / 100
        svg.line(x, top, x, bottom, "grid")
        svg.text(x, bottom + 18, f"{tick} %", "axis", "middle")
    for tick in range(low, high + 1, 10_000):
        svg.line(at(tick), top, at(tick), bottom, "grid" if tick else "zero-line")
        svg.text(at(tick), bottom + 18, f"{tick / TICKS_PER_MS:+.0f} ms" if tick else "median", "axis", "middle")
    for index, (label, run) in enumerate(rows):
        y = top + 34 * index
        svg.text(28, y + 21, label, "axis")
        x = share_left
        for count, style in ((run.exact, "green"), (run.longer, "red"), (run.shorter, "amber")):
            width = (share_right - share_left) * count / run.steps
            if width > 0:
                svg.rect(x, y + 8, width, 16, style)
            x += width
        start = at(run.start_step_p1 - run.start_step_median)
        svg.rect(start, y + 11, max(2.0, at(run.start_step_p99 - run.start_step_median) - start), 10, "blue", radius=1)
    return svg.render()


# ----------------------------------------------------------------------------------------------------------------------------------------
# The document's tables
# ----------------------------------------------------------------------------------------------------------------------------------------


NO_COLUMNS: frozenset[int] = frozenset()


def table(header: list[str], rows: list[list[str]], right: frozenset[int] = NO_COLUMNS) -> str:
    """A Markdown table as Prettier formats it: every column as wide as its widest cell."""
    widths = [max(3, len(header[column]), *(len(row[column]) for row in rows)) for column in range(len(header))]

    def line(cells: list[str]) -> str:
        padded = (cell.rjust(widths[column]) if column in right else cell.ljust(widths[column]) for column, cell in enumerate(cells))
        return "| " + " | ".join(padded) + " |"

    rule = ["-" * (widths[column] - 1) + ":" if column in right else "-" * widths[column] for column in range(len(header))]
    return "\n".join([line(header), "| " + " | ".join(rule) + " |", *(line(row) for row in rows)])


def of(count: int, total: int) -> str:
    return f"{count} of {total}"


def hold_table(runs: list[Run]) -> str:
    rows = [
        [f"{hz} Hz", f"{fps} fps", *(of(cells[hold].off, cells[hold].steps) if hold in cells else "" for hold in HOLDS)] for hz, fps, cells in hold_groups(runs)
    ]
    header = ["Display", "Frame rate", *(f"{HOLD_NAMES[hold]}, {load}" for hold, load in HOLDS)]
    return table(header, rows, frozenset(range(2, 6)))


def spread_table(runs: list[Run]) -> str:
    rows = [
        [f"{hz} Hz", f"{fps} fps", *(around(cells[hold].below, cells[hold].above) if hold in cells else "" for hold in HOLDS)]
        for hz, fps, cells in hold_groups(runs)
    ]
    header = ["Display", "Frame rate", *(f"{HOLD_NAMES[hold]}, {load} (ms)" for hold, load in HOLDS)]
    return table(header, rows, frozenset(range(2, 6)))


def placement_table(runs: list[Run]) -> str:
    sweep = placement_runs(runs)
    rates = sorted({hz for hz, _, _ in sweep}, reverse=True)
    phases = sorted({phase for _, phase, _ in sweep})
    header = ["Place of the present", *(f"{hz} Hz, {load}" for hz in rates for load in ("idle", "loaded"))]
    rows = [[f"{phase} %", *(str(sweep[(hz, phase, load)].off) for hz in rates for load in ("idle", "loaded"))] for phase in phases]
    return table(header, rows, frozenset(range(1, len(header))))


def work_130_table(runs: list[Run]) -> str:
    chosen = [run for run in runs if run.name.startswith("w130") and run.present_timing == 1 and run.vsync_source == "dxgi" and run.gsync == "off"]
    rows: list[list[str]] = []
    for hz in sorted({run.refresh_hz for run in chosen}, reverse=True):
        for hold in ("sleep", "vsync"):
            members = [run for run in chosen if run.refresh_hz == hz and run.hold == hold]
            if not members:
                continue
            rows.append(
                [
                    f"{hz} Hz",
                    HOLD_NAMES[hold],
                    f"{round(100 * members[0].work_median / members[0].refresh_period)} %",
                    ", ".join(sorted({str(run.first_slower_frame) for run in members})),
                    str(members[0].late_frames_to_slow_down - 1),
                    of(sum(run.off for run in members), sum(run.steps for run in members)),
                ]
            )
    header = ["Display", "Hold", "Work", "Two refreshes from frame", "Late frames it takes more than", "Frames off"]
    return table(header, rows, frozenset({2, 3, 4, 5}))


def work_90_table(runs: list[Run]) -> str:
    chosen = [run for run in runs if run.name.startswith("w90") and run.folder.endswith("plain-vulkan")]
    chosen.sort(key=lambda run: (-run.refresh_hz, run.frames_in_flight, run.load, run.name))
    rows = [
        [
            f"{run.refresh_hz} Hz",
            f"`{run.name}`",
            str(run.frames_in_flight),
            run.load,
            f"{round(100 * run.work_median / run.refresh_period)} %",
            of(run.longer, run.steps),
            str(run.never_shown),
            of(run.most_late_frames, run.late_frames_to_slow_down - 1),
            str(run.first_slower_frame) if run.first_slower_frame else "never",
        ]
        for run in chosen
    ]
    header = [
        "Display",
        "Run",
        "Frames in flight",
        "Machine",
        "Work",
        "Shown longer",
        "Never shown",
        "Most late frames, of the limit",
        "Two refreshes from frame",
    ]
    return table(header, rows, frozenset({2, 4, 5, 6, 7, 8}))


def refresh_table(runs: list[Run]) -> str:
    seen: dict[tuple[int, int], tuple[list[int], list[str]]] = {}
    for run in runs:
        if run.swapchain_refresh_ns and run.gsync in ("off", ""):
            # A swap chain's report wobbles by a few tenths of a microsecond from read to read: grouped to the nearest 10 us
            reports, folders = seen.setdefault((run.refresh_period, round(run.swapchain_refresh_ns / 10_000)), ([], []))
            reports.append(run.swapchain_refresh_ns)
            if run.folder not in folders:
                folders.append(run.folder)
    rows: list[list[str]] = []
    for (period, _), (reports, folders) in sorted(seen.items()):
        reported = statistics.median_low(reports)
        rows.append(
            [
                f"{ms(period)} ms ({10_000_000 / period:.2f} Hz)",
                f"{reported / 1_000_000:.3f} ms ({1_000_000_000 / reported:.2f} Hz)",
                ", ".join(f"`{folder}`" for folder in folders),
            ]
        )
    return table(["The window's display, from the window system", "What the swap chain reported", "Folders"], rows)


def sources_table(runs: list[Run]) -> str:
    chosen = [run for run in runs if run.folder.endswith("vsync-sources") and run.pacer == "fixed" and run.hold == "vsync" and run.present_timing == 1]
    chosen.sort(key=lambda run: (run.refresh_period, -run.fps, run.vsync_source, run.load))
    rows = [
        [
            f"{run.refresh_hz} Hz",
            f"{run.fps} fps",
            {"dxgi": "DXGI vertical blank", "dwm": "Compositor time"}[run.vsync_source],
            run.load,
            f"{ms(run.start_step_median)} ms",
            of(run.exact, run.steps),
        ]
        for run in chosen
    ]
    header = ["The window's display", "Asked for", "Vsync time from", "Machine", "Frame start to frame start", "Shown for its swap interval"]
    return table(header, rows, frozenset({4, 5}))


def gsync_table(runs: list[Run]) -> str:
    chosen = [run for run in runs if run.gsync.startswith("on") and run.pacer == "fixed"]
    chosen.sort(key=lambda run: (run.gsync, -run.fps, run.hold, run.folder, run.name))
    rows = [
        [
            run.gsync.removeprefix("on for "),
            f"`{run.name}`",
            f"{run.fps} fps",
            HOLD_NAMES[run.hold],
            f"{of(run.exact, run.steps)}",
            f"{ms(run.start_step_p1, 2)} to {ms(run.start_step_p99, 2)} ms",
            of(run.feedback_refused, run.feedback_refused + run.feedback_used) if run.present_feedback else "",
            of(run.swapchain_reads_fixed, run.swapchain_reads),
        ]
        for run in chosen
    ]
    header = [
        "G-SYNC on for",
        "Run",
        "Asked for",
        "Hold",
        "Shown for its swap interval",
        "Frame start to frame start",
        "Display times refused",
        "Swap chain reports saying fixed refresh",
    ]
    return table(header, rows, frozenset({4, 5, 6, 7}))


def summary_table(runs: list[Run]) -> str:
    folders = sorted({run.folder for run in runs}, key=lambda folder: [run.folder for run in runs].index(folder))
    rows = [
        [f"`{folder}`", str(sum(1 for run in runs if run.folder == folder)), str(sum(run.frames for run in runs if run.folder == folder))] for folder in folders
    ]
    rows.append(["All", str(len(runs)), str(sum(run.frames for run in runs))])
    return table(["Folder", "Runs", "Frames logged"], rows, frozenset({1, 2}))


TABLES = {
    "runs": summary_table,
    "hold": hold_table,
    "spread": spread_table,
    "placement": placement_table,
    "work-130": work_130_table,
    "work-90": work_90_table,
    "sources": sources_table,
    "refresh": refresh_table,
    "gsync": gsync_table,
}


def update_document(text: str, runs: list[Run]) -> str:
    """The document with every generated block it has rewritten."""
    for name, make in TABLES.items():
        begin = f"<!-- pacer-capture:{name}: generated by {GENERATOR} -->"
        end = f"<!-- /pacer-capture:{name} -->"
        if begin in text:
            start = text.index(begin) + len(begin)
            stop = text.index(end, start)
            text = text[:start] + "\n\n" + make(runs) + "\n\n" + text[stop:]
    return text


# ----------------------------------------------------------------------------------------------------------------------------------------


def main() -> int:
    parser = argparse.ArgumentParser(description=(__doc__ or "").splitlines()[0])
    _ = parser.add_argument("capture", type=Path, help="the capture session's zip")
    _ = parser.add_argument("--update-doc", action="store_true", help="also rewrite the generated tables of the session's document")
    _ = parser.add_argument("--check", action="store_true", help="write nothing: fail when a file differs from what the zip gives")
    arguments = parser.parse_args(namespace=Arguments())

    with zipfile.ZipFile(arguments.capture) as archive:
        runs = read_runs(archive)
    folder = arguments.capture.with_suffix("")
    files: dict[Path, str] = {
        folder / "runs.csv": runs_csv(runs),
        folder / "hold-methods.svg": hold_chart(runs),
        folder / "frame-start-spread.svg": spread_chart(runs),
        folder / "present-placement.svg": placement_chart(runs),
        folder / "work-90-at-240hz.svg": work_chart(runs, "240hz-plain-vulkan"),
        folder / "gsync-hold.svg": gsync_chart(runs),
    }
    document = arguments.capture.with_suffix(".md")
    if (arguments.update_doc or arguments.check) and document.exists():
        files[document] = update_document(document.read_text(encoding="utf-8"), runs)

    if arguments.check:
        stale = [path for path, text in files.items() if not path.exists() or path.read_text(encoding="utf-8") != text]
        for path in stale:
            print(f"{path} is not what {arguments.capture.name} gives")
        if stale:
            print(f"Regenerate: python {GENERATOR} {arguments.capture.as_posix()} --update-doc")
            return 1
        print(f"{len(runs)} runs: {len(files)} files are what {arguments.capture.name} gives")
        return 0

    for path, text in files.items():
        path.parent.mkdir(parents=True, exist_ok=True)
        _ = path.write_text(text, encoding="utf-8", newline="\n")
    print(f"{len(runs)} runs: {len(files)} files written for {arguments.capture.name}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
