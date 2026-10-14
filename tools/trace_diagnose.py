#!/usr/bin/env python3
# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""What went wrong in a run: the facts the capture and a Perfetto trace give of each frame that went wrong (measure/doc/perfetto.md).

    uv run --group trace tools/trace_diagnose.py <capture folder> [--run ID]

<capture folder> is a capture that was analysed with a trace (it has analysis/summary.json, and run-<id>-trace.csv next to the
run's frames). The run's augmented trace (run-<id>-augmented.pftrace, written with --charts or Save charts) adds what the
application logged around each frame (part 2 of the frame record) and what ran on the CPU while the application's thread
waited for one. Without the join only the markers' own facts are given; without the augmented trace, the join's.

It writes run-<id>-diagnosis.txt (the run in words, then every frame that went wrong) and run-<id>-diagnosis.csv (a line per
frame that went wrong) next to the analysis, or into --output. They are for whoever measured: the text names the programs
that ran on the application's CPU (--no-programs leaves them out); nothing of it goes onto a report card or into a repository.
Every fact is a measurement or what the application or the system logged; none is a guess at a cause (tools/trace_diagnosis.py
says which facts there are and in what order)."""

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "sdk" / "python"))
from mb_framepacing.data import DataFormatError, find_analysis, read_frames, read_summary  # noqa: E402
from trace_alignment import (  # noqa: E402  # pyright: ignore[reportImplicitRelativeImport] (a script next to this one: its folder is on sys.path)
    literal_pattern,
    record_process,
)
from trace_augment import UsageError, pick_run  # noqa: E402  # pyright: ignore[reportImplicitRelativeImport]
from trace_diagnosis import FrameDetails, RunMoment, diagnose, frame_lines, summary_lines, to_csv  # noqa: E402  # pyright: ignore[reportImplicitRelativeImport]
from trace_events import TraceReadError  # noqa: E402  # pyright: ignore[reportImplicitRelativeImport]
from trace_frames import TraceFrame, from_csv  # noqa: E402  # pyright: ignore[reportImplicitRelativeImport]

FRAMES_SUFFIX = "-frames.csv"
NEEDS_PACKAGE = "Reading the augmented trace needs Perfetto's Python package: uv run --group trace tools/trace_diagnose.py ..."


class Arguments(argparse.Namespace):
    capture: Path = Path()
    run: int | None = None
    output: Path | None = None
    thread: str | None = None
    trace_processor: Path | None = None
    no_programs: bool = False


def read_trace(
    trace: Path, arguments: Arguments, run_id: int, joined: dict[int, TraceFrame], wrong: list[int]
) -> tuple[dict[int, FrameDetails], list[RunMoment], dict[int, list[tuple[str, int]]], list[str]]:
    """Part 2 of the frame record, the run's moments and what ran while the application's thread waited for a CPU in the frames
    that went wrong (wrong: their frame indices), with lines that say what was found."""
    from trace_source import TraceSource  # noqa: PLC0415  # pyright: ignore[reportImplicitRelativeImport]

    notes: list[str] = []
    with TraceSource(trace, arguments.trace_processor) as source:
        details = source.frame_details(run_id)
        moments = source.run_moments(run_id)
        notes.append(f"{trace.name}: part 2 of the frame record for {len(details)} frames, {len(moments)} moments of the run")
        programs: dict[int, list[tuple[str, int]]] = {}
        process = record_process(source.frame_records())
        utid = None
        if process is not None:
            threads = source.threads(literal_pattern(process))
            wanted = [t for t in threads if (t[2] == arguments.thread if arguments.thread is not None else t[3])]
            utid = wanted[0][0] if wanted else None
        if utid is None:
            notes.append("  no thread of the application found: what ran while it waited for a CPU is not given")
        else:
            for index in wrong:
                row = joined.get(index)
                if row is not None and row.cpu_runnable_ns and row.cpu_start_trace_ns is not None and row.cpu_end_trace_ns is not None:
                    programs[index] = source.waited_for_cpu(utid, row.cpu_start_trace_ns, row.cpu_end_trace_ns)
    return details, moments, programs, notes


def main() -> int:
    parser = argparse.ArgumentParser(description=(__doc__ or "").splitlines()[0])
    _ = parser.add_argument("capture", type=Path, help="a capture folder that was analysed with a trace")
    _ = parser.add_argument("--run", type=int, metavar="ID", help="the run id, for a capture with more than one run")
    _ = parser.add_argument("-o", "--output", type=Path, metavar="FOLDER", help="where to write the two files (next to the analysis)")
    _ = parser.add_argument("--thread", metavar="NAME", help="the application's thread whose waits for a CPU are looked at (the main thread)")
    _ = parser.add_argument("--trace-processor", type=Path, metavar="PATH", help="a trace_processor_shell that is on the disk already")
    _ = parser.add_argument("--no-programs", action="store_true", help="name no program that ran on the application's CPU")
    arguments = parser.parse_args(namespace=Arguments())

    try:
        analysis = find_analysis(arguments.capture)
        if analysis is None:
            raise UsageError(f"{arguments.capture} has no analysis (analysis/summary.json): analyse the capture first.")
        summary = read_summary(analysis / "summary.json")
        picked = pick_run(summary, arguments.run)
        frames = read_frames(analysis / picked.frames_file)
        prefix = picked.frames_file[: -len(FRAMES_SUFFIX)] if picked.frames_file.endswith(FRAMES_SUFFIX) else picked.frames_file
        period_ns = picked.pacing.refresh_period_ns if picked.pacing is not None else summary.capture_period_ns
        name = picked.name or picked.sequence_id or f"run {picked.run_id}"

        notes: list[str] = []
        joined_file = analysis / f"{prefix}-trace.csv"
        joined: dict[int, TraceFrame] = {}
        if joined_file.is_file():
            joined = {row.frame_index: row for row in from_csv(joined_file.read_text(encoding="utf-8"))}
            notes.append(f"{joined_file.name}: the join of {len(joined)} frames")
            notes.append("  a time against the refresh a frame was due at is on the clocks as the frames tie them: a delay every frame shares is not in it")
        else:
            notes.append(f"no {joined_file.name}: no trace was joined (analyse with --trace), so only the markers' facts are given")

        details: dict[int, FrameDetails] = {}
        moments: list[RunMoment] = []
        programs: dict[int, list[tuple[str, int]]] = {}
        trace = analysis / f"{prefix}-augmented.pftrace"
        if trace.is_file():
            first_pass = diagnose(frames, joined, {}, [], period_ns, summary.error_threshold_ns)
            wrong = [d.frame_index for d in first_pass if d.went_wrong]
            details, moments, programs, found = read_trace(trace, arguments, picked.run_id, joined, wrong)
            notes += found
        else:
            notes.append(f"no {trace.name} (written with --charts or Save charts): nothing of what the application logged around its frames")

        diagnoses = diagnose(frames, joined, details, moments, period_ns, summary.error_threshold_ns, programs)
        name_programs = not arguments.no_programs
        lines = [f"{name}: what went wrong", *notes, "", *summary_lines(diagnoses, name_programs), "", *frame_lines(diagnoses, name_programs)]
        folder = arguments.output or analysis
        folder.mkdir(parents=True, exist_ok=True)
        text_file = folder / f"{prefix}-diagnosis.txt"
        csv_file = folder / f"{prefix}-diagnosis.csv"
        _ = text_file.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
        _ = csv_file.write_text(to_csv(diagnoses), encoding="utf-8", newline="\n")
        for line in [lines[0], *notes, "", *summary_lines(diagnoses, name_programs)]:
            print(line)
        print(f"\n{text_file}\n{csv_file}")
        return 0
    except ImportError as error:
        print(f"{error}\n{NEEDS_PACKAGE}", file=sys.stderr)
        return 1
    except (UsageError, TraceReadError, DataFormatError, ValueError, OSError) as error:
        print(error, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
