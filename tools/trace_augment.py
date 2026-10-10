#!/usr/bin/env python3
# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""EXPERIMENTAL: join a capture's analysis with a Perfetto trace of the same run (measure/doc/perfetto.md).

The capture's measurements into the trace, and what the trace says into the capture's frames:

    uv run --group trace tools/trace_augment.py <capture folder> <trace> -o augmented.pftrace    # the trace with the capture's tracks added
    uv run --group trace tools/trace_augment.py <capture folder> <trace> --frames frames.csv     # a line per frame with what the trace says of it
    uv run --group trace tools/trace_augment.py <capture folder> -o run.pftrace                  # the capture alone as a trace
    uv run --group trace tools/trace_augment.py --inspect <trace>                                # what a trace has to go by

<capture folder> is a capture that was analysed (it has analysis/summary.json), <trace> a Perfetto protobuf trace recorded on
the device while the run was captured. Nothing is uploaded and neither input is changed. A trace names the device and its
programs: an augmented trace holds everything the recorded one did.

The trace has to have the frame record the application logs (sdk/doc/trace-frame-record.md: a slice per frame named
'mbfp frame <run id> <frame index>'): the frames are placed by it, it says which process is the application, and without it
nothing is joined. The capture's clock and the application's are tied together by the frames only (tools/trace_alignment.py
says how, and what that can and can not claim).

It needs Perfetto's Python package (the dependency group 'trace' of pyproject.toml), which reads a trace through Perfetto's
trace processor: a native program the package downloads on first use, or the one --trace-processor names."""

import argparse
import dataclasses
import sys
from collections.abc import Sequence
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "sdk" / "python"))
from mb_framepacing.data import (  # noqa: E402
    AnalysisSummary,
    CaptureCsvRow,
    DataFormatError,
    FrameRow,
    SummaryRun,
    find_analysis,
    read_captures,
    read_frames,
    read_summary,
)
from trace_alignment import (  # noqa: E402  # pyright: ignore[reportImplicitRelativeImport] (a script next to this one: its folder is on sys.path)
    RECORD_CLOCK_ID,
    RECORD_NAME_PREFIX,
    SYSTEM_CLOCK_NAMES,
    Alignment,
    AlignmentError,
    Anchor,
    ClockSnapshot,
    align,
    capture_minus_app,
    check_inside_trace,
    fit_vblanks,
    from_trace_ns,
    literal_pattern,
    record_offset,
    record_process,
    to_trace_ns,
)
from trace_events import TraceFormatError, TraceReadError, Track, in_stretch, track_events  # noqa: E402  # pyright: ignore[reportImplicitRelativeImport]
from trace_frames import join, summary_lines, to_csv  # noqa: E402  # pyright: ignore[reportImplicitRelativeImport]

NS_PER_SECOND = 1_000_000_000
NEEDS_PACKAGE = "This needs Perfetto's Python package: uv run --group trace tools/trace_augment.py ..."


class Arguments(argparse.Namespace):
    capture: Path | None = None
    trace: Path | None = None
    inspect: Path | None = None
    output: Path | None = None
    frames: Path | None = None
    run: int | None = None
    from_seconds: float | None = None
    to_seconds: float | None = None
    capture_offset_ns: int | None = None
    process: str | None = None
    layer: str = "*"
    thread: str | None = None
    vblanks: str | None = None
    trace_processor: Path | None = None
    no_check: bool = False


class UsageError(ValueError):
    """What was asked for can not be done with what was given. The message says what to give."""


@dataclasses.dataclass(frozen=True)
class Run:
    """The run of a capture that is worked on: its frames (the stretch asked for), the capture's rows and its refresh period."""

    run_id: int
    name: str
    key: str
    frames: list[FrameRow]
    captures: list[CaptureCsvRow]
    period_ns: int


