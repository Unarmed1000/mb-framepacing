# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""summary.json and the CSVs: the format version, columns by name, whole ticks."""

import tempfile
import unittest
from pathlib import Path

from .. import DataFormatError, frames_file_name, parse_summary, parse_ticks, read_captures, read_frames

MINIMAL_SUMMARY = """
{
  "scanout": "SingleScanout",
  "analysedUtc": "2026-01-01T00:00:00.1234567Z",
  "capturePeriodMs": 16.6667,
  "measurementResolutionMs": 16.6667,
  "errorThresholdMs": 1,
  "runs": []
}
"""


class AnalysisOutputTests(unittest.TestCase):
    def test_a_summary_without_a_format_version_is_format_one(self) -> None:
        summary = parse_summary(MINIMAL_SUMMARY)
        self.assertEqual(summary.format_version, 1)
        self.assertEqual((summary.runs, summary.markers, summary.warnings), ((), (), ()))
        self.assertIsNotNone(summary.analysed_utc)

    def test_a_newer_format_is_refused(self) -> None:
        with self.assertRaisesRegex(DataFormatError, "update"):
            _ = parse_summary(MINIMAL_SUMMARY.replace("{", '{ "formatVersion": 2,', 1))

    def test_file_names_follow_the_run_ids(self) -> None:
        self.assertEqual(frames_file_name(3), "run-3-frames.csv")
        self.assertEqual(frames_file_name(3, 1), "run-3-2-frames.csv")

    def test_frames_are_read_by_column_name_whatever_the_order(self) -> None:
        text = (
            "frameIndex,newColumn,segment,animationMs,firstCaptureIndex,firstSeenMs,onScreenMs,captures,skippedBefore,driftMs,flags,cpuBusyMs\r\n"
            "7,x,0,116.6667,3,50,33.3333,2,1,-0.5,SkippedBefore|Late,\r\n"
        )
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "run-1-frames.csv"
            _ = path.write_bytes(text.encode("utf-8"))
            (row,) = read_frames(path)
        self.assertEqual((row.frame_index, row.segment, row.animation_ticks, row.on_screen_ticks, row.drift_ticks), (7, 0, 1_166_667, 333_333, -5_000))
        self.assertEqual(row.flags, ("SkippedBefore", "Late"))
        self.assertIsNone(row.cpu_busy_ticks, "an empty cell")
        self.assertIsNone(row.last_seen_ticks, "a column the file lacks")

    def test_captures_carry_source_drops_missed_refreshes_and_the_sync_marker(self) -> None:
        text = (
            "captureIndex,captureMs,status,kind,runId,frameIndex,animationMs,sourceDropsBefore,missedBefore,syncRunId,syncFrameIndex,hostMs,deviceMs,payloadHex\r\n"
            "4,66.6667,Torn,Frame,7,12,200,3,1,7,11,70.1,66.6667,4D46\r\n"
            "5,,NotRecorded,,,,,0,0,,,,,\r\n"
        )
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "captures.csv"
            _ = path.write_bytes(text.encode("utf-8"))
            torn, dropped = read_captures(path)
        self.assertEqual((torn.status, torn.frame_index, torn.sync_run_id, torn.sync_frame_index), ("Torn", 12, 7, 11))
        self.assertEqual((torn.source_drops_before, torn.missed_before, torn.payload), (3, 1, bytes([0x4D, 0x46])))
        self.assertEqual((dropped.capture_ticks, dropped.sync_frame_index, dropped.source_drops_before), (None, None, 0))

    def test_milliseconds_are_whole_ticks(self) -> None:
        self.assertEqual(parse_ticks("16.6667"), 166_667)
        self.assertEqual(parse_ticks("-0.0003"), -3)
        self.assertEqual(parse_ticks("12345678.9012"), 123_456_789_012)


if __name__ == "__main__":
    _ = unittest.main()
