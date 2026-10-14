# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
# pyright: basic
"""trace_diagnose.py from end to end: a copy of the golden capture joined with the SDK's example trace (the made-up application's
own trace with part 2 of the frame record) by trace_augment.py, as an analysis with a trace leaves it, then diagnosed; and the
trace reader's account of what ran while a thread waited for a CPU, on the made-up device trace.

These tests need the dependency group 'trace' and the trace processor, as test_trace_roundtrip does: skipped where either can
not be had, and a failure instead where MB_REQUIRE_TRACE_PROCESSOR is set (CI)."""

import csv
import io
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from .golden_capture import CAPTURE, frames

TOOLS = Path(__file__).resolve().parent.parent
REQUIRED = os.environ.get("MB_REQUIRE_TRACE_PROCESSOR", "") not in ("", "0")

try:
    from trace_source import TraceSource

    from . import synthetic_trace

    UNAVAILABLE = ""
except ImportError as error:
    UNAVAILABLE = f"Perfetto's Python package is not installed ({error}): uv run --group trace python -m unittest ..."


def run(script: str, *arguments: str | Path) -> subprocess.CompletedProcess[str]:
    return subprocess.run([sys.executable, str(TOOLS / script), *[str(a) for a in arguments]], capture_output=True, text=True, check=False)


class DiagnoseTests(unittest.TestCase):
    folder: tempfile.TemporaryDirectory[str]
    work: Path
    capture: Path
    analysis: Path

    @classmethod
    def setUpClass(cls) -> None:
        if UNAVAILABLE:
            if REQUIRED:
                raise AssertionError(UNAVAILABLE)
            raise unittest.SkipTest(UNAVAILABLE)
        cls.folder = tempfile.TemporaryDirectory()
        cls.work = Path(cls.folder.name)
        cls.capture = cls.work / "capture"
        _ = shutil.copytree(CAPTURE, cls.capture)
        cls.analysis = cls.capture / "analysis"
        example = cls.work / "example-app.pftrace"
        synthetic_trace.write_example(example)
        done = run("trace_augment.py", cls.capture, example, "-o", cls.analysis / "run-1-augmented.pftrace", "--frames", cls.analysis / "run-1-trace.csv")
        if done.returncode != 0:
            cls.folder.cleanup()
            if REQUIRED or "trace processor" not in done.stderr:
                raise AssertionError(done.stdout + done.stderr)
            raise unittest.SkipTest(f"Perfetto's trace processor could not be had or started: {done.stderr}")

    @classmethod
    def tearDownClass(cls) -> None:
        cls.folder.cleanup()

    def test_a_capture_with_its_join_and_augmented_trace_is_diagnosed(self) -> None:
        done = run("trace_diagnose.py", self.capture)
        self.assertEqual(done.returncode, 0, done.stdout + done.stderr)
        self.assertIn("run-1-trace.csv: the join of 388 frames", done.stdout)
        self.assertIn("run-1-augmented.pftrace: part 2 of the frame record for 387 frames, 2 moments of the run", done.stdout)
        self.assertIn("388 frames, 66 of them went wrong: 33 late, 33 off", done.stdout)
        text = (self.analysis / "run-1-diagnosis.txt").read_text(encoding="utf-8")
        self.assertTrue(text.startswith("busy-full-rate: what went wrong\n"), text[:200])
        rows = list(csv.DictReader(io.StringIO((self.analysis / "run-1-diagnosis.csv").read_text(encoding="utf-8"))))
        self.assertEqual(len(rows), 66)
        # The example's GPU work ends two milliseconds after its present: after the refresh for the late frames whose present
        # was near it, so part 2 is read and held against the capture's refreshes
        late = {f.frame_index for f in frames() if "Late" in f.flags}
        self.assertEqual({int(row["frameIndex"]) for row in rows if row["what"] == "late"}, late)
        self.assertTrue(any("GPU work ended after its refresh" in row["facts"] for row in rows), [row["facts"] for row in rows][:5])

    def test_the_files_go_where_they_are_told_and_name_no_program_when_asked(self) -> None:
        output = self.work / "elsewhere"
        done = run("trace_diagnose.py", self.capture, "-o", output, "--no-programs")
        self.assertEqual(done.returncode, 0, done.stdout + done.stderr)
        self.assertTrue((output / "run-1-diagnosis.txt").is_file())
        self.assertTrue((output / "run-1-diagnosis.csv").is_file())
        self.assertNotIn("What ran on the CPU", (output / "run-1-diagnosis.txt").read_text(encoding="utf-8"))

    def test_a_capture_without_a_join_gives_the_markers_facts(self) -> None:
        bare = self.work / "bare"
        _ = shutil.copytree(CAPTURE, bare)
        done = run("trace_diagnose.py", bare)
        self.assertEqual(done.returncode, 0, done.stdout + done.stderr)
        self.assertIn("no run-1-trace.csv: no trace was joined", done.stdout)
        self.assertIn("no run-1-augmented.pftrace", done.stdout)
        self.assertIn("CPU work longer than intended: 31 of 66 (47 %)", done.stdout)

    def test_what_ran_while_the_application_waited_for_a_cpu(self) -> None:
        device = self.work / "device.pftrace"
        synthetic_trace.write(device)
        preempted = next(f for f in frames() if synthetic_trace.is_preempted(f.frame_index) and (f.cpu_busy_ns or 0) > 4 * synthetic_trace.PREEMPTED_NS)
        begin = synthetic_trace.trace_ns(preempted.cpu_start_ns or 0)
        with TraceSource(device) as source:
            utid = next(t[0] for t in source.threads(synthetic_trace.APP_NAME) if t[3])
            self.assertEqual(source.waited_for_cpu(utid, begin, begin + (preempted.cpu_busy_ns or 0)), [("other", synthetic_trace.PREEMPTED_NS)])
            # A frame that was not put aside waited for nothing
            calm = next(f for f in frames() if not synthetic_trace.is_preempted(f.frame_index) and f.cpu_start_ns is not None)
            start = synthetic_trace.trace_ns(calm.cpu_start_ns or 0)
            self.assertEqual(source.waited_for_cpu(utid, start, start + (calm.cpu_busy_ns or 0)), [])


if __name__ == "__main__":
    _ = unittest.main()
