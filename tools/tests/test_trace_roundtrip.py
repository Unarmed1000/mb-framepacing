# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
# pyright: basic
"""trace_augment.py from end to end: the golden capture with a trace that the test makes to fit it (synthetic_trace), written
and read back with Perfetto's own package and trace processor.

These tests need the dependency group 'trace' and the trace processor, a native program the package downloads on first use.
Where either can not be had they are skipped, and they fail instead where MB_REQUIRE_TRACE_PROCESSOR is set (CI)."""

import csv
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from trace_alignment import align
from trace_events import Track, counters, cpu, frame_events, intended, on_screen

from .golden_capture import CAPTURE, PERIOD_NS, frames

TOOL = Path(__file__).resolve().parent.parent / "trace_augment.py"
REQUIRED = os.environ.get("MB_REQUIRE_TRACE_PROCESSOR", "") not in ("", "0")

try:
    from trace_source import TraceSource, quoted
    from trace_tracks import root_name

    from . import synthetic_trace

    UNAVAILABLE = ""
except ImportError as error:
    UNAVAILABLE = f"Perfetto's Python package is not installed ({error}): uv run --group trace python -m unittest ..."

RUN_NAME = "busy-full-rate"


def run_tool(*arguments: str | Path | int) -> subprocess.CompletedProcess[str]:
    return subprocess.run([sys.executable, str(TOOL), *[str(a) for a in arguments]], capture_output=True, text=True, check=False)


