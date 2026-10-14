# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""trace_diagnosis.py on the SDK's golden capture: the facts its markers give alone, and the facts a made-up join and a made-up
part 2 of the frame record add to known frames that went wrong, each where it was put and in its order."""

import csv
import io
import unittest
from pathlib import Path

from mb_framepacing.data import FrameRow
from trace_diagnosis import (
    ANIMATION_STEP,
    CPU_LONGER,
    CPU_PAST_DUE,
    DISPLAY_NAME_PREFIX,
    DONE_IN_TIME,
    ENVIRONMENT_NAME,
    GPU_NAME_PREFIX,
    GPU_PAST_DUE,
    NOT_PRESENTED_NAME_PREFIX,
    NOTHING_FOUND,
    PRESENT_BLOCKED,
    PRESENT_NAME_PREFIX,
    PRESENT_NOT_ACCEPTED,
    PRESENT_PAST_DUE,
    SHOWN_SOONER,
    SKIPPED_BEFORE,
    SWAP_CHAIN_RECREATED_NAME,
    SWAP_INTERVAL_CHANGE_NAME,
    SYSTEM_JANK,
    WAIT_NAME_PREFIX,
    WAIT_PAST_TIME,
    WAITED_FOR_CPU,
    WRONG_SKIPPED,
    Diagnosis,
    Fact,
    FrameDetails,
    Present,
    RunMoment,
    Wait,
    diagnose,
    frame_lines,
    summary_lines,
    to_csv,
)
from trace_events import VERDICT_LATE, VERDICT_OFF, VERDICT_ON_TIME
from trace_frames import TraceFrame, from_csv
from trace_frames import to_csv as join_csv

from .golden_capture import PERIOD_NS, MadeFrame, frames, summary

ROOT = Path(__file__).resolve().parent.parent.parent
THRESHOLD_NS = 1_000_000
# Where the made-up trace puts the run: a frame's display time on the trace's clock is its display time on the capture's and this
TRACE_MINUS_CAPTURE_NS = 7_000_000_000


def _joined(frame: FrameRow, **values: int | str | None) -> TraceFrame:
    """A made-up join row of a golden frame: its display time and its CPU work on the trace's clock, and what the test adds."""
    start = frame.first_seen_ns + TRACE_MINUS_CAPTURE_NS - 40_000_000
    end = start + (frame.cpu_busy_ns or 0)
    fields: dict[str, int | str | None] = {"cpu_start_trace_ns": start, "cpu_end_trace_ns": end, "cpu_running_ns": frame.cpu_busy_ns, "cpu_runnable_ns": 0}
    fields.update(values)
    return TraceFrame(frame.frame_index, frame.first_seen_ns + TRACE_MINUS_CAPTURE_NS, **fields)  # pyright: ignore[reportArgumentType]


def _due(frame: FrameRow) -> int:
    """The refresh a golden frame was due at, on the made-up trace's clock."""
    return frame.first_seen_ns + TRACE_MINUS_CAPTURE_NS + (frame.animation_error_ns or 0)