def pick_run(summary: AnalysisSummary, run_id: int | None) -> SummaryRun:
    runs = list(summary.runs)
    if not runs:
        raise UsageError("The analysis has no run: nothing to place.")
    if run_id is None:
        if len(runs) > 1:
            listed = ", ".join(str(run.run_id) for run in runs)
            raise UsageError(f"The capture has {len(runs)} runs ({listed}): say which with --run.")
        return runs[0]
    for run in runs:
        if run.run_id == run_id:
            return run
    raise UsageError(f"The capture has no run {run_id}.")


def read_run(capture: Path, run_id: int | None, from_seconds: float | None, to_seconds: float | None) -> Run:
    analysis = find_analysis(capture)
    if analysis is None:
        raise UsageError(f"{capture} has no analysis (analysis/summary.json): analyse the capture first.")
    summary = read_summary(analysis / "summary.json")
    picked = pick_run(summary, run_id)
    frames = read_frames(analysis / picked.frames_file)
    if frames and (from_seconds is not None or to_seconds is not None):
        first = frames[0].first_seen_ns
        from_ns = first + round(from_seconds * NS_PER_SECOND) if from_seconds is not None else None
        to_ns = first + round(to_seconds * NS_PER_SECOND) if to_seconds is not None else None
        frames = in_stretch(frames, from_ns, to_ns)
    if not frames:
        raise UsageError("The run has no frame in the stretch asked for.")
    captures_file = analysis / "captures.csv"
    captures = read_captures(captures_file) if captures_file.exists() else []
    period = picked.pacing.refresh_period_ns if picked.pacing is not None else summary.capture_period_ns
    name = picked.name or picked.sequence_id or f"run {picked.run_id}"
    return Run(picked.run_id, name, f"{picked.run_id}/{picked.sequence_id or ''}", frames, captures, period)


def write_tracks(
    output: Path, source: Path | None, run: Run, alignment: Alignment, snapshots: Sequence[ClockSnapshot], attributes: dict[str, int | str]
) -> "dict[str, int]":
    """Writes the trace: the run's tracks, on the trace's own clock by its snapshots, and then what the recorded trace holds,
    when there is one. Returns what was written per track name, slices and moments in one number and counter values in
    another, under 'slices <name>' and 'values <name>'."""
    from trace_tracks import TrackWriter, copy_source  # noqa: PLC0415  # pyright: ignore[reportImplicitRelativeImport]

    # Written next to the output and moved over it when it is whole: a file that could not be finished is not left behind
    partial = output.with_name(output.name + ".partial")
    try:
        with partial.open("wb") as file:
            writer = TrackWriter(file, run.key, run.name, lambda time_ns: to_trace_ns(time_ns, snapshots))
            writer.write_attributes(attributes)
            for track in Track:
                writer.write_track(track, track_events(track, run.frames, run.captures, alignment, run.period_ns))
            if source is not None:
                copy_source(source, file)
        _ = partial.replace(output)
    finally:
        partial.unlink(missing_ok=True)
    written: dict[str, int] = {}
    for track in Track:
        slices = writer.written.slices.get(track, 0) + writer.written.instants.get(track, 0)
        if slices:
            written[f"slices {track.value}"] = slices
        if writer.written.counters.get(track, 0):
            written[f"values {track.value}"] = writer.written.counters[track]
    return written


def check_output(output: Path, run_name: str, written: dict[str, int], errors_before: dict[str, int], arguments: Arguments) -> list[str]:
    """Reads the trace that was written back, and says what is not as it was written: a track that has fewer events than were
    written, and errors the trace processor counts that it did not count in the recorded trace."""
    from trace_source import TraceSource  # noqa: PLC0415  # pyright: ignore[reportImplicitRelativeImport]
    from trace_tracks import root_name  # noqa: PLC0415  # pyright: ignore[reportImplicitRelativeImport]

    problems: list[str] = []
    with TraceSource(output, arguments.trace_processor) as result:
        slices, values = result.added(root_name(run_name))
        errors = result.errors()
    for key, count in written.items():
        kind, _, name = key.partition(" ")
        found = (slices if kind == "slices" else values).get(name, 0)
        if found != count:
            problems.append(f"the track '{name}' has {found} of the {count} {kind} that were written")
    for name, count in errors.items():
        if count > errors_before.get(name, 0):
            problems.append(f"the trace processor counts {count} '{name}' where the recorded trace had {errors_before.get(name, 0)}")
    return problems


