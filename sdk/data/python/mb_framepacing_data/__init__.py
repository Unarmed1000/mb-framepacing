# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""mb_framepacing_data: read the data of the mb-framepacing tools in Python.

The capture data (captures.mbcd: every captured frame's times and markers, doc/capture-data-format.md) and the analysis output
(summary.json, captures.csv and run-<id>-frames.csv: every run's statistics and every presented frame, doc/analysis-output-format.md).
Times are 100 ns ticks (TimeSpan ticks); a value a file does not have is None.

    from mb_framepacing_data import find_analysis, read_frames, read_summary

    analysis = find_analysis(capture_folder)
    summary = read_summary(analysis / "summary.json")
    for run in summary.runs:
        frames = read_frames(analysis / run.frames_file)
        errors_ms = [f.animation_error_ticks / 10_000 for f in frames if f.animation_error_ticks is not None]

Standard library only, Python 3.11 or later. Decoding a marker's payload in the capture data (CaptureDataRecord.try_decode_main) uses the
mb_framemarker package.
"""

from .analysis_files import (
    CAPTURES_FILE_NAME,
    DIRECTORY_NAME,
    SUMMARY_FILE_NAME,
    TICKS_PER_MILLISECOND,
    find_analysis,
    frames_file_name,
    ms_to_ticks,
    parse_ticks,
    run_file_prefix,
)
from .capture_data import (
    FILE_NAME as CAPTURE_DATA_FILE_NAME,
)
from .capture_data import (
    FORMAT_VERSION as CAPTURE_DATA_FORMAT_VERSION,
)
from .capture_data import (
    UNKNOWN_TICKS,
    CaptureDataHeader,
    CaptureDataReader,
    CaptureDataRecord,
    CaptureDataStatus,
    DataRect,
    MarkerLocation,
)
from .csv_files import ON_DEMAND_FRAME_TICKS, CaptureCsvRow, FrameRow, read_captures, read_frames
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

__version__ = "0.1.0"

__all__ = [
    "ANALYSIS_FORMAT_VERSION",
    "CAPTURES_FILE_NAME",
    "CAPTURE_DATA_FILE_NAME",
    "CAPTURE_DATA_FORMAT_VERSION",
    "DIRECTORY_NAME",
    "ON_DEMAND_FRAME_TICKS",
    "SUMMARY_FILE_NAME",
    "TICKS_PER_MILLISECOND",
    "UNKNOWN_TICKS",
    "AnalysisSummary",
    "CaptureCsvRow",
    "CaptureDataHeader",
    "CaptureDataReader",
    "CaptureDataRecord",
    "CaptureDataStatus",
    "DataFormatError",
    "DataRect",
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
    "ms_to_ticks",
    "parse_summary",
    "parse_ticks",
    "read_captures",
    "read_frames",
    "read_summary",
    "run_file_prefix",
]
