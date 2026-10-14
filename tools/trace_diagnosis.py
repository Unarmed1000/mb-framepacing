# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""What the capture and a Perfetto trace say of each frame of a run that went wrong: the facts behind trace_diagnose.py,
with nothing of Perfetto in it (trace_diagnose.py reads the files and the trace).

A frame went wrong when it was late, off by more than the error threshold, off by delta time jitter, shown after frame
indices that never were, or followed by an older frame out of order. Of every frame, good or bad, the facts are worked out
in the order a frame is made in (FACT_ORDER): a wait before it ran past its time, its thread waited for a CPU, its CPU work
took longer than its intended period, its CPU work, its GPU work or its present ended after the refresh it was due at, its
present blocked or was not accepted, the system's frame timeline calls it janky, the application's side was done in time
and it was shown later all the same, it was shown sooner than its intended period after the frame before, its animation time step was off while its display time step was as intended, and frame
indices before it were never shown. A frame that went wrong is grouped by its first fact; one with none is "nothing in the
trace explains it". The good frames' facts are what the bad frames' are held against: a fact as common in good frames
explains nothing.

Every fact is a measurement, or what the application or the system logged, with its number: never a guess at a cause, and
the application's log (part 2 of the frame record) is its own word, not checked against the capture. A time beyond the error
threshold counts. Times are whole nanoseconds; on the trace's clock where they come from the trace or the join
(trace_frames.py), and the refresh a frame was due at is its display time there plus its animation error.

The strings of part 2 of the frame record (sdk/doc/trace-frame-record.md) are the SDK's FrameRecordText; the tests hold these
to its golden data."""

import csv
import io
from collections import Counter
from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from typing import Protocol

from trace_events import (  # pyright: ignore[reportImplicitRelativeImport] (a script next to this one: its folder is on sys.path)
    VERDICT_DELTA_TIME_JITTER,
    VERDICT_LATE,
    VERDICT_OFF,
    VERDICT_ON_TIME,
    Frame,
    verdict,
)
from trace_frames import TraceFrame  # pyright: ignore[reportImplicitRelativeImport]

# Part 2 of the frame record: the names' prefixes (a frame's run id and frame index follow), the moments of a run, their
# tracks' names, and the annotations read
WAIT_NAME_PREFIX = "mbfp wait "
PRESENT_NAME_PREFIX = "mbfp present "
GPU_NAME_PREFIX = "mbfp gpu "
DISPLAY_NAME_PREFIX = "mbfp display "
NOT_PRESENTED_NAME_PREFIX = "mbfp not presented "
ENVIRONMENT_NAME = "mbfp environment"
SWAP_CHAIN_RECREATED_NAME = "mbfp swap chain recreated"
SWAP_INTERVAL_CHANGE_NAME = "mbfp swap interval change"
RESULT_ACCEPTED = "accepted"
JANK_NONE = ("", "None")

# What a frame that went wrong is, beyond the verdicts of trace_events
WRONG_SKIPPED = "after frame indices never shown"
WRONG_OUT_OF_ORDER = "followed by an older frame"
WRONG_VERDICTS = (VERDICT_LATE, VERDICT_OFF, VERDICT_DELTA_TIME_JITTER)

# The facts, in the order a frame is made in: a frame that went wrong is grouped by the first it has
WAIT_PAST_TIME = "wait past its time"
WAITED_FOR_CPU = "waited for a CPU"
CPU_LONGER = "CPU work longer than intended"
CPU_PAST_DUE = "CPU work ended after its refresh"
GPU_PAST_DUE = "GPU work ended after its refresh"
PRESENT_PAST_DUE = "present ended after its refresh"
PRESENT_BLOCKED = "present blocked"
PRESENT_NOT_ACCEPTED = "present not accepted"
SYSTEM_JANK = "system calls it janky"
DONE_IN_TIME = "done in time, shown later"
SHOWN_SOONER = "shown sooner than intended"
ANIMATION_STEP = "animation time step off"
SKIPPED_BEFORE = "frame indices never shown before it"
FACT_ORDER = (
    WAIT_PAST_TIME,
    WAITED_FOR_CPU,
    CPU_LONGER,
    CPU_PAST_DUE,
    GPU_PAST_DUE,
    PRESENT_PAST_DUE,
    PRESENT_BLOCKED,
    PRESENT_NOT_ACCEPTED,
    SYSTEM_JANK,
    DONE_IN_TIME,
    SHOWN_SOONER,
    ANIMATION_STEP,
    SKIPPED_BEFORE,
)
NOTHING_FOUND = "nothing in the trace explains it"

# How long before a frame's display a run's moment is named with it
MOMENT_REACH_NS = 1_000_000_000
# A repeat is named when this many frames that went wrong, and at least half of them, are that many frames apart
REPEAT_MIN_COUNT = 3
# A burst is this many frames that went wrong in a row, or more
BURST_MIN_FRAMES = 3
# The programs named in the summary
PROGRAMS_LISTED = 8


class DiagnosedFrame(Frame, Protocol):
    """What the diagnosis reads of a presented frame: mb_framepacing.data's FrameRow has it."""

    @property
    def frame_time_ns(self) -> int | None: ...


