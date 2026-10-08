# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""summary.json and the CSVs: the format version, columns by name, times as whole nanoseconds, and content that is refused."""

import tempfile
import unittest
from datetime import UTC, datetime
from pathlib import Path

from ...marker import ON_DEMAND_FRAME_NS as MARKER_ON_DEMAND_FRAME_NS
from .. import (
    DIRECTORY_NAME,
    NS_PER_MILLISECOND,
    ON_DEMAND_FRAME_NS,
    SUMMARY_FILE_NAME,
    CaptureCsvRow,
    DataFormatError,
    FrameRow,
    ValueStatistics,
    find_analysis,
    frames_file_name,
    parse_summary,
    read_captures,
    read_frames,
    read_summary,
    run_file_prefix,
)

MINIMAL_SUMMARY = """
{
  "scanout": "SingleScanout",
  "analysedUtc": "2026-01-01T00:00:00.1234567Z",
  "capturePeriodNs": 16666667,
  "measurementResolutionNs": 16666667,
  "errorThresholdNs": 1000000,
  "runs": []
}
"""

COUNTS = """"counts": { "captures": 10, "decoded": 9, "undecodable": 1, "torn": 0, "notRecorded": 0, "sourceDroppedFrames": 0,
    "missedCaptures": 0, "presentedFrames": 8, "skippedFrameIndices": 0, "droppedFrames": 0, "outOfOrderCaptures": 0, "segments": 1 }"""

VALUES = '{ "count": 3, "min": -1.5, "mean": 0.25, "stdDev": 1.25, "p50": 0, "p95": 2, "p99": 2.5, "max": 3 }'

RUN_START = '"runId": 7, "hasStartMarker": true, "hasEndMarker": false, "framesFile": "run-7-frames.csv"'

FRAME_COLUMNS = "segment,frameIndex,animationNs,firstCaptureIndex,firstSeenNs,onScreenNs,captures,skippedBefore,driftNs,flags"

CAPTURE_COLUMNS = (
    "captureIndex,captureNs,status,kind,runId,frameIndex,animationNs,sourceDropsBefore,missedBefore,syncRunId,syncFrameIndex,hostNs,deviceNs,payloadHex"
)

PACING = """"pacing": { "refreshPeriodNs": 16666667, "refreshCalculated": false, "targetFrameNs": 33333334, "source": "TargetFrameTime",
    "lateFrames": 1, "lateShare": 0.125, "worstLateShare": 0.5, "errorFramesWithUnevenDisplay": 1, "errorFramesWithEvenDisplay": 1,
    "verdict": "Both", "refreshHz": 59.9999988 }"""

HISTOGRAM = '{ "binWidthMs": 0.1, "total": 2, "bins": [ { "centerMs": 0.1, "count": 2 } ] }'


def statistics(without: str = "", values: str = VALUES) -> str:
    """The statistics every run has; one of the six value statistics can be left out, and all of them given other values."""
    names = ("displayDeltaMs", "animationDeltaMs", "animationErrorMs", "absoluteAnimationErrorMs", "driftMs", "onScreenMs")
    text = "".join(f'"{name}": {values}, ' for name in names if name != without)
    tail = '"framesWithAnimationError": 2, "errorPerFrameMs": 0.5, "percentError": 25, "excludedStaticFrames": 0, "uncertainSteps": 0 }'
    return '"statistics": { ' + text + tail


def one_run(*members: str) -> str:
    """A summary whose one run has the given members."""
    return '{ "capturePeriodNs": 16666667, "errorThresholdNs": 1000000, "runs": [ { ' + ", ".join(m for m in members if m) + " } ] }"


def minimal_with(member: str) -> str:
    return MINIMAL_SUMMARY.replace("{", "{ " + member + ",", 1)


def replaced(text: str, old: str, new: str) -> str:
    assert old in text, old
    return text.replace(old, new, 1)


def frames(content: str) -> list[FrameRow]:
    with tempfile.TemporaryDirectory() as folder:
        path = Path(folder) / "run-1-frames.csv"
        _ = path.write_bytes(content.encode("utf-8"))
        return read_frames(path)