def inspect(arguments: Arguments, trace: Path) -> int:
    from trace_source import TraceSource  # noqa: PLC0415  # pyright: ignore[reportImplicitRelativeImport]

    with TraceSource(trace, arguments.trace_processor) as source:
        first, last = source.trace_range()
        print(f"{trace.name}: {first} to {last} ns on the trace's clock ({(last - first) / NS_PER_SECOND:.3f} s)")
        print("Clocks the trace has snapshots of (the frame record is on the monotonic clock):")
        for clock_id, clock_name, snapshots in source.clocks():
            print(f"  {clock_id:>3} {SYSTEM_CLOCK_NAMES.get(clock_id, clock_name or '')} ({snapshots} snapshots)")
        print("Vertical blanks (--vblanks):")
        for vblank_source in source.vblank_sources():
            print(f"  {vblank_source.name} ({vblank_source.kind}, {vblank_source.count})")
        pattern = arguments.process or "*"
        processes = source.processes(pattern)
        print(f"Processes that fit '{pattern}' (--process): {len(processes)}")
        for process in processes[:40]:
            print(f"  {process.name} (pid {process.pid})")
        layers = source.layers(pattern)
        print(f"Frame timeline layers of those (--layer): {len(layers)}")
        for process_name, layer, count in layers[:40]:
            print(f"  {process_name}: '{layer}' ({count} frames)")
        if arguments.process is not None:
            print("Threads of those (--thread):")
            for _, process_name, thread, is_main in source.threads(pattern)[:60]:
                print(f"  {process_name}: '{thread}'{' (main)' if is_main else ''}")
        print(f"The application's frame record (slices named '{RECORD_NAME_PREFIX}<run id> <frame index>'): {len(source.frame_records())}")
        errors = source.errors()
        print(f"Errors the trace processor counts: {sum(errors.values())}")
        for name, count in errors.items():
            print(f"  {name}: {count}")
    return 0


def capture_alone(arguments: Arguments, run: Run) -> int:
    """The capture's tracks as a trace of their own, on the capture's clock."""
    if arguments.frames is not None:
        raise UsageError("--frames is what a trace says of the frames: give the trace.")
    if arguments.output is None:
        raise UsageError("Nothing to do: give a trace, or -o for the capture alone as a trace.")
    anchor, offset = capture_minus_app(run.frames)
    alignment = Alignment(anchor, offset, app_clock_offset_ns=offset)
    attributes: dict[str, int | str] = {
        "run": run.name,
        "frames": len(run.frames),
        "clock": "the capture's, from its start",
        "the markers' times placed by": anchor.value,
        "capture clock minus application clock (ns)": offset,
    }
    written = write_tracks(arguments.output, None, run, alignment, [], attributes)
    print(f"{arguments.output}: {len(run.frames)} frames of {run.name} as a trace of their own ({sum(written.values())} events)")
    return 0


def pick_vblanks(arguments: Arguments, sources: Sequence[object]) -> object | None:
    """The source of vertical blanks to use: the one named, none when 'none' is named, and else the kernel's with the most."""
    from trace_source import VBlankSource  # noqa: PLC0415  # pyright: ignore[reportImplicitRelativeImport]

    known = [source for source in sources if isinstance(source, VBlankSource)]
    if arguments.vblanks == "none":
        return None
    if arguments.vblanks is not None:
        for source in known:
            if source.name == arguments.vblanks:
                return source
        listed = ", ".join(source.name for source in known) or "none"
        raise UsageError(f"The trace has no vertical blanks named '{arguments.vblanks}' (it has: {listed}).")
    kernel = [source for source in known if not source.is_counter]
    return max(kernel, key=lambda source: source.count) if kernel else None