@dataclass(frozen=True)
class Wait:
    """A wait before a frame, as the application logged it: its kind and the time it waited for, where it said them."""

    begin_ns: int
    end_ns: int
    kind: str | None = None
    planned_ns: int | None = None


@dataclass(frozen=True)
class Present:
    """A frame's present call, as the application logged it."""

    begin_ns: int
    end_ns: int
    result: str | None = None
    swap_interval: int | None = None


@dataclass(frozen=True)
class FrameDetails:
    """Part 2 of the frame record for one frame: what the application logged around it. Times on the trace's clock."""

    waits: tuple[Wait, ...] = ()
    present: Present | None = None
    gpu_end_ns: int | None = None
    display_ns: int | None = None
    not_presented: bool = False


@dataclass(frozen=True)
class RunMoment:
    """A moment of the run the application logged: its name (one of the run's moments), when, and what it said of it."""

    name: str
    time_ns: int
    text: str = ""


@dataclass(frozen=True)
class Fact:
    """One thing the capture or the trace says of a frame: its kind (FACT_ORDER), its number, and the words with the number."""

    kind: str
    value_ns: int
    text: str


@dataclass(frozen=True)
class Diagnosis:
    """A frame with what it is (a verdict, or what went wrong beyond one), its facts in FACT_ORDER, the run's moments shortly
    before it, and the programs that ran on its CPU while its thread waited for one (program, nanoseconds)."""

    frame_index: int
    what: str
    animation_error_ns: int | None
    facts: tuple[Fact, ...]
    moments: tuple[str, ...] = ()
    programs: tuple[tuple[str, int], ...] = ()

    @property
    def went_wrong(self) -> bool:
        return self.what != VERDICT_ON_TIME

    @property
    def first(self) -> str:
        return self.facts[0].kind if self.facts else NOTHING_FOUND


def ms(nanoseconds: int) -> str:
    """Nanoseconds as milliseconds in words, to the hundredth."""
    return f"{nanoseconds / 1_000_000:.2f} ms"


def what_went_wrong(frame: Frame, error_threshold_ns: int) -> str:
    """The frame's verdict where it went wrong by one (late, off, delta time jitter), else what else went wrong, else on time."""
    word = verdict(frame, error_threshold_ns)
    if word in WRONG_VERDICTS:
        return word
    if frame.skipped_before > 0 and "UncertainStart" not in frame.flags:
        return WRONG_SKIPPED
    if frame.older_frames:
        return WRONG_OUT_OF_ORDER
    return VERDICT_ON_TIME


