# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""trace_alignment: a capture's clocks against a trace's, on the golden capture and on frames made up for each case."""

import unittest

from trace_alignment import (
    Alignment,
    AlignmentError,
    Anchor,
    ClockSnapshot,
    align,
    capture_minus_app,
    check_inside_trace,
    fit_vblanks,
    nearest,
    to_trace_ns,
)

from .golden_capture import CAPTURE_MINUS_PACER_NS, PERIOD_NS, MadeFrame, frames


class CaptureMinusAppTests(unittest.TestCase):
    def test_a_schedule_gives_the_analysis_own_offset(self) -> None:
        anchor, offset = capture_minus_app(frames())
        self.assertIs(anchor, Anchor.SCHEDULE)
        self.assertEqual(offset, CAPTURE_MINUS_PACER_NS)

    def test_frames_that_disagree_are_refused(self) -> None:
        made = [
            MadeFrame(1, 1_000, intended_display_ns=400, lateness_ns=0),
            MadeFrame(2, 2_000, intended_display_ns=1_400, lateness_ns=1),
        ]
        with self.assertRaisesRegex(AlignmentError, "2 values"):
            _ = capture_minus_app(made)

    def test_without_a_schedule_no_frame_is_on_screen_before_its_cpu_work_ended(self) -> None:
        # Shown 700, 500 and 900 ns after their CPU work ended: the clocks are lined up by the soonest
        made = [
            MadeFrame(1, 10_700, cpu_start_ns=9_000, cpu_busy_ns=1_000),
            MadeFrame(2, 20_500, cpu_start_ns=19_000, cpu_busy_ns=1_000),
            MadeFrame(3, 30_900, cpu_start_ns=29_000, cpu_busy_ns=1_000),
            MadeFrame(4, 40_000),
        ]
        self.assertEqual(capture_minus_app(made), (Anchor.CPU_WORK, 500))

    def test_markers_without_times_place_nothing(self) -> None:
        self.assertEqual(capture_minus_app([MadeFrame(1, 10_000), MadeFrame(2, 20_000)]), (Anchor.NOTHING, 0))
        self.assertEqual(capture_minus_app([]), (Anchor.NOTHING, 0))

    def test_an_offset_that_is_given_is_taken(self) -> None:
        self.assertEqual(align(frames(), 7, 123), Alignment(Anchor.GIVEN, 123, 7))
        self.assertEqual(align(frames(), 7), Alignment(Anchor.SCHEDULE, CAPTURE_MINUS_PACER_NS, 7))


class AlignmentTests(unittest.TestCase):
    def test_times_go_to_the_system_clock(self) -> None:
        alignment = Alignment(Anchor.SCHEDULE, capture_minus_app_ns=1_000, app_clock_offset_ns=50, vblank_shift_ns=3)
        self.assertEqual(alignment.marker_time(400), 450)
        self.assertEqual(alignment.capture_time(5_000), 4_050)
        self.assertEqual(alignment.display_time(5_000), 4_047)

    def test_an_on_time_frame_is_displayed_at_its_intended_time(self) -> None:
        alignment = align(frames())
        on_time = [f for f in frames() if f.lateness_ns == 0 and f.intended_display_ns is not None]
        self.assertGreater(len(on_time), 100)
        for frame in on_time:
            self.assertEqual(alignment.display_time(frame.first_seen_ns), alignment.marker_time(frame.intended_display_ns or 0))


class ToTraceTests(unittest.TestCase):
    def test_without_snapshots_the_clock_is_the_traces(self) -> None:
        self.assertEqual(to_trace_ns(123, []), 123)

    def test_the_newest_snapshot_at_or_before_a_time_converts_it(self) -> None:
        # The system clock is 1000 behind the trace's, and 1010 behind after the second snapshot
        snapshots = [ClockSnapshot(trace_ns=5_000, clock_ns=4_000), ClockSnapshot(trace_ns=9_010, clock_ns=8_000)]
        self.assertEqual(to_trace_ns(4_000, snapshots), 5_000)
        self.assertEqual(to_trace_ns(7_999, snapshots), 8_999)
        self.assertEqual(to_trace_ns(8_000, snapshots), 9_010)
        self.assertEqual(to_trace_ns(8_500, snapshots), 9_510)
        # Before the first snapshot: by the first
        self.assertEqual(to_trace_ns(3_000, snapshots), 4_000)