class GoldenAloneTests(unittest.TestCase):
    """The golden capture's markers alone: its 33 late frames and 33 frames shown a refresh too soon."""

    def test_the_markers_alone_say_why_most_frames_went_wrong(self) -> None:
        diagnoses = diagnose(frames(), {}, {}, [], PERIOD_NS, summary().error_threshold_ns)
        wrong = [d for d in diagnoses if d.went_wrong]
        self.assertEqual(len(diagnoses), 388)
        self.assertEqual(sum(1 for d in wrong if d.what == VERDICT_LATE), 33)
        self.assertEqual(sum(1 for d in wrong if d.what == VERDICT_OFF), 33)
        # A late frame's CPU work was longer than its intended period, but for two
        late = [d for d in wrong if d.what == VERDICT_LATE]
        self.assertEqual(sum(1 for d in late if d.first == CPU_LONGER), 31)
        self.assertEqual(sum(1 for d in late if d.first == NOTHING_FOUND), 2)
        # A frame off by a refresh was shown a refresh sooner than its intended period of two
        off = [d for d in wrong if d.what == VERDICT_OFF]
        self.assertTrue(all(d.first == SHOWN_SOONER for d in off))
        self.assertTrue(all(d.facts[0].value_ns == 16_666_667 or d.facts[0].value_ns == 16_666_668 for d in off), [d.facts[0] for d in off])
        # No frame on time has a fact
        self.assertTrue(all(not d.facts for d in diagnoses if not d.went_wrong))

    def test_the_summary_holds_each_fact_against_the_frames_on_time(self) -> None:
        diagnoses = diagnose(frames(), {}, {}, [], PERIOD_NS, summary().error_threshold_ns)
        lines = summary_lines(diagnoses, name_programs=True)
        self.assertEqual(lines[0], "388 frames, 66 of them went wrong: 33 late, 33 off")
        self.assertIn(f"  {CPU_LONGER}: 31 of 66 (47 %), on time 0 of 322 (0 %)", lines)
        self.assertIn(f"  {SHOWN_SOONER}: 33 of 66 (50 %), on time 0 of 322 (0 %)", lines)
        self.assertIn("Bursts: 8 of 3 or more frames in a row, the longest 4 from #492", lines)
        # Every frame that went wrong is in the frame lines, its facts under it
        text = frame_lines(diagnoses, name_programs=True)
        self.assertEqual(sum(1 for line in text if line.startswith("#")), 66)
        self.assertEqual(text[0], "#480 late, animation error -16.67 ms")
        self.assertEqual(text[1], "  its CPU work took 20.81 ms, its intended period 16.67 ms")

    def test_the_csv_has_a_line_per_frame_that_went_wrong(self) -> None:
        diagnoses = diagnose(frames(), {}, {}, [], PERIOD_NS, summary().error_threshold_ns)
        rows = list(csv.reader(io.StringIO(to_csv(diagnoses))))
        self.assertEqual(rows[0], ["frameIndex", "what", "animationErrorNs", "first", "facts"])
        self.assertEqual(len(rows), 67)
        self.assertEqual(rows[1], ["480", "late", "-16666667", CPU_LONGER, f"{CPU_LONGER}=4140688"])