def with_trace(arguments: Arguments, run: Run, trace: Path) -> int:
    from trace_source import TraceSource, VBlankSource  # noqa: PLC0415  # pyright: ignore[reportImplicitRelativeImport]

    if arguments.output is None and arguments.frames is None:
        raise UsageError("Nothing to do: give -o for the trace with the capture's tracks, --frames for what the trace says of each frame, or both.")
    with TraceSource(trace, arguments.trace_processor) as source:
        snapshots: list[ClockSnapshot] = source.clock_snapshots(RECORD_CLOCK_ID)
        # The frame record says where the run's frames are on the trace's clock: without it nothing is joined
        records = [dataclasses.replace(record, start_ns=from_trace_ns(record.start_ns, snapshots)) for record in source.frame_records()]
        record_offset_ns, named = record_offset(run.frames, records, run.run_id)
        alignment = align(run.frames, record_offset_ns, arguments.capture_offset_ns)
        if alignment.anchor is Anchor.NOTHING:
            raise UsageError(
                "The markers of this run carry no intended display time and no CPU busy time, so nothing ties the capture's clock "
                + "to the application's: give the offset with --capture-offset-ns (the capture's clock minus the application's)."
            )
        # The application: the process that was named, else the one its frame record is on
        process = arguments.process
        if process is None and (on := record_process(records)) is not None:
            process = literal_pattern(on)
        trace_range = source.trace_range()
        check_inside_trace(run.frames, alignment, snapshots, trace_range)
        errors_before = source.errors()

        print(f"{run.name}: {len(run.frames)} frames, placed by {alignment.anchor.value}")
        print(f"  capture clock minus application clock: {alignment.capture_minus_app_ns} ns")
        print(f"  frame record minus the markers' CPU start times: {record_offset_ns} ns, by {named} frames")
        if not snapshots:
            print("  the trace has no snapshot of the monotonic clock: its own time is taken to be that clock")

        picked = pick_vblanks(arguments, source.vblank_sources())
        vblanks: list[int] = []
        vblank_note = "none used"
        if isinstance(picked, VBlankSource):
            vblanks = source.vblanks(picked)
            displays = [to_trace_ns(alignment.display_time(frame.first_seen_ns), snapshots) for frame in run.frames]
            fit = fit_vblanks(displays, vblanks, run.period_ns)
            if fit is None:
                print(f"  vertical blanks of {picked.name}: too few of them lie in the run, none used")
                vblanks = []
            else:
                alignment = dataclasses.replace(alignment, vblank_shift_ns=fit.shift_ns)
                vblank_note = f"{picked.name}, display times moved by {-fit.shift_ns} ns"
                print(f"  vertical blanks of {picked.name}: display times moved by {-fit.shift_ns} ns onto them")
                print(f"    {fit.used_frames} of {len(displays)} frames then have one within half a refresh, the furthest {fit.spread_ns} ns from it")
        else:
            print("  vertical blanks: none used (--inspect lists what the trace has, --vblanks names one)")

        rows = None
        if arguments.frames is not None:
            surface_frames = source.surface_frames(process, arguments.layer) if process is not None else []
            presents = source.display_presents() if surface_frames else {}
            states = []
            if process is not None:
                threads = source.threads(process)
                wanted = [t for t in threads if (t[2] == arguments.thread if arguments.thread is not None else t[3])]
                if arguments.thread is not None and not wanted:
                    raise UsageError(f"No thread named '{arguments.thread}' in the processes that fit '{arguments.process or 'the application'}'.")
                states = source.thread_states(wanted[0][0]) if wanted else []
            rows = join(run.frames, alignment, snapshots, run.period_ns, vblanks, surface_frames, presents, states)

    if rows is not None and arguments.frames is not None:
        _ = arguments.frames.write_text(to_csv(rows), encoding="utf-8", newline="\n")
        print(f"{arguments.frames}:")
        for line in summary_lines(rows):
            print(f"  {line}")
        if process is None:
            print("  (the frame record is on no process's track: --process names the application, for the system's account and the CPU's time)")

    if arguments.output is not None:
        attributes: dict[str, int | str] = {
            "run": run.name,
            "frames": len(run.frames),
            "placed by": alignment.anchor.value,
            "capture clock minus application clock (ns)": alignment.capture_minus_app_ns,
            "application clock": SYSTEM_CLOCK_NAMES[RECORD_CLOCK_ID],
            "application clock offset (ns)": alignment.app_clock_offset_ns,
            "vertical blanks": vblank_note,
        }
        written = write_tracks(arguments.output, trace, run, alignment, snapshots, attributes)
        print(f"{arguments.output}: {trace.name} with {sum(written.values())} events of {len(run.frames)} frames added")
        if not arguments.no_check:
            problems = check_output(arguments.output, run.name, written, errors_before, arguments)
            if problems:
                print("The trace that was written does not read back as written:", file=sys.stderr)
                for problem in problems:
                    print(f"  {problem}", file=sys.stderr)
                return 1
            print("  read back: every track has what was written, and no new error is counted")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=(__doc__ or "").splitlines()[0])
    _ = parser.add_argument("capture", type=Path, nargs="?", help="a capture folder that was analysed")
    _ = parser.add_argument("trace", type=Path, nargs="?", help="a Perfetto protobuf trace of the same run")
    _ = parser.add_argument("--inspect", type=Path, metavar="TRACE", help="list what a trace has to go by, and do nothing else")
    _ = parser.add_argument("-o", "--output", type=Path, help="write the trace with the capture's tracks added (or the capture alone as a trace)")
    _ = parser.add_argument("--frames", type=Path, metavar="CSV", help="write a line per presented frame with what the trace says of it")
    _ = parser.add_argument("--run", type=int, metavar="ID", help="the run id, for a capture with more than one run")
    _ = parser.add_argument("--from", dest="from_seconds", type=float, metavar="SECONDS", help="from this long after the run's first frame")
    _ = parser.add_argument("--to", dest="to_seconds", type=float, metavar="SECONDS", help="up to this long after the run's first frame")
    _ = parser.add_argument("--capture-offset-ns", type=int, metavar="NS", help="the capture's clock minus the application's, when it is known")
    _ = parser.add_argument("--process", metavar="GLOB", help="the application's process name in the trace (the process the frame record is on)")
    _ = parser.add_argument("--layer", default="*", metavar="GLOB", help="the application's layer in the frame timeline (every layer)")
    _ = parser.add_argument("--thread", metavar="NAME", help="the thread whose CPU time is counted (the main thread)")
    _ = parser.add_argument("--vblanks", metavar="NAME", help="the trace's vertical blanks to use, or 'none' (the kernel's events, when it has them)")
    _ = parser.add_argument("--trace-processor", type=Path, metavar="PATH", help="a trace_processor_shell that is on the disk already")
    _ = parser.add_argument("--no-check", action="store_true", help="do not read the written trace back")
    arguments = parser.parse_args(namespace=Arguments())

    try:
        if arguments.inspect is not None:
            return inspect(arguments, arguments.inspect)
        if arguments.capture is None:
            parser.error("give a capture folder, or --inspect with a trace")
        run = read_run(arguments.capture, arguments.run, arguments.from_seconds, arguments.to_seconds)
        if arguments.trace is None:
            return capture_alone(arguments, run)
        return with_trace(arguments, run, arguments.trace)
    except ImportError as error:
        print(f"{error}\n{NEEDS_PACKAGE}", file=sys.stderr)
        return 1
    except (UsageError, AlignmentError, TraceFormatError, TraceReadError, DataFormatError, OSError) as error:
        print(error, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