class RoundTripTests(unittest.TestCase):
    folder: tempfile.TemporaryDirectory[str]
    work: Path
    trace: Path

    @classmethod
    def setUpClass(cls) -> None:
        if UNAVAILABLE:
            if REQUIRED:
                raise AssertionError(UNAVAILABLE)
            raise unittest.SkipTest(UNAVAILABLE)
        cls.folder = tempfile.TemporaryDirectory()
        cls.work = Path(cls.folder.name)
        cls.trace = cls.work / "device.pftrace"
        synthetic_trace.write(cls.trace)
        try:
            with TraceSource(cls.trace) as source:
                _ = source.trace_range()
        except Exception as error:
            cls.folder.cleanup()
            if REQUIRED:
                raise
            raise unittest.SkipTest(f"Perfetto's trace processor could not be had or started: {error}") from error

    @classmethod
    def tearDownClass(cls) -> None:
        cls.folder.cleanup()

    def augmented(self, name: str = "augmented.pftrace") -> Path:
        output = self.work / name
        if not output.exists():
            done = run_tool(CAPTURE, self.trace, "-o", output, "--app-clock-offset-ns", synthetic_trace.APP_CLOCK_OFFSET_NS)
            self.assertEqual(done.returncode, 0, done.stdout + done.stderr)
            self.assertIn("read back: every track has what was written", done.stdout)
        return output

    def expected_alignment(self):
        golden = frames()
        alignment = align(golden, synthetic_trace.APP_CLOCK_OFFSET_NS)
        # The trace's vertical blanks are before the refreshes the pacer aimed at
        return alignment.__class__(alignment.anchor, alignment.capture_minus_app_ns, alignment.app_clock_offset_ns, synthetic_trace.VBLANK_BEFORE_INTENDED_NS)

    def test_the_synthetic_trace_has_what_it_was_made_with(self) -> None:
        done = run_tool("--inspect", self.trace, "--process", synthetic_trace.APP_NAME)
        self.assertEqual(done.returncode, 0, done.stdout + done.stderr)
        self.assertIn("vblank-0 (kernel", done.stdout)
        self.assertIn(synthetic_trace.APP_NAME, done.stdout)
        self.assertIn(synthetic_trace.APP_LAYER, done.stdout)
        self.assertIn("monotonic", done.stdout)
        self.assertIn("Errors the trace processor counts: 0", done.stdout)

    def test_every_track_is_where_the_frames_say(self) -> None:
        golden = frames()
        alignment = self.expected_alignment()
        boot = synthetic_trace.BOOT_MINUS_MONOTONIC_NS
        with TraceSource(self.augmented()) as source:
            root = root_name(RUN_NAME)

            def slices(track: Track) -> list[tuple[int, int, str]]:
                rows = source._rows(
                    "SELECT s.ts AS ts, s.dur AS dur, s.name AS name FROM slice s JOIN track t ON s.track_id = t.id "
                    + f"WHERE t.name = {quoted(track.value)} AND t.parent_id IN (SELECT id FROM track WHERE name = {quoted(root)}) ORDER BY s.ts, s.id"
                )
                return [(int(row.ts), int(row.dur), str(row.name)) for row in rows]

            def values(track: Track) -> list[tuple[int, float]]:
                rows = source._rows(
                    "SELECT c.ts AS ts, c.value AS value FROM counter c JOIN counter_track t ON c.track_id = t.id "
                    + f"WHERE t.name = {quoted(track.value)} AND t.parent_id IN (SELECT id FROM track WHERE name = {quoted(root)}) ORDER BY c.ts, c.id"
                )
                return [(int(row.ts), float(row.value)) for row in rows]

            self.assertEqual(slices(Track.ON_SCREEN), [(s.start_ns + boot, s.end_ns - s.start_ns, s.name) for s in on_screen(golden, alignment, PERIOD_NS)])
            self.assertEqual(slices(Track.INTENDED), [(s.start_ns + boot, s.end_ns - s.start_ns, s.name) for s in intended(golden, alignment)])
            self.assertEqual(slices(Track.CPU), [(s.start_ns + boot, s.end_ns - s.start_ns, s.name) for s in cpu(golden, alignment)])
            self.assertEqual(slices(Track.FRAME_EVENTS), [(e.time_ns + boot, 0, e.name) for e in frame_events(golden, alignment)])
            expected = list(counters(golden, alignment))
            for track in (Track.ANIMATION_ERROR, Track.DISPLAY_STEP, Track.LATENESS):
                self.assertEqual(values(track), [(c.time_ns + boot, c.value) for c in expected if c.track is track], track)

            # A slice's values are its arguments, and a frame's CPU slice is joined to its slice on screen
            late = [f for f in golden if "Late" in f.flags][0]
            row = source._rows(
                "SELECT EXTRACT_ARG(s.arg_set_id, 'debug.frame index') AS frame, EXTRACT_ARG(s.arg_set_id, 'debug.flags') AS flags, "
                + "EXTRACT_ARG(s.arg_set_id, 'debug.lateness (ms)') AS lateness FROM slice s JOIN track t ON s.track_id = t.id "
                + f"WHERE t.name = {quoted(Track.ON_SCREEN.value)} AND s.name = '#{late.frame_index}'"
            )[0]
            self.assertEqual(int(row.frame), late.frame_index)
            self.assertIn("Late", str(row.flags))
            self.assertEqual(float(row.lateness), (late.lateness_ns or 0) / 1_000_000)
            flows = source._rows(
                "SELECT COUNT(*) AS count FROM flow f JOIN slice a ON f.slice_out = a.id JOIN slice b ON f.slice_in = b.id "
                + "JOIN track ta ON a.track_id = ta.id JOIN track tb ON b.track_id = tb.id "
                + f"WHERE ta.name = {quoted(Track.CPU.value)} AND tb.name = {quoted(Track.ON_SCREEN.value)} AND a.name = b.name"
            )[0]
            self.assertEqual(int(flows.count), len(list(cpu(golden, alignment))))
            self.assertEqual(source.errors(), {})

    def test_what_the_recorded_trace_held_is_still_there(self) -> None:
        queries = {
            "frame timeline": "SELECT COUNT(*) AS count FROM actual_frame_timeline_slice",
            "scheduling": "SELECT COUNT(*) AS count FROM sched_slice",
            "vertical blanks": "SELECT COUNT(*) AS count FROM slice s JOIN track t ON s.track_id = t.id WHERE t.name GLOB 'vblank-*'",
            "clock snapshots": "SELECT COUNT(*) AS count FROM clock_snapshot",
        }
        with TraceSource(self.trace) as before, TraceSource(self.augmented()) as after:
            for name, sql in queries.items():
                count = int(before._rows(sql)[0].count)
                self.assertGreater(count, 0, name)
                self.assertEqual(int(after._rows(sql)[0].count), count, name)
            self.assertEqual(before.trace_range(), after.trace_range())

    def test_the_same_inputs_give_the_same_bytes(self) -> None:
        again = self.augmented("again.pftrace")
        self.assertEqual(again.read_bytes(), self.augmented().read_bytes())
        # And a recorded trace that is a gzip file is unpacked: the result is the same trace
        import gzip  # noqa: PLC0415

        packed = self.work / "device.pftrace.gz"
        _ = packed.write_bytes(gzip.compress(self.trace.read_bytes()))
        output = self.work / "from-gzip.pftrace"
        done = run_tool(CAPTURE, packed, "-o", output, "--app-clock-offset-ns", synthetic_trace.APP_CLOCK_OFFSET_NS)
        self.assertEqual(done.returncode, 0, done.stdout + done.stderr)
        self.assertEqual(output.read_bytes(), again.read_bytes())

    def test_the_frames_csv_has_what_the_trace_was_made_with(self) -> None:
        golden = frames()
        alignment = self.expected_alignment()
        boot = synthetic_trace.BOOT_MINUS_MONOTONIC_NS
        output = self.work / "frames.csv"
        done = run_tool(
            CAPTURE, self.trace, "--frames", output, "--process", synthetic_trace.APP_NAME, "--app-clock-offset-ns", synthetic_trace.APP_CLOCK_OFFSET_NS
        )
        self.assertEqual(done.returncode, 0, done.stdout + done.stderr)
        self.assertIn(f"display times moved by {-synthetic_trace.VBLANK_BEFORE_INTENDED_NS} ns", done.stdout)
        self.assertIn("the furthest 0 ns from it", done.stdout)
        with output.open(encoding="utf-8", newline="") as file:
            rows = list(csv.DictReader(file))
        self.assertEqual(len(rows), len(golden))
        checked = 0
        for row, frame in zip(rows, golden, strict=True):
            self.assertEqual(int(row["frameIndex"]), frame.frame_index)
            display = alignment.display_time(frame.first_seen_ns) + boot
            self.assertEqual(int(row["displayTraceNs"]), display)
            self.assertEqual(row["displayMinusVBlankNs"], "0", frame.frame_index)
            if frame.cpu_start_ns is None or frame.cpu_busy_ns is None:
                self.assertEqual(row["surfaceFrameToken"], "")
                self.assertEqual(row["cpuRunningNs"], "")
                continue
            self.assertEqual(int(row["cpuStartTraceNs"]), synthetic_trace.trace_ns(frame.cpu_start_ns))
            self.assertEqual(int(row["cpuEndTraceNs"]), synthetic_trace.trace_ns(frame.cpu_start_ns) + frame.cpu_busy_ns)
            self.assertEqual(int(row["surfaceFrameToken"]), 1_000 + frame.frame_index)
            self.assertEqual(int(row["displayFrameToken"]), 5_000 + frame.frame_index)
            self.assertEqual(row["layerName"], synthetic_trace.APP_LAYER)
            late = "Late" in frame.flags
            self.assertEqual(row["presentType"], "Late Present" if late else "On-time Present")
            self.assertEqual(row["onTimeFinish"], "false" if late else "true")
            preempted = synthetic_trace.is_preempted(frame.frame_index) and frame.cpu_busy_ns > (4 * synthetic_trace.PREEMPTED_NS)
            self.assertEqual(int(row["cpuRunnableNs"]), synthetic_trace.PREEMPTED_NS if preempted else 0, frame.frame_index)
            self.assertEqual(int(row["cpuRunningNs"]), frame.cpu_busy_ns - (synthetic_trace.PREEMPTED_NS if preempted else 0), frame.frame_index)
            self.assertEqual(int(row["cpuSleepingNs"]), 0)
            if frame.intended_display_ns is not None and frame.lateness_ns is not None:
                # The made-up system says a frame was presented at its intended display time (or right after its CPU work,
                # where that ended later): what the capture measured is later by the frame's lateness, less what the display
                # times were moved onto the vertical blanks by
                presented = synthetic_trace.presented_ns(frame.intended_display_ns, frame.cpu_start_ns, frame.cpu_busy_ns)
                self.assertEqual(int(row["systemPresentTraceNs"]), presented)
                self.assertEqual(int(row["displayMinusSystemPresentNs"]), display - presented)
                if presented == synthetic_trace.trace_ns(frame.intended_display_ns):
                    self.assertEqual(display - presented, frame.lateness_ns - synthetic_trace.VBLANK_BEFORE_INTENDED_NS)
                    checked += 1
        self.assertGreater(checked, 250)

    def test_the_capture_alone_is_a_trace_on_the_captures_clock(self) -> None:
        golden = frames()
        output = self.work / "capture.pftrace"
        done = run_tool(CAPTURE, "-o", output)
        self.assertEqual(done.returncode, 0, done.stdout + done.stderr)
        with TraceSource(output) as source:
            slices, values = source.added(root_name(RUN_NAME))
            self.assertEqual(slices[Track.ON_SCREEN.value], len(golden))
            self.assertEqual(values[Track.DISPLAY_STEP.value], len(golden) - 1)
            first = source._rows(f"SELECT MIN(s.ts) AS ts FROM slice s JOIN track t ON s.track_id = t.id WHERE t.name = {quoted(Track.ON_SCREEN.value)}")[0]
            self.assertEqual(int(first.ts), golden[0].first_seen_ns)
            self.assertEqual(source.errors(), {})

    def test_what_does_not_fit_is_refused_with_what_to_do(self) -> None:
        # The markers' clock taken as the system's own, which it is not: the CPU start times are not in the trace
        done = run_tool(CAPTURE, self.trace, "-o", self.work / "refused.pftrace")
        self.assertEqual(done.returncode, 1)
        self.assertIn("--app-clock-offset-ns", done.stderr)
        self.assertFalse((self.work / "refused.pftrace").exists())
        done = run_tool(CAPTURE, self.trace)
        self.assertEqual(done.returncode, 1)
        self.assertIn("Nothing to do", done.stderr)
        done = run_tool(CAPTURE, "--frames", self.work / "none.csv")
        self.assertEqual(done.returncode, 1)
        self.assertIn("give the trace", done.stderr)
        done = run_tool(self.work, self.trace, "-o", self.work / "refused.pftrace")
        self.assertEqual(done.returncode, 1)
        self.assertIn("has no analysis", done.stderr)
        done = run_tool(CAPTURE, self.trace, "-o", self.work / "refused.pftrace", "--run", "77")
        self.assertEqual(done.returncode, 1)
        self.assertIn("no run 77", done.stderr)
        not_a_trace = self.work / "notes.txt"
        _ = not_a_trace.write_text("not a trace", encoding="utf-8")
        done = run_tool(CAPTURE, not_a_trace, "-o", self.work / "refused.pftrace", "--capture-offset-ns", "0")
        self.assertEqual(done.returncode, 1)

    def test_a_trace_written_in_pieces(self) -> None:
        # A long recording is written to its file in pieces, with marks between them, and a reader takes the pieces one after
        # the other: packets after the last piece whose times lie in earlier ones come too late and are left out (appended to
        # this trace, a third of the tracks' events were). The tracks are written before the trace's own packets, where
        # nothing is too late, and all of them are there
        pieces = self.work / "pieces.pftrace"
        synthetic_trace.write(pieces, with_service_events=True)
        output = self.work / "pieces-augmented.pftrace"
        done = run_tool(CAPTURE, pieces, "-o", output, "--app-clock-offset-ns", synthetic_trace.APP_CLOCK_OFFSET_NS)
        self.assertEqual(done.returncode, 0, done.stdout + done.stderr)
        self.assertIn("read back: every track has what was written", done.stdout)
        with TraceSource(output) as source:
            self.assertEqual(source.errors(), {})
            slices, _ = source.added(root_name(RUN_NAME))
            self.assertEqual(slices[Track.ON_SCREEN.value], len(frames()))


if __name__ == "__main__":
    _ = unittest.main()