def captures(content: str) -> list[CaptureCsvRow]:
    with tempfile.TemporaryDirectory() as folder:
        path = Path(folder) / "captures.csv"
        _ = path.write_bytes(content.encode("utf-8"))
        return read_captures(path)


class SummaryTests(unittest.TestCase):
    def refused(self, text: str, what: str = "") -> None:
        with self.assertRaises(DataFormatError, msg=what):
            _ = parse_summary(text)

    def test_a_summary_without_a_format_version_is_format_one(self) -> None:
        summary = parse_summary(MINIMAL_SUMMARY)
        self.assertEqual(summary.format_version, 1)
        self.assertEqual((summary.runs, summary.markers, summary.warnings), ((), (), ()))
        self.assertEqual(summary.analysed_utc, datetime(2026, 1, 1, 0, 0, 0, 123456, tzinfo=UTC), "a seventh fractional digit is dropped")

    def test_a_newer_format_is_refused(self) -> None:
        with self.assertRaisesRegex(DataFormatError, "update"):
            _ = parse_summary(minimal_with('"formatVersion": 2'))

    def test_format_version_zero_is_format_one(self) -> None:
        self.assertEqual(parse_summary(minimal_with('"formatVersion": 0')).format_version, 1, "as a file without it (C# reads both as 0)")
        self.refused(minimal_with('"formatVersion": -1'))
        self.refused(minimal_with('"formatVersion": 4294967297'), "not format 1 by wrapping in another language")
        self.refused(minimal_with('"formatVersion": 1.0'))

    def test_a_summarys_times_are_whole_nanoseconds(self) -> None:
        summary = parse_summary(MINIMAL_SUMMARY)
        self.assertEqual((summary.capture_period_ns, summary.measurement_resolution_ns, summary.error_threshold_ns), (16_666_667, 16_666_667, 1_000_000))
        without = parse_summary('{ "capturePeriodNs": 16666667, "errorThresholdNs": 1000000 }')
        self.assertEqual(without.measurement_resolution_ns, 16_666_667, "a file without it: the capture period")
        resolution = '"measurementResolutionNs": 16666667'
        zero = parse_summary(replaced(MINIMAL_SUMMARY, resolution, '"measurementResolutionNs": 0'))
        self.assertEqual(zero.measurement_resolution_ns, 16_666_667, "0, as C# reads a file without it")
        self.assertEqual(parse_summary(replaced(MINIMAL_SUMMARY, resolution, '"measurementResolutionNs": 5')).measurement_resolution_ns, 5)

        # Never a fraction, a text or another unit's field
        self.refused(replaced(MINIMAL_SUMMARY, "16666667,", "16666667.5,"))
        self.refused(replaced(MINIMAL_SUMMARY, "16666667,", "16666667.0,"), "a whole number written as a real one")
        self.refused(replaced(MINIMAL_SUMMARY, '"errorThresholdNs": 1000000', '"errorThresholdNs": "1000000"'))
        self.refused(replaced(MINIMAL_SUMMARY, '"errorThresholdNs": 1000000', '"errorThresholdNs": 9223372036854775808'), "beyond 64 bits")
        self.refused(replaced(MINIMAL_SUMMARY, '"errorThresholdNs": 1000000', '"errorThresholdNs": true'))
        with self.assertRaisesRegex(DataFormatError, "capturePeriodNs"):
            _ = parse_summary(replaced(MINIMAL_SUMMARY, "capturePeriodNs", "capturePeriodMs"))

    def test_a_summary_from_before_the_nanoseconds_is_refused(self) -> None:
        # The same format version, its times in ticks of 100 ns under names that end in Ticks: the required ...Ns fields are not there
        old = """
{
  "formatVersion": 1,
  "scanout": "SingleScanout",
  "capturePeriodTicks": 166667,
  "measurementResolutionTicks": 166667,
  "errorThresholdTicks": 10000,
  "runs": []
}
"""
        with self.assertRaisesRegex(DataFormatError, "lacks 'capturePeriodNs'"):
            _ = parse_summary(old)
        with self.assertRaisesRegex(DataFormatError, "lacks 'errorThresholdNs'"):
            _ = parse_summary(replaced(old, "capturePeriodTicks", "capturePeriodNs"))
        # A run's pacing has two more
        with self.assertRaisesRegex(DataFormatError, "pacing lacks 'refreshPeriodNs'"):
            _ = parse_summary(one_run(RUN_START, COUNTS, statistics(), replaced(PACING, "refreshPeriodNs", "refreshPeriodTicks")))
        with self.assertRaisesRegex(DataFormatError, "pacing lacks 'targetFrameNs'"):
            _ = parse_summary(one_run(RUN_START, COUNTS, statistics(), replaced(PACING, "targetFrameNs", "targetFrameTicks")))

    def test_a_run_is_read_with_its_counts_statistics_and_pacing(self) -> None:
        (run,) = parse_summary(one_run(RUN_START, COUNTS, statistics(), PACING)).runs
        self.assertEqual((run.run_id, run.frames_file, run.counts.captures, run.counts.presented_frames), (7, "run-7-frames.csv", 10, 8))
        self.assertEqual((run.statistics.frames_with_animation_error, run.statistics.percent_error), (2, 25.0))
        self.assertEqual(run.statistics.drift_ms, ValueStatistics(3, -1.5, 0.25, 1.25, 0.0, 2.0, 2.5, 0.0, 3.0), "p999 is 0 in a file without it")
        self.assertEqual(run.statistics.cpu_busy_ms, ValueStatistics.empty(), "an optional one the file lacks is empty")
        assert run.pacing is not None
        self.assertEqual((run.pacing.refresh_period_ns, run.pacing.target_frame_ns, run.pacing.source), (16_666_667, 33_333_334, "TargetFrameTime"))
        self.assertIsNone(run.histograms)
        self.assertIsNone(run.start_time_utc)

        self.refused(one_run(RUN_START, COUNTS, statistics(), replaced(PACING, "33333334", "33333334.5")))
        self.refused(one_run(RUN_START, COUNTS, statistics(), replaced(PACING, "refreshPeriodNs", "refreshPeriodMs")))
        self.refused(one_run(RUN_START, COUNTS, statistics(), '"pacing": 1'), "pacing that is a number")

    def test_a_run_without_what_every_run_has_is_not_a_summary(self) -> None:
        self.refused(one_run(RUN_START, statistics()), "no counts")
        self.refused(one_run(RUN_START, COUNTS), "no statistics")
        self.refused(one_run(RUN_START, '"counts": 3', statistics()), "counts that are a number")
        self.refused(one_run(RUN_START, COUNTS, '"statistics": []'), "statistics that are an array")
        self.refused(one_run(RUN_START, COUNTS, statistics("driftMs")), "no drift statistics")
        self.refused(one_run(RUN_START, COUNTS, statistics(values="7")), "statistics that are a number")
        self.refused(one_run(RUN_START, COUNTS, statistics(values=replaced(VALUES, '"p50": 0, ', ""))), "statistics without their median")
        self.refused(one_run(COUNTS, statistics()), "no run id")
        self.refused(one_run('"runId": 7, "hasStartMarker": true, "hasEndMarker": false', COUNTS, statistics()), "no frames file")
        self.refused(one_run('"runId": 7, "hasStartMarker": true, "hasEndMarker": false, "framesFile": null', COUNTS, statistics()), "null is absent")

    def test_histograms_and_the_camera_are_read_whole(self) -> None:
        def histograms(error: str, display: str) -> str:
            return f'"histograms": {{ "animationErrorMs": {error}, "displayDeltaMs": {display} }}'

        (run,) = parse_summary(one_run(RUN_START, COUNTS, statistics(), histograms(HISTOGRAM, HISTOGRAM))).runs
        assert run.histograms is not None
        self.assertEqual((run.histograms.animation_error_ms.bin_width_ms, run.histograms.display_delta_ms.total), (0.1, 2))
        ((bin0),) = run.histograms.animation_error_ms.bins
        self.assertEqual((bin0.center_ms, bin0.count), (0.1, 2))
        self.refused(one_run(RUN_START, COUNTS, statistics(), '"histograms": {}'), "empty histograms")
        self.refused(one_run(RUN_START, COUNTS, statistics(), '"histograms": { "animationErrorMs": 1, "displayDeltaMs": 2 }'))
        self.refused(one_run(RUN_START, COUNTS, statistics(), histograms(HISTOGRAM, '{ "binWidthMs": 0.1, "total": 2 }')), "no bins")
        self.refused(one_run(RUN_START, COUNTS, statistics(), histograms(HISTOGRAM, '{ "binWidthMs": 0.1, "total": 2, "bins": 3 }')))
        self.refused(one_run(RUN_START, COUNTS, statistics(), histograms(HISTOGRAM, '{ "binWidthMs": 0.1, "total": 2, "bins": [ 5 ] }')))

        delay = '"scanoutDelay": { "count": 1, "min": 0, "mean": 0, "stdDev": 0, "p50": 0, "p95": 0, "p99": 0, "max": 0 }, '
        zones = '"framesSeenInBothZones": 4, "tornFrames": 1, "secondZoneOnlyFrames": 0 }'
        (run,) = parse_summary(one_run(RUN_START, COUNTS, statistics(), '"camera": { ' + delay + zones)).runs
        assert run.camera is not None
        self.assertEqual((run.camera.frames_seen_in_both_zones, run.camera.torn_frames, run.camera.scanout_delay.count), (4, 1, 1))
        self.refused(one_run(RUN_START, COUNTS, statistics(), '"camera": { ' + zones), "a camera without its scanout delay")
        self.refused(one_run(RUN_START, COUNTS, statistics(), '"camera": 1'))

    def test_lists_must_be_lists(self) -> None:
        self.refused('{ "capturePeriodNs": 16666667, "errorThresholdNs": 1000000, "runs": { "a": 1 } }')
        self.refused('{ "capturePeriodNs": 16666667, "errorThresholdNs": 1000000, "runs": [ 5 ] }')
        self.refused(minimal_with('"markers": { "bounds": "0,0,1,1", "moduleSizePx": 3 }'))
        self.refused(minimal_with('"markers": [ 3 ]'))
        self.refused(minimal_with('"warnings": "one text"'))
        self.refused(minimal_with('"warnings": [ 1 ]'))
        self.assertEqual(parse_summary(minimal_with('"warnings": [ "one", "two" ]')).warnings, ("one", "two"))
        (marker,) = parse_summary(minimal_with('"markers": [ { "bounds": "0,0,1,1", "moduleSizePx": 3 } ]')).markers
        self.assertEqual((marker.bounds, marker.module_size_px), ("0,0,1,1", 3.0))
        self.assertEqual(parse_summary(minimal_with('"capture": { "width": 1280 }')).capture, {"width": 1280})

    def test_numbers_must_fit_their_fields(self) -> None:
        def start(run_id: str, flag: str = "true", file: str = '"f"') -> str:
            return f'"runId": {run_id}, "hasStartMarker": {flag}, "hasEndMarker": false, "framesFile": {file}'

        self.refused(one_run(start("-1"), COUNTS, statistics()))
        self.refused(one_run(start("4294967296"), COUNTS, statistics()))
        self.assertEqual(parse_summary(one_run(start("4294967295"), COUNTS, statistics())).runs[0].run_id, 4_294_967_295)
        self.refused(one_run(start("1.5"), COUNTS, statistics()), "a fraction for a whole number")
        self.refused(one_run(start("1", flag="1"), COUNTS, statistics()), "a number for a flag")
        self.refused(one_run(start("1", file="5"), COUNTS, statistics()), "a number for a text")
        frames_with_error = '"framesWithAnimationError": 2'
        self.refused(one_run(RUN_START, COUNTS, replaced(statistics(), frames_with_error, '"framesWithAnimationError": 1e300')))
        self.refused(one_run(RUN_START, COUNTS, replaced(statistics(), frames_with_error, '"framesWithAnimationError": 9223372036854775808')))
        self.refused(one_run(RUN_START, COUNTS, replaced(statistics(), frames_with_error, '"framesWithAnimationError": -9223372036854775809')))
        self.refused(one_run(RUN_START, COUNTS, replaced(statistics(), '"errorPerFrameMs": 0.5', '"errorPerFrameMs": "0.5"')), "a text for a number")
        # What JSON has no number for, and Python's parser takes anyway
        for constant in ("NaN", "Infinity", "-Infinity"):
            self.refused(one_run(RUN_START, COUNTS, replaced(statistics(), '"errorPerFrameMs": 0.5', f'"errorPerFrameMs": {constant}')), constant)
        self.refused(one_run(RUN_START, COUNTS, replaced(statistics(), '"errorPerFrameMs": 0.5', '"errorPerFrameMs": 1' + "0" * 400)), "no double")
        (run,) = parse_summary(one_run(RUN_START, COUNTS, replaced(statistics(), '"percentError": 25', '"percentError": 25.5'))).runs
        self.assertEqual(run.statistics.percent_error, 25.5)

    def test_required_fields_are_required(self) -> None:
        self.refused('{ "errorThresholdNs": 1000000 }', "no capture period")
        self.refused('{ "capturePeriodNs": 16666667 }', "no error threshold")
        self.refused('{ "capturePeriodNs": null, "errorThresholdNs": 1000000 }', "null is absent")
        self.refused("[]", "not an object")
        self.refused('{ "capturePeriodNs": ', "not JSON")
        self.refused("", "empty")
        self.refused("null")
        self.refused(replaced(MINIMAL_SUMMARY, "capturePeriodNs", "CapturePeriodNs"), "names are case sensitive")

    def test_times_without_an_offset_are_utc(self) -> None:
        summary = parse_summary(replaced(MINIMAL_SUMMARY, "2026-01-01T00:00:00.1234567Z", "2026-01-01T12:30:00"))
        self.assertEqual(summary.analysed_utc, datetime(2026, 1, 1, 12, 30, tzinfo=UTC))
        offset = parse_summary(replaced(MINIMAL_SUMMARY, "2026-01-01T00:00:00.1234567Z", "2026-01-01T12:30:00.5+02:00"))
        self.assertEqual(offset.analysed_utc, datetime(2026, 1, 1, 10, 30, 0, 500_000, tzinfo=UTC))
        self.refused(replaced(MINIMAL_SUMMARY, "2026-01-01T00:00:00.1234567Z", "yesterday"))
        self.refused(replaced(MINIMAL_SUMMARY, '"2026-01-01T00:00:00.1234567Z"', "5"))

    def test_a_file_is_read_or_said_to_be_missing(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / SUMMARY_FILE_NAME
            _ = path.write_text(MINIMAL_SUMMARY, encoding="utf-8")
            self.assertEqual(read_summary(path).capture_period_ns, 16_666_667)
            missing = Path(folder) / "no-such-folder" / SUMMARY_FILE_NAME
            for read in (read_summary, read_frames, read_captures):
                with self.assertRaises(OSError):
                    _ = read(missing)
            _ = path.write_bytes(b"\xff\xfe not UTF-8")
            with self.assertRaises(DataFormatError):
                _ = read_summary(path)

    def test_file_names_follow_the_run_ids(self) -> None:
        self.assertEqual(frames_file_name(3), "run-3-frames.csv")
        self.assertEqual(frames_file_name(3, 1), "run-3-2-frames.csv")
        self.assertEqual(run_file_prefix(3), "run-3")
        self.assertEqual(run_file_prefix(4_294_967_295, 2), "run-4294967295-3")

    def test_the_analysis_is_found_in_the_folder_or_its_analysis_folder(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            (folder / DIRECTORY_NAME).mkdir()
            self.assertIsNone(find_analysis(folder), "no summary.json anywhere")
            self.assertIsNone(find_analysis(folder / "no-such-folder"))
            _ = (folder / DIRECTORY_NAME / SUMMARY_FILE_NAME).write_text(MINIMAL_SUMMARY, encoding="utf-8")
            self.assertEqual(find_analysis(folder), folder / DIRECTORY_NAME, "a capture folder")
            self.assertEqual(find_analysis(str(folder / DIRECTORY_NAME)), folder / DIRECTORY_NAME, "the analysis folder itself")
            _ = (folder / SUMMARY_FILE_NAME).write_text(MINIMAL_SUMMARY, encoding="utf-8")
            self.assertEqual(find_analysis(folder), folder, "the folder's own summary wins")


class FramesCsvTests(unittest.TestCase):
    def refused(self, content: str, what: str = "") -> None:
        with self.assertRaises(DataFormatError, msg=what):
            _ = frames(content)

    def test_frames_are_read_by_column_name_whatever_the_order(self) -> None:
        text = (
            "frameIndex,newColumn,segment,animationNs,firstCaptureIndex,firstSeenNs,onScreenNs,captures,skippedBefore,driftNs,flags,"
            "cpuBusyNs\r\n"
            "7,x,0,116666669,3,50000001,33333334,2,1,-500000,SkippedBefore|Late,\r\n"
            "\r\n"
            "8,x,1,0,4,0,1,1,0,0,,5\n"
        )
        first, second = frames(text)
        self.assertEqual(
            (first.frame_index, first.segment, first.animation_ns, first.first_seen_ns, first.on_screen_ns, first.drift_ns),
            (7, 0, 116_666_669, 50_000_001, 33_333_334, -500_000),
        )
        self.assertEqual(first.flags, ("SkippedBefore", "Late"))
        self.assertIsNone(first.cpu_busy_ns, "an empty cell")
        self.assertIsNone(first.last_seen_ns, "a column the file lacks")
        self.assertEqual(first.older_frames, ())
        self.assertEqual((second.segment, second.flags, second.cpu_busy_ns), (1, (), 5), "an empty line is skipped, a line may end with \\n")

    def test_every_time_is_read_as_the_nanoseconds_it_is(self) -> None:
        # The values a marker can carry at their limits: nothing between the file and the row converts them
        text = (
            "segment,frameIndex,animationNs,firstCaptureIndex,firstSeenNs,onScreenNs,captures,skippedBefore,displayDeltaNs,"
            "animationDeltaNs,animationErrorNs,driftNs,flags,intendedDisplayNs,markerTargetNs,targetNs,markerPreferredNs,"
            "preferredNs,pacingErrorNs,predictionErrorNs,latenessNs,lastSeenNs,cpuStartNs,cpuBusyNs,frameTimeNs,"
            "cpuWaitNs,olderFrames,mainMarkerFirstSeenNs,scanoutDelayNs\n"
            "2,18446744073709551615,-9223372036854775808,5,9223372036854775807,16666667,1,0,16666666,9223372036854775807,-1,-7,Late,"
            "-9223372036854775808,4294967295,33333334,1,16666667,3,-3,8333333,1234567890123456789,-1234567890123456789,12006034,16666812,4660778,"
            "41@1234567890123456790|40@-5,9007199254740993,-9007199254740993\n"
        )
        (row,) = frames(text)
        self.assertEqual(
            row,
            FrameRow(
                segment=2,
                frame_index=18_446_744_073_709_551_615,
                animation_ns=-9_223_372_036_854_775_808,
                first_capture_index=5,
                first_seen_ns=9_223_372_036_854_775_807,
                on_screen_ns=16_666_667,
                captures=1,
                skipped_before=0,
                display_delta_ns=16_666_666,
                animation_delta_ns=9_223_372_036_854_775_807,
                animation_error_ns=-1,
                drift_ns=-7,
                flags=("Late",),
                intended_display_ns=-9_223_372_036_854_775_808,
                marker_target_ns=4_294_967_295,
                target_ns=33_333_334,
                marker_preferred_ns=1,
                preferred_ns=16_666_667,
                pacing_error_ns=3,
                prediction_error_ns=-3,
                lateness_ns=8_333_333,
                last_seen_ns=1_234_567_890_123_456_789,
                cpu_start_ns=-1_234_567_890_123_456_789,
                cpu_busy_ns=12_006_034,
                frame_time_ns=16_666_812,
                cpu_wait_ns=4_660_778,
                older_frames=((41, 1_234_567_890_123_456_790), (40, -5)),
                main_marker_first_seen_ns=9_007_199_254_740_993,
                scanout_delay_ns=-9_007_199_254_740_993,
            ),
        )

    def test_a_time_that_is_not_whole_nanoseconds_is_refused(self) -> None:
        cells = ("16666666.7", "1e3", " 5", "5 ", "+5", "1_000", "0x10", "NaN", "9223372036854775808", "-9223372036854775809", "", "٥")
        for cell in cells:
            self.refused(f"{FRAME_COLUMNS}\n0,1,{cell},0,0,16666667,1,0,0,\n", f"'{cell}'")
        self.assertEqual(frames(f"{FRAME_COLUMNS}\n0,1,-0,0,0,16666667,1,0,0,\n")[0].animation_ns, 0)
        self.assertEqual(frames(f"{FRAME_COLUMNS}\n0,1,007,0,0,16666667,1,0,0,\n")[0].animation_ns, 7)

    def test_a_frames_csv_from_before_the_nanoseconds_is_refused(self) -> None:
        # Its times are ticks of 100 ns in columns that end in Ticks: the required ...Ns columns are not there
        old = (
            "segment,frameIndex,animationTicks,firstCaptureIndex,firstSeenTicks,onScreenTicks,captures,skippedBefore,displayDeltaTicks,"
            "animationDeltaTicks,animationErrorTicks,driftTicks,flags\n"
            "0,1,166667,0,0,166667,1,0,,,,0,\n"
        )
        with self.assertRaisesRegex(DataFormatError, r"run-1-frames\.csv' line 2: "):
            _ = frames(old)
        # Each required time on its own
        row = "0,1,0,0,0,16666667,1,0,0,"
        self.assertEqual(len(frames(f"{FRAME_COLUMNS}\n{row}\n")), 1, "the row these cases rename one column of")
        for column in ("animationNs", "firstSeenNs", "onScreenNs", "driftNs"):
            self.refused(f"{replaced(FRAME_COLUMNS, column, column.removesuffix('Ns') + 'Ticks')}\n{row}\n", column)

    def test_the_markers_values_must_fit_32_bits(self) -> None:
        def read(cpu_busy: str) -> list[FrameRow]:
            return frames(f"{FRAME_COLUMNS},cpuBusyNs\n0,1,0,0,0,16666667,1,0,0,,{cpu_busy}\n")

        self.assertEqual((ON_DEMAND_FRAME_NS, NS_PER_MILLISECOND), (MARKER_ON_DEMAND_FRAME_NS, 1_000_000), "the marker's own value, and a millisecond")
        self.assertEqual(read("4294967295")[0].cpu_busy_ns, 0xFFFF_FFFF, "the largest: on demand in a frame time")
        self.assertEqual(read("8000000")[0].cpu_busy_ns, 8_000_000)
        self.assertEqual(read("0")[0].cpu_busy_ns, 0)
        for cell in ("4294967296", "-1"):
            with self.assertRaises(DataFormatError):
                _ = read(cell)

    def test_other_content_that_is_not_a_frame_is_refused(self) -> None:
        self.refused("", "an empty file")
        self.assertEqual(frames(FRAME_COLUMNS + "\n\n"), [], "a header and an empty line")
        self.refused(f"{FRAME_COLUMNS}\nx,1,0,0,0,16666667,1,0,0,\n", "a segment that is no number")
        self.refused(f"{FRAME_COLUMNS}\n0,-1,0,0,0,16666667,1,0,0,\n", "a negative frame index")
        self.refused(f"{FRAME_COLUMNS}\n0,18446744073709551616,0,0,0,16666667,1,0,0,\n", "a frame index beyond 64 bits")
        self.refused(f"{FRAME_COLUMNS}\n0,1,0,0,0,16666667,2147483648,0,0,\n", "captures beyond 32 bits")
        self.refused("frameIndex,animationNs\n1,0\n", "a required column the file lacks")
        for older in ("41", "@5", "41@", "x@5", "41@5.5", "41@5|", "41@5||42@6", "|41@5"):
            self.refused(f"{FRAME_COLUMNS},olderFrames\n0,1,0,0,0,16666667,1,0,0,,{older}\n", f"olderFrames '{older}'")
        self.refused(f"{FRAME_COLUMNS}\n0,1,0,0,0,16666667,1,0,0,Late|\n", "an empty flag")
        # The error names the file and the line
        with self.assertRaisesRegex(DataFormatError, r"run-1-frames\.csv' line 4: .*'1\.5'"):
            _ = frames(f"{FRAME_COLUMNS}\n0,1,0,0,0,16666667,1,0,0,\n\n0,2,1.5,0,0,16666667,1,0,0,\n")


class CapturesCsvTests(unittest.TestCase):
    def refused(self, line: str, what: str = "") -> None:
        with self.assertRaises(DataFormatError, msg=what):
            _ = captures(f"{CAPTURE_COLUMNS}\n{line}\n")

    def test_captures_carry_source_drops_missed_refreshes_and_the_sync_marker(self) -> None:
        torn, dropped = captures(
            CAPTURE_COLUMNS + "\r\n4,66666668,Torn,Frame,7,12,200000000,3,1,7,11,70100000,66666668,4D46\r\n5,,NotRecorded,,,,,0,0,,,,,\r\n"
        )
        self.assertEqual(
            torn,
            CaptureCsvRow(
                capture_index=4,
                capture_ns=66_666_668,
                capture_status="Torn",
                kind="Frame",
                run_id=7,
                frame_index=12,
                animation_ns=200_000_000,
                source_drops_before=3,
                missed_before=1,
                sync_run_id=7,
                sync_frame_index=11,
                host_ns=70_100_000,
                device_ns=66_666_668,
                payload=bytes([0x4D, 0x46]),
            ),
        )
        self.assertEqual(
            (dropped.capture_ns, dropped.kind, dropped.sync_frame_index, dropped.source_drops_before, dropped.payload), (None, None, None, 0, None)
        )

    def test_content_that_is_not_a_capture_is_refused(self) -> None:
        (row,) = captures(f"{CAPTURE_COLUMNS}\n4,66666668,Decoded,Frame,4294967295,12,200000000,0,0,4294967295,11,70100000,66666668,4d46\n")
        self.assertEqual((row.run_id, row.sync_run_id, row.payload), (4_294_967_295, 4_294_967_295, b"MF"))
        self.refused("4,66666668,Decoded,Frame,-1,12,200000000,0,0,,,70100000,66666668,4D46", "a run id below 0")
        self.refused("4,66666668,Decoded,Frame,4294967296,12,200000000,0,0,,,70100000,66666668,4D46", "a run id beyond 32 bits")
        self.refused("4,66666668,Torn,Frame,7,12,200000000,0,0,4294967296,11,70100000,66666668,4D46", "a sync run id beyond 32 bits")
        self.refused("4,66666668.5,Decoded,Frame,7,12,200000000,0,0,,,70100000,66666668,4D46", "a fraction")
        self.refused("4,66666668,Decoded,Frame,7,12,200000000,0,0,,,70100000,66666668,4D4", "half a byte")
        self.refused("4,66666668,Decoded,Frame,7,12,200000000,0,0,,,70100000,66666668,4D4G", "no hex digit")
        self.refused("4,66666668,Decoded,Frame,7,12,200000000,0,0,,,70100000,66666668,4D 46", "a space between bytes")
        self.refused("x,66666668,Decoded,Frame,7,12,200000000,0,0,,,70100000,66666668,4D46", "no capture index")
        with self.assertRaises(DataFormatError):
            _ = captures("")

    def test_a_captures_csv_from_before_the_nanoseconds_reads_as_captures_without_times(self) -> None:
        # Only the capture index is required: the times in columns that end in Ticks are columns this reader does not know
        old = (
            "captureIndex,captureTicks,status,kind,runId,frameIndex,animationTicks,sourceDropsBefore,missedBefore,syncRunId,syncFrameIndex,hostTicks,"
            "deviceTicks,payloadHex\n"
            "4,666667,Decoded,Frame,7,12,2000000,0,0,,,701000,666667,4D46\n"
        )
        (row,) = captures(old)
        self.assertEqual((row.capture_index, row.capture_status, row.frame_index, row.payload), (4, "Decoded", 12, b"MF"))
        self.assertEqual((row.capture_ns, row.animation_ns, row.host_ns, row.device_ns), (None, None, None, None))


if __name__ == "__main__":
    _ = unittest.main()