def _facts(
    frame: DiagnosedFrame, previous: DiagnosedFrame | None, joined: TraceFrame | None, details: FrameDetails | None, period_ns: int, threshold_ns: int
) -> list[Fact]:
    facts: list[Fact] = []
    intended_ns = frame.target_ns if frame.target_ns is not None and frame.target_ns > 0 else period_ns
    # The refresh the frame was due at, on the trace's clock: where it was shown, less what it was late by
    due_ns = joined.display_trace_ns + frame.animation_error_ns if joined is not None and frame.animation_error_ns is not None else None

    if details is not None:
        late_waits = [(w.end_ns - w.planned_ns, w) for w in details.waits if w.planned_ns is not None and w.end_ns - w.planned_ns > threshold_ns]
        if late_waits:
            past, wait = max(late_waits, key=lambda item: item[0])
            facts.append(Fact(WAIT_PAST_TIME, past, f"its wait before it ({wait.kind or 'of no kind'}) ended {ms(past)} after the time it waited for"))
    if joined is not None and joined.cpu_runnable_ns is not None and joined.cpu_runnable_ns > threshold_ns:
        facts.append(Fact(WAITED_FOR_CPU, joined.cpu_runnable_ns, f"its thread waited {ms(joined.cpu_runnable_ns)} for a CPU during its CPU work"))
    if frame.cpu_busy_ns is not None and frame.cpu_busy_ns - intended_ns > threshold_ns:
        facts.append(Fact(CPU_LONGER, frame.cpu_busy_ns - intended_ns, f"its CPU work took {ms(frame.cpu_busy_ns)}, its intended period {ms(intended_ns)}"))
    if due_ns is not None:
        if joined is not None and joined.cpu_end_trace_ns is not None and joined.cpu_end_trace_ns - due_ns > threshold_ns:
            past = joined.cpu_end_trace_ns - due_ns
            facts.append(Fact(CPU_PAST_DUE, past, f"its CPU work ended {ms(past)} after the refresh it was due at"))
        if details is not None and details.gpu_end_ns is not None and details.gpu_end_ns - due_ns > threshold_ns:
            past = details.gpu_end_ns - due_ns
            facts.append(Fact(GPU_PAST_DUE, past, f"its GPU work ended {ms(past)} after the refresh it was due at (the application's log)"))
        if details is not None and details.present is not None and details.present.end_ns - due_ns > threshold_ns:
            past = details.present.end_ns - due_ns
            facts.append(Fact(PRESENT_PAST_DUE, past, f"its present returned {ms(past)} after the refresh it was due at (the application's log)"))
    if details is not None and details.present is not None:
        blocked = details.present.end_ns - details.present.begin_ns
        if blocked * 2 > period_ns:
            facts.append(Fact(PRESENT_BLOCKED, blocked, f"its present call took {ms(blocked)} (the application's log)"))
        if details.present.result is not None and details.present.result != RESULT_ACCEPTED:
            facts.append(Fact(PRESENT_NOT_ACCEPTED, 0, f"its present's result was '{details.present.result}' (the application's log)"))
    if joined is not None and joined.jank_type is not None and joined.jank_type not in JANK_NONE:
        facts.append(Fact(SYSTEM_JANK, 0, f"the system's frame timeline says '{joined.jank_type}' ({joined.present_type or 'no present type'})"))
    if due_ns is not None and frame.animation_error_ns is not None and frame.animation_error_ns < -threshold_ns and joined is not None:
        # Every end the application's side has is in time, and the frame was shown later all the same
        ends = [joined.cpu_end_trace_ns]
        if details is not None:
            ends += [details.gpu_end_ns, details.present.end_ns if details.present is not None else None]
        known = [end for end in ends if end is not None]
        if known and max(known) <= due_ns:
            ahead = due_ns - max(known)
            facts.append(Fact(DONE_IN_TIME, ahead, f"what the trace has of the application's side was done {ms(ahead)} before the refresh it was due at"))
    if frame.animation_error_ns is not None and abs(frame.animation_error_ns) > threshold_ns and frame.display_delta_ns is not None:
        if intended_ns - frame.display_delta_ns > threshold_ns:
            sooner = intended_ns - frame.display_delta_ns
            text = f"it was shown {ms(frame.display_delta_ns)} after the frame before, {ms(sooner)} sooner than its intended period"
            facts.append(Fact(SHOWN_SOONER, sooner, text))
        refreshes = (frame.display_delta_ns + period_ns // 2) // period_ns if period_ns > 0 else 0
        intended_refreshes = (intended_ns + period_ns // 2) // period_ns if period_ns > 0 else 0
        if refreshes == intended_refreshes and frame.animation_delta_ns is not None:
            text = f"its display time step was {refreshes} refresh{'es' if refreshes != 1 else ''} as intended, its animation time step {ms(frame.animation_delta_ns)}"
            if previous is not None and previous.frame_time_ns is not None:
                text += f", its CPU start {ms(previous.frame_time_ns)} after the frame before's"
            facts.append(Fact(ANIMATION_STEP, frame.animation_error_ns, text))
    if frame.skipped_before > 0:
        text = f"{frame.skipped_before} frame ind{'ices' if frame.skipped_before != 1 else 'ex'} before it never shown"
        facts.append(Fact(SKIPPED_BEFORE, frame.skipped_before, text))
    return facts


def _moment_text(moment: RunMoment) -> str:
    if moment.name == SWAP_CHAIN_RECREATED_NAME:
        return f"the swap chain was made anew{f' ({moment.text})' if moment.text else ''}"
    if moment.name == SWAP_INTERVAL_CHANGE_NAME:
        return f"the swap interval changed{f' ({moment.text})' if moment.text else ''}"
    return "the environment changed"


def diagnose(
    frames: Sequence[DiagnosedFrame],
    joined: Mapping[int, TraceFrame],
    details: Mapping[int, FrameDetails],
    moments: Sequence[RunMoment],
    period_ns: int,
    error_threshold_ns: int,
    programs: Mapping[int, Sequence[tuple[str, int]]] | None = None,
) -> list[Diagnosis]:
    """A diagnosis per frame, in the frames' order. joined, details and programs are by frame index (a frame without one has
    nothing of it); moments are the run's, ascending by time."""
    waited = programs or {}
    diagnoses: list[Diagnosis] = []
    previous: DiagnosedFrame | None = None
    for frame in frames:
        row = joined.get(frame.frame_index)
        facts = _facts(frame, previous, row, details.get(frame.frame_index), period_ns, error_threshold_ns)
        facts.sort(key=lambda fact: FACT_ORDER.index(fact.kind))
        near: list[str] = []
        if row is not None:
            for moment in moments:
                if 0 <= row.display_trace_ns - moment.time_ns <= MOMENT_REACH_NS:
                    near.append(f"{_moment_text(moment)} {ms(row.display_trace_ns - moment.time_ns)} before it was shown")
        diagnoses.append(
            Diagnosis(
                frame.frame_index,
                what_went_wrong(frame, error_threshold_ns),
                frame.animation_error_ns,
                tuple(facts),
                tuple(near),
                tuple(waited.get(frame.frame_index, ())),
            )
        )
        previous = frame
    return diagnoses


def _share(count: int, total: int) -> str:
    return f"{count} of {total} ({(100 * count + total // 2) // total if total else 0} %)"


def _repeat(indices: Sequence[int]) -> tuple[int, int] | None:
    """The distance most frames that went wrong are apart, and how many are, when that is a repeat."""
    gaps = Counter(later - earlier for earlier, later in zip(indices, indices[1:], strict=False))
    if not gaps:
        return None
    gap, count = max(gaps.items(), key=lambda item: (item[1], -item[0]))
    return (gap, count) if gap > 1 and count >= REPEAT_MIN_COUNT and count * 2 >= len(indices) - 1 else None


def _bursts(indices: Sequence[int]) -> list[tuple[int, int]]:
    """The stretches of frames that went wrong one after the other: first frame index and length."""
    bursts: list[tuple[int, int]] = []
    start = 0
    for position in range(1, len(indices) + 1):
        if position == len(indices) or indices[position] != indices[position - 1] + 1:
            if position - start >= BURST_MIN_FRAMES:
                bursts.append((indices[start], position - start))
            start = position
    return bursts


def summary_lines(diagnoses: Sequence[Diagnosis], name_programs: bool) -> list[str]:
    """The run in words: what went wrong, what came first, what each fact comes with in good frames and bad, repeats and
    bursts, the run's moments, and (name_programs) which programs ran while the application's thread waited for a CPU."""
    wrong = [d for d in diagnoses if d.went_wrong]
    good = [d for d in diagnoses if not d.went_wrong]
    kinds = Counter(d.what for d in wrong)
    lines = [
        f"{len(diagnoses)} frames, {len(wrong)} of them went wrong"
        + (": " + ", ".join(f"{count} {what}" for what, count in kinds.most_common()) if wrong else "")
    ]
    if not wrong:
        return lines
    lines.append("")
    lines.append("What came first in each frame that went wrong:")
    for what, _ in kinds.most_common():
        firsts = Counter(d.first for d in wrong if d.what == what)
        lines.append(f"  {what}:")
        for first, count in sorted(firsts.items(), key=lambda item: (-item[1], FACT_ORDER.index(item[0]) if item[0] in FACT_ORDER else len(FACT_ORDER))):
            lines.append(f"    {count:5}  {first}")
    lines.append("")
    lines.append("What each fact comes with: frames that went wrong, and frames on time")
    for kind in FACT_ORDER:
        in_wrong = sum(1 for d in wrong if any(f.kind == kind for f in d.facts))
        in_good = sum(1 for d in good if any(f.kind == kind for f in d.facts))
        if in_wrong or in_good:
            lines.append(f"  {kind}: {_share(in_wrong, len(wrong))}, on time {_share(in_good, len(good))}")
    indices = [d.frame_index for d in wrong]
    repeat = _repeat(indices)
    bursts = _bursts(indices)
    if repeat is not None or bursts:
        lines.append("")
        if repeat is not None:
            lines.append(f"Repeats: {repeat[1]} times {repeat[0]} frame indices after the one before that went wrong")
        if bursts:
            first, length = max(bursts, key=lambda burst: (burst[1], -burst[0]))
            lines.append(f"Bursts: {len(bursts)} of {BURST_MIN_FRAMES} or more frames in a row, the longest {length} from #{first}")
    near = [d for d in wrong if d.moments]
    if near:
        lines.append("")
        lines.append(f"Shortly after a moment of the run (within {ms(MOMENT_REACH_NS)}): {len(near)} frames that went wrong")
    if name_programs:
        totals: Counter[str] = Counter()
        for d in wrong:
            for program, nanoseconds in d.programs:
                totals[program] += nanoseconds
        if totals:
            lines.append("")
            lines.append("What ran on the CPU while the application's thread waited for it, in the frames that went wrong:")
            for program, nanoseconds in totals.most_common(PROGRAMS_LISTED):
                lines.append(f"  {ms(nanoseconds):>12}  {program}")
    return lines


def frame_lines(diagnoses: Sequence[Diagnosis], name_programs: bool) -> list[str]:
    """Every frame that went wrong, with its facts in order, the run's moments before it and (name_programs) what ran on its CPU."""
    lines: list[str] = []
    for d in diagnoses:
        if not d.went_wrong:
            continue
        error = f", animation error {ms(d.animation_error_ns)}" if d.animation_error_ns is not None else ""
        lines.append(f"#{d.frame_index} {d.what}{error}")
        if not d.facts:
            lines.append(f"  {NOTHING_FOUND}")
        lines += [f"  {fact.text}" for fact in d.facts]
        lines += [f"  {moment}" for moment in d.moments]
        if name_programs and d.programs:
            lines.append("  while it waited for a CPU: " + ", ".join(f"{program} {ms(nanoseconds)}" for program, nanoseconds in d.programs))
    return lines


# The CSV's columns: a frame that went wrong per line
COLUMNS = ("frameIndex", "what", "animationErrorNs", "first", "facts")


def to_csv(diagnoses: Sequence[Diagnosis]) -> str:
    """The frames that went wrong as CSV text with a header line, lines ending in a line feed: the facts as kind=number,
    separated by '|', in their order (numbers in nanoseconds, a count for frame indices)."""
    text = io.StringIO()
    writer = csv.writer(text, lineterminator="\n")
    writer.writerow(COLUMNS)
    for d in diagnoses:
        if d.went_wrong:
            facts = "|".join(f"{fact.kind}={fact.value_ns}" for fact in d.facts)
            writer.writerow([d.frame_index, d.what, "" if d.animation_error_ns is None else d.animation_error_ns, d.first, facts])
    return text.getvalue()
