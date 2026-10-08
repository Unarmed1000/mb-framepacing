# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""mb_framepacing.data: read the data of the mb-framepacing tools in Python.

The capture data (captures.mbcd: every captured frame's times and markers, doc/capture-data-format.md) and the analysis output
(summary.json, captures.csv and run-<id>-frames.csv: every run's statistics and every presented frame, doc/analysis-output-format.md).
Times are whole nanoseconds, plain ints named ..._ns; a value a file does not have is None. The analysis output of tools from before the
files counted in nanoseconds (names that end in Ticks) is not read: its summary.json and frames CSVs lack what is required and are refused.

    from mb_framepacing.data import find_analysis, read_frames, read_summary

    analysis = find_analysis(capture_folder)
    summary = read_summary(analysis / "summary.json")
    for run in summary.runs:
        frames = read_frames(analysis / run.frames_file)
        errors_ms = [f.animation_error_ns / 1_000_000 for f in frames if f.animation_error_ns is not None]

Standard library only, Python 3.12 or later. A marker's payload in the capture data (CaptureDataRecord.try_decode_main) is decoded with
mb_framepacing.marker.
"""

from ..rectangle import Rectangle
from .analysis_files import (
    CAPTURES_FILE_NAME,
    DIRECTORY_NAME,
    NS_PER_MILLISECOND,
    SUMMARY_FILE_NAME,
    find_analysis,
    frames_file_name,
    run_file_prefix,
)
from .capture_data import (
    FILE_NAME as CAPTURE_DATA_FILE_NAME,
)
from .capture_data import (
    FORMAT_VERSION as CAPTURE_DATA_FORMAT_VERSION,
)
from .capture_data import (
    UNKNOWN_NS,
    CaptureDataHeader,
    CaptureDataReader,
    CaptureDataRecord,
    CaptureDataStatus,
    MarkerLocation,
)
from .csv_files import ON_DEMAND_FRAME_NS, CaptureCsvRow, FrameRow, read_captures, read_frames
from .errors import DataFormatError
from .summary import (
    FORMAT_VERSION as ANALYSIS_FORMAT_VERSION,
)
from .summary import (
    AnalysisSummary,
    SummaryCamera,
    SummaryCounts,
    SummaryHistogram,
    SummaryHistogramBin,
    SummaryHistograms,
    SummaryMarker,
    SummaryPacing,
    SummaryRun,
    SummaryStatistics,
    ValueStatistics,
    parse_summary,
    read_summary,
)

__all__ = [
    "ANALYSIS_FORMAT_VERSION",
    "CAPTURES_FILE_NAME",
    "CAPTURE_DATA_FILE_NAME",
    "CAPTURE_DATA_FORMAT_VERSION",
    "DIRECTORY_NAME",
    "NS_PER_MILLISECOND",
    "ON_DEMAND_FRAME_NS",
    "SUMMARY_FILE_NAME",
    "UNKNOWN_NS",
    "AnalysisSummary",
    "CaptureCsvRow",
    "CaptureDataHeader",
    "CaptureDataReader",
    "CaptureDataRecord",
    "CaptureDataStatus",
    "DataFormatError",
    "Rectangle",
    "FrameRow",
    "MarkerLocation",
    "SummaryCamera",
    "SummaryCounts",
    "SummaryHistogram",
    "SummaryHistogramBin",
    "SummaryHistograms",
    "SummaryMarker",
    "SummaryPacing",
    "SummaryRun",
    "SummaryStatistics",
    "ValueStatistics",
    "find_analysis",
    "frames_file_name",
    "parse_summary",
    "read_captures",
    "read_frames",
    "read_summary",
    "run_file_prefix",
]
