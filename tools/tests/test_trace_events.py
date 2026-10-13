# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
"""trace_events: a capture's frames as the tracks of a trace, on the golden capture and on frames made up for each case."""

import unittest

from trace_alignment import Alignment, Anchor, align
from trace_events import (
    COUNTER_TRACKS,
    TRACK_DESCRIPTIONS,
    Counter,
    Instant,
    Slice,
    Track,
    capture_events,
    capture_range_of,
    counters,
    cpu,
    frame_events,
    in_stretch,
    intended,
    milliseconds,
    on_screen,
    track_events,
)

from .golden_capture import PERIOD_NS, MadeCapture, MadeFrame, captures, frames

PLAIN = Alignment(Anchor.NOTHING, 0)


class OnScreenTests(unittest.TestCase):
    def test_a_slice_per_frame_from_its_display_time_for_its_time_on_screen(self) -> None:
        made = [
            MadeFrame(7, 1_000, on_screen_ns=500, display_delta_ns=None),
            MadeFrame(8, 1_500, on_screen_ns=1_000, display_delta_ns=500, animation_delta_ns=450, animation_error_ns=-50, lateness_ns=20, target_ns=500),
            # On screen for longer than to the next frame's display time (a gap in the capture): cut there
            MadeFrame(9, 2_000, on_screen_ns=300, skipped_before=2, flags=("SkippedBefore", "Late"), cpu_start_ns=1_200, cpu_busy_ns=100),
        ]
        slices = list(on_screen(made, Alignment(Anchor.GIVEN, 100, app_clock_offset_ns=10, vblank_shift_ns=1), 500))
        self.assertEqual([(s.start_ns, s.end_ns, s.name) for s in slices], [(909, 1_409, "#7"), (1_409, 1_909, "#8"), (1_909, 2_209, "#9")])
        self.assertTrue(all(s.track is Track.ON_SCREEN for s in slices))
        self.assertEqual(dict(slices[0].args), {"frame index": 7, "on screen (ms)": 0.0005, "captures": 1})
        self.assertEqual(
            dict(slices[1].args),
            {
                "frame index": 8,
                "on screen (ms)": 0.001,
                "captures": 1,
                "display time step (ms)": 0.0005,
                "display time step (refreshes)": 1,
                "animation time step (ms)": 0.00045,
                "animation error (ms)": -0.00005,
                "lateness (ms)": 0.00002,
                "target frame time (ms)": 0.0005,
            },
        )
        self.assertEqual(dict(slices[2].args)["frame indices skipped before"], 2)
        self.assertEqual(dict(slices[2].args)["flags"], "SkippedBefore, Late")
        # Only a frame that has a CPU slice is joined to one
        self.assertEqual([s.flow for s in slices], [0, 0, 10])

    def test_the_golden_capture(self) -> None:
        golden = frames()
        slices = list(on_screen(golden, align(golden), PERIOD_NS))
        self.assertEqual(len(slices), 388)
        self.assertEqual(slices[0].name, "#388")
        self.assertEqual(slices[-1].name, "#775")
        for before, after in zip(slices, slices[1:], strict=False):
            self.assertLessEqual(before.start_ns, before.end_ns)
            self.assertLessEqual(before.end_ns, after.start_ns)