class MadeUpTraceTests(unittest.TestCase):
    """Facts put on known frames that went wrong, by a made-up join and part 2 of the frame record."""

    frames: list[FrameRow] = frames()
    late: list[FrameRow] = [f for f in frames if "Late" in f.flags]
    # The two late frames the markers alone do not explain
    unexplained: list[FrameRow] = [
        f for f, d in zip(frames, diagnose(frames, {}, {}, [], PERIOD_NS, THRESHOLD_NS), strict=True) if d.what == VERDICT_LATE and not d.facts
    ]

    def test_two_late_frames_are_not_explained_by_the_markers(self) -> None:
        self.assertEqual(len(self.unexplained), 2)

    def _diagnosis(self, joined: dict[int, TraceFrame], details: dict[int, FrameDetails], moments: tuple[RunMoment, ...] = ()) -> dict[int, Diagnosis]:
        return {d.frame_index: d for d in diagnose(self.frames, joined, details, list(moments), PERIOD_NS, THRESHOLD_NS)}

    def test_each_fact_is_found_where_it_was_put_and_comes_in_its_order(self) -> None:
        frame = self.late[0]
        due = _due(frame)
        joined = {frame.frame_index: _joined(frame, cpu_runnable_ns=3_000_000, jank_type="App Deadline Missed", present_type="Late Present")}
        start = joined[frame.frame_index].cpu_start_trace_ns or 0
        details = {
            frame.frame_index: FrameDetails(
                waits=(Wait(start - 6_000_000, start, "acquire", start - 8_000_000), Wait(start - 9_000_000, start - 6_000_000, "present")),
                present=Present(due - 5_000_000, due + 4_000_000, "suboptimal", 1),
                gpu_end_ns=due + 2_000_000,
            )
        }
        facts = self._diagnosis(joined, details)[frame.frame_index].facts
        self.assertEqual(
            [f.kind for f in facts],
            [WAIT_PAST_TIME, WAITED_FOR_CPU, CPU_LONGER, GPU_PAST_DUE, PRESENT_PAST_DUE, PRESENT_BLOCKED, PRESENT_NOT_ACCEPTED, SYSTEM_JANK],
        )
        self.assertEqual(facts[0], Fact(WAIT_PAST_TIME, 8_000_000, "its wait before it (acquire) ended 8.00 ms after the time it waited for"))
        self.assertEqual(facts[1].value_ns, 3_000_000)
        self.assertEqual(facts[3].value_ns, 2_000_000)
        self.assertEqual(facts[7].text, "the system's frame timeline says 'App Deadline Missed' (Late Present)")

    def test_a_frame_done_in_time_and_shown_later_says_so(self) -> None:
        # The application's side ended well before the refresh the frame was due at
        frame = self.unexplained[0]
        due = _due(frame)
        joined = {frame.frame_index: _joined(frame, cpu_start_trace_ns=due - 12_000_000, cpu_end_trace_ns=due - 9_000_000)}
        details = {frame.frame_index: FrameDetails(present=Present(due - 9_000_000, due - 8_900_000, "accepted"), gpu_end_ns=due - 5_000_000)}
        diagnosis = self._diagnosis(joined, details)[frame.frame_index]
        self.assertEqual(diagnosis.first, DONE_IN_TIME)
        self.assertEqual(diagnosis.facts[0].value_ns, 5_000_000)
        # Its CPU work ended after that refresh: no longer done in time
        joined = {frame.frame_index: _joined(frame, cpu_start_trace_ns=due - 2_000_000, cpu_end_trace_ns=due + 3_000_000)}
        diagnosis = self._diagnosis(joined, {})[frame.frame_index]
        self.assertEqual([f.kind for f in diagnosis.facts], [CPU_PAST_DUE])

    def test_a_moment_of_the_run_shortly_before_a_frame_is_named_with_it(self) -> None:
        frame = self.late[0]
        joined = {frame.frame_index: _joined(frame)}
        shown = joined[frame.frame_index].display_trace_ns
        moments = (
            RunMoment(SWAP_CHAIN_RECREATED_NAME, shown - 300_000_000, "out of date"),
            RunMoment(SWAP_INTERVAL_CHANGE_NAME, shown - 100_000_000, "from 1 to 2, by the pacer"),
            RunMoment(ENVIRONMENT_NAME, shown - 2_000_000_000),
        )
        diagnosis = self._diagnosis(joined, {}, moments)[frame.frame_index]
        self.assertEqual(
            diagnosis.moments,
            (
                "the swap chain was made anew (out of date) 300.00 ms before it was shown",
                "the swap interval changed (from 1 to 2, by the pacer) 100.00 ms before it was shown",
            ),
        )
        lines = summary_lines(list(self._diagnosis(joined, {}, moments).values()), name_programs=False)
        self.assertIn("Shortly after a moment of the run (within 1000.00 ms): 1 frames that went wrong", lines)

    def test_the_programs_are_named_only_when_asked(self) -> None:
        frame = self.late[0]
        joined = {frame.frame_index: _joined(frame, cpu_runnable_ns=2_500_000)}
        programs = {frame.frame_index: [("other", 2_000_000), ("compositor", 500_000)]}
        diagnoses = diagnose(self.frames, joined, {}, [], PERIOD_NS, THRESHOLD_NS, programs)
        named = summary_lines(diagnoses, name_programs=True)
        self.assertIn("       2.00 ms  other", named)
        self.assertIn("  while it waited for a CPU: other 2.00 ms, compositor 0.50 ms", frame_lines(diagnoses, name_programs=True))
        self.assertFalse(any("other" in line for line in summary_lines(diagnoses, name_programs=False) + frame_lines(diagnoses, name_programs=False)))