class InsideTraceTests(unittest.TestCase):
    def test_cpu_start_times_inside_the_trace_pass(self) -> None:
        made = [MadeFrame(index, 0, cpu_start_ns=1_000 * index) for index in range(1, 11)]
        check_inside_trace(made, Alignment(Anchor.NOTHING, 0), [], (0, 20_000))
        # A trace that begins in the middle of the run: half of the frames are inside
        check_inside_trace(made, Alignment(Anchor.NOTHING, 0), [], (6_000, 20_000))
        # No CPU start times: nothing to check
        check_inside_trace([MadeFrame(1, 0)], Alignment(Anchor.NOTHING, 0), [], (0, 1))

    def test_another_clock_or_another_run_is_refused(self) -> None:
        made = [MadeFrame(index, 0, cpu_start_ns=1_000 * index) for index in range(1, 11)]
        with self.assertRaisesRegex(AlignmentError, "Only 4 of 10 CPU start times"):
            check_inside_trace(made, Alignment(Anchor.NOTHING, 0), [], (7_000, 20_000))
        # The same frames with the application's clock an hour off the system's
        with self.assertRaisesRegex(AlignmentError, "--app-clock-offset-ns"):
            check_inside_trace(made, Alignment(Anchor.NOTHING, 0, app_clock_offset_ns=3_600_000_000_000), [], (0, 20_000))


class VBlankFitTests(unittest.TestCase):
    def test_nearest(self) -> None:
        self.assertIsNone(nearest([], 5))
        self.assertEqual(nearest([10, 20, 30], 1), 10)
        self.assertEqual(nearest([10, 20, 30], 99), 30)
        self.assertEqual(nearest([10, 20, 30], 14), 10)
        self.assertEqual(nearest([10, 20, 30], 15), 10)
        self.assertEqual(nearest([10, 20, 30], 16), 20)

    def test_display_times_a_little_after_the_vertical_blanks(self) -> None:
        vblanks = [1_000_000 + (index * PERIOD_NS) for index in range(100)]
        # Every display time 2 ms after a vertical blank, a few of them a refresh or two late
        displays = [vblanks[index] + 2_000_000 for index in (3, 4, 5, 7, 8, 9, 12, 13, 14, 15)]
        fit = fit_vblanks(displays, vblanks, PERIOD_NS)
        assert fit is not None
        self.assertEqual(fit.shift_ns, 2_000_000)
        self.assertEqual(fit.used_frames, 10)
        self.assertEqual(fit.spread_ns, 0)

    def test_display_times_a_little_before_the_vertical_blanks_move_forward(self) -> None:
        vblanks = [1_000_000 + (index * PERIOD_NS) for index in range(100)]
        displays = [vblanks[index] - 1_500_000 for index in range(10, 30)]
        fit = fit_vblanks(displays, vblanks, PERIOD_NS)
        assert fit is not None
        self.assertEqual(fit.shift_ns, -1_500_000)
        self.assertEqual(fit.spread_ns, 0)

    def test_display_times_on_both_sides_of_a_vertical_blank_are_one_group(self) -> None:
        # Jitter of 0.3 ms either way around the vertical blanks: the points lie across the end of the refresh on the circle
        vblanks = [index * PERIOD_NS for index in range(1, 100)]
        jitter = [-300_000, 200_000, -100_000, 0, 300_000, 100_000, -200_000, 250_000, -250_000]
        displays = [vblanks[10 + index] + off for index, off in enumerate(jitter)]
        fit = fit_vblanks(displays, vblanks, PERIOD_NS)
        assert fit is not None
        self.assertEqual(fit.shift_ns, 0)
        self.assertEqual(fit.used_frames, len(jitter))
        self.assertEqual(fit.spread_ns, 300_000)

    def test_too_few_vertical_blanks_fit_nothing(self) -> None:
        self.assertIsNone(fit_vblanks([10, 20, 30], [], PERIOD_NS))
        # Vertical blanks that end before the run
        vblanks = [index * PERIOD_NS for index in range(10)]
        displays = [(index * PERIOD_NS) + 5 for index in range(50, 70)]
        self.assertIsNone(fit_vblanks(displays, vblanks, PERIOD_NS))
        with self.assertRaises(AlignmentError):
            _ = fit_vblanks(displays, vblanks, 0)

    def test_the_golden_capture_on_vertical_blanks_of_its_own_rate(self) -> None:
        # The display's vertical blanks are 5 ms before where the analysis put the frames: the clip's captures are a sixtieth of
        # a second apart to the nanoseconds, and so are these vertical blanks
        alignment = align(frames())
        displays = [alignment.display_time(f.first_seen_ns) for f in frames()]
        vblanks = [(((index * 1_000_000_000) + 30) // 60) - CAPTURE_MINUS_PACER_NS - 5_000_000 for index in range(600)]
        fit = fit_vblanks(displays, vblanks, PERIOD_NS)
        assert fit is not None
        self.assertEqual(fit.shift_ns, 5_000_000)
        self.assertEqual(fit.used_frames, len(displays))
        self.assertEqual(fit.spread_ns, 0)


if __name__ == "__main__":
    _ = unittest.main()