class IntendedAndCpuTests(unittest.TestCase):
    def test_intended_slices_run_to_the_next_frames_intended_time(self) -> None:
        made = [
            MadeFrame(1, 0, intended_display_ns=1_000, target_ns=400),
            MadeFrame(2, 0),
            MadeFrame(3, 0, intended_display_ns=1_500, target_ns=400),
            # Before the one before it: left out, and the one before runs to the next that is not
            MadeFrame(4, 0, intended_display_ns=1_200, target_ns=400),
            MadeFrame(5, 0, intended_display_ns=2_100, target_ns=400),
        ]
        slices = list(intended(made, Alignment(Anchor.NOTHING, 0, app_clock_offset_ns=5)))
        self.assertEqual([(s.name, s.start_ns, s.end_ns) for s in slices], [("#1", 1_005, 1_505), ("#3", 1_505, 2_105), ("#5", 2_105, 2_505)])
        self.assertEqual(list(intended([MadeFrame(1, 0)], PLAIN)), [])

    def test_cpu_slices_are_cut_where_the_next_begins_and_stay_in_time_order(self) -> None:
        made = [
            MadeFrame(1, 0, cpu_start_ns=1_000, cpu_busy_ns=300),
            # Busy for longer than to the next frame's start
            MadeFrame(2, 0, cpu_start_ns=2_000, cpu_busy_ns=1_500),
            MadeFrame(3, 0, cpu_start_ns=3_000, cpu_busy_ns=200),
            # A start before the one before it: left out, and it cuts nothing
            MadeFrame(4, 0, cpu_start_ns=2_500, cpu_busy_ns=200),
            MadeFrame(5, 0, cpu_start_ns=None, cpu_busy_ns=200),
            MadeFrame(6, 0, cpu_start_ns=4_000, cpu_busy_ns=0),
        ]
        slices = list(cpu(made, PLAIN))
        self.assertEqual(
            [(s.name, s.start_ns, s.end_ns, s.flow) for s in slices],
            [("#1", 1_000, 1_300, 2), ("#2", 2_000, 3_000, 3), ("#3", 3_000, 3_200, 4), ("#6", 4_000, 4_000, 7)],
        )
        self.assertEqual(dict(slices[1].args), {"frame index": 2, "CPU busy (ms)": 0.0015})

    def test_the_golden_capture_has_both_for_nearly_every_frame(self) -> None:
        golden = frames()
        alignment = align(golden)
        self.assertEqual(len(list(intended(golden, alignment))), sum(1 for f in golden if f.intended_display_ns is not None))
        cpu_slices = list(cpu(golden, alignment))
        self.assertEqual(len(cpu_slices), sum(1 for f in golden if f.cpu_start_ns is not None and f.cpu_busy_ns is not None))
        shown = {s.flow: s for s in on_screen(golden, alignment, PERIOD_NS) if s.flow != 0}
        for work in cpu_slices:
            # A frame's CPU work is done before the frame is on screen
            self.assertLessEqual(work.end_ns, shown[work.flow].start_ns)


class CounterTests(unittest.TestCase):
    def test_a_value_per_frame_that_has_one(self) -> None:
        made = [
            MadeFrame(1, 1_000),
            MadeFrame(2, 2_000, display_delta_ns=1_000_000, animation_error_ns=-250_000, lateness_ns=0),
            MadeFrame(3, 3_000, display_delta_ns=2_000_000),
        ]
        values = list(counters(made, PLAIN))
        self.assertEqual(
            values,
            [
                Counter(Track.ANIMATION_ERROR, 2_000, -0.25),
                Counter(Track.DISPLAY_STEP, 2_000, 1.0),
                Counter(Track.LATENESS, 2_000, 0.0),
                Counter(Track.DISPLAY_STEP, 3_000, 2.0),
            ],
        )
        self.assertEqual(milliseconds(16_666_667), 16.666667)

    def test_the_golden_capture(self) -> None:
        golden = frames()
        values = list(counters(golden, align(golden)))
        self.assertEqual(sum(1 for v in values if v.track is Track.LATENESS), sum(1 for f in golden if f.lateness_ns is not None))
        self.assertEqual(sum(1 for v in values if v.track is Track.DISPLAY_STEP), 387)