class MadeFrameTests(unittest.TestCase):
    """Frames made up for what the golden capture does not have."""

    def test_frame_indices_never_shown_and_an_animation_step_that_was_off(self) -> None:
        made = [
            MadeFrame(10, 1_000_000_000, display_delta_ns=PERIOD_NS, animation_delta_ns=PERIOD_NS, animation_error_ns=0, target_ns=PERIOD_NS),
            MadeFrame(13, 1_016_666_667, skipped_before=2, display_delta_ns=PERIOD_NS, animation_delta_ns=PERIOD_NS, animation_error_ns=0, target_ns=PERIOD_NS),
            MadeFrame(
                14, 1_033_333_334, display_delta_ns=PERIOD_NS, animation_delta_ns=PERIOD_NS + 4_000_000, animation_error_ns=4_000_000, target_ns=PERIOD_NS
            ),
        ]
        diagnoses = diagnose(made, {}, {}, [], PERIOD_NS, THRESHOLD_NS)
        self.assertEqual(diagnoses[0].what, VERDICT_ON_TIME)
        self.assertEqual(diagnoses[1].what, WRONG_SKIPPED)
        self.assertEqual(diagnoses[1].facts, (Fact(SKIPPED_BEFORE, 2, "2 frame indices before it never shown"),))
        self.assertEqual(diagnoses[2].what, VERDICT_OFF)
        self.assertEqual(diagnoses[2].first, ANIMATION_STEP)
        self.assertEqual(diagnoses[2].facts[0].text, "its display time step was 1 refresh as intended, its animation time step 20.67 ms")

    def test_a_repeat_is_named(self) -> None:
        made: list[MadeFrame] = []
        for index in range(60):
            error = -PERIOD_NS if index % 6 == 0 and index > 0 else 0
            flags = ("Late",) if error else ()
            made.append(
                MadeFrame(
                    index, 1_000_000_000 + index * PERIOD_NS, display_delta_ns=PERIOD_NS, animation_delta_ns=PERIOD_NS, animation_error_ns=error, flags=flags
                )
            )
        lines = summary_lines(diagnose(made, {}, {}, [], PERIOD_NS, THRESHOLD_NS), name_programs=False)
        self.assertIn("Repeats: 8 times 6 frame indices after the one before that went wrong", lines)


class NamesTests(unittest.TestCase):
    """Part 2's strings are the SDK's: its golden names and its document."""

    def test_the_prefixes_are_those_of_the_golden_names(self) -> None:
        rows = list(csv.DictReader((ROOT / "sdk" / "test-data" / "trace" / "frame-details.csv").read_text(encoding="utf-8").splitlines()))
        prefixes = {
            "Wait": WAIT_NAME_PREFIX,
            "Present": PRESENT_NAME_PREFIX,
            "Gpu": GPU_NAME_PREFIX,
            "Display": DISPLAY_NAME_PREFIX,
            "NotPresented": NOT_PRESENTED_NAME_PREFIX,
        }
        self.assertEqual({row["detail"] for row in rows}, set(prefixes))
        for row in rows:
            self.assertEqual(row["name"], f"{prefixes[row['detail']]}{row['runId']} {row['frameIndex']}")

    def test_the_moments_are_named_as_the_document_names_them(self) -> None:
        document = (ROOT / "sdk" / "doc" / "trace-frame-record.md").read_text(encoding="utf-8")
        for name in (ENVIRONMENT_NAME, SWAP_CHAIN_RECREATED_NAME, SWAP_INTERVAL_CHANGE_NAME):
            self.assertIn(f"`{name}`", document)


class JoinCsvTests(unittest.TestCase):
    def test_the_join_reads_back_as_written(self) -> None:
        text = (ROOT / "measure" / "test-data" / "trace" / "frames.csv").read_text(encoding="utf-8")
        rows = from_csv(text)
        self.assertEqual(len(rows), 388)
        self.assertEqual(join_csv(rows), text)

    def test_a_file_that_is_not_a_join_is_refused(self) -> None:
        with self.assertRaises(ValueError):
            _ = from_csv("frameIndex,displayTraceNs\n1,2\n")
        header = (ROOT / "measure" / "test-data" / "trace" / "frames.csv").read_text(encoding="utf-8").splitlines()[0]
        cells = ",".join([""] * 19)
        with self.assertRaises(ValueError):
            _ = from_csv(f"{header}\n{cells}\n")
        with self.assertRaises(ValueError):
            _ = from_csv(f"{header}\n1,x" + "," * 17 + "\n")


if __name__ == "__main__":
    _ = unittest.main()