class EventTests(unittest.TestCase):
    def test_frame_events_say_what_the_capture_found(self) -> None:
        made = [
            MadeFrame(10, 1_000, flags=("Late",), lateness_ns=17_000_000),
            MadeFrame(13, 2_000, skipped_before=2, flags=("SkippedBefore",)),
            MadeFrame(14, 3_000, skipped_before=1, flags=("SkippedBefore", "Torn", "Late")),
            MadeFrame(15, 4_000, older_frames=((12, 4_500), (11, 4_200))),
            MadeFrame(16, 5_000, flags=("StaticAfter",)),
        ]
        events = frame_events(made, Alignment(Anchor.GIVEN, 100))
        self.assertEqual(
            [(e.time_ns, e.name) for e in events],
            [
                (900, "late #10"),
                (1_900, "2 frame indices skipped before #13"),
                (2_900, "1 frame index skipped before #14"),
                (2_900, "late #14"),
                (2_900, "torn #14"),
                (4_100, "out of order: #11 while #15 was the newest"),
                (4_400, "out of order: #12 while #15 was the newest"),
            ],
        )
        self.assertEqual(dict(events[0].args), {"frame index": 10, "lateness (ms)": 17.0})
        self.assertEqual(dict(events[3].args), {"frame index": 14})

    def test_the_golden_capture_has_its_late_frames(self) -> None:
        golden = frames()
        events = frame_events(golden, align(golden))
        self.assertEqual(sum(1 for e in events if e.name.startswith("late ")), 33)
        self.assertEqual(len(events), 33)
        self.assertEqual(capture_events(captures(), align(golden), capture_range_of(golden)), [])

    def test_capture_events_are_the_captures_own_gaps(self) -> None:
        made = [
            MadeCapture(1, 500, source_drops_before=3),
            MadeCapture(2, 1_000),
            MadeCapture(3, None, "NotRecorded"),
            MadeCapture(4, None, "NotRecorded"),
            MadeCapture(5, 2_000, "Undecodable", missed_before=1),
            MadeCapture(6, 3_000, source_drops_before=2),
            MadeCapture(7, None, "NotRecorded"),
            MadeCapture(8, 9_000),
        ]
        events = capture_events(made, Alignment(Anchor.GIVEN, 100), (1_000, 3_000))
        self.assertEqual(
            [(e.time_ns, e.name, dict(e.args)["capture index"]) for e in events],
            [
                (1_900, "not recorded: 2 before this capture", 5),
                (1_900, "missed: 1 before this capture", 5),
                (1_900, "not decoded", 5),
                (2_900, "dropped by the source: 2 before this capture", 6),
            ],
        )
        self.assertEqual(capture_range_of([]), (0, -1))
        self.assertEqual(capture_range_of([MadeFrame(1, 1_000, on_screen_ns=700), MadeFrame(2, 1_700, on_screen_ns=-5)]), (1_000, 1_700))


class TrackTests(unittest.TestCase):
    def test_every_track_has_its_events_and_its_description(self) -> None:
        golden = frames()
        alignment = align(golden)
        for track in Track:
            self.assertIn(track, TRACK_DESCRIPTIONS)
            events = list(track_events(track, golden, captures(), alignment, PERIOD_NS))
            self.assertTrue(all(e.track is track for e in events), track)
            if track in COUNTER_TRACKS:
                self.assertTrue(all(isinstance(e, Counter) for e in events), track)
                self.assertGreater(len(events), 380)
            elif track in (Track.FRAME_EVENTS, Track.CAPTURE_EVENTS):
                self.assertTrue(all(isinstance(e, Instant) for e in events), track)
            else:
                self.assertTrue(all(isinstance(e, Slice) for e in events), track)
                self.assertGreater(len(events), 380)

    def test_a_stretch_of_a_run(self) -> None:
        made = [MadeFrame(index, index * 1_000) for index in range(10)]
        self.assertEqual([f.frame_index for f in in_stretch(made, 3_000, 6_000)], [3, 4, 5])
        self.assertEqual([f.frame_index for f in in_stretch(made, None, 2_000)], [0, 1])
        self.assertEqual([f.frame_index for f in in_stretch(made, 8_000, None)], [8, 9])
        self.assertEqual(len(in_stretch(made, None, None)), 10)


if __name__ == "__main__":
    _ = unittest.main()
