# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""The golden data's digest, as the C# library's tests compute it (sdk/csharp/data/UnitTest/source/DataDigest.cs): counts and sums of what a
reader reads from every file. It must equal digest.json, so this library reads the same values as the C# one."""

from collections import Counter
from collections.abc import Callable, Iterable
from pathlib import Path

from .. import (
    CAPTURE_DATA_FILE_NAME,
    CAPTURES_FILE_NAME,
    DIRECTORY_NAME,
    SUMMARY_FILE_NAME,
    AnalysisSummary,
    CaptureDataReader,
    FrameRow,
    Rectangle,
    read_captures,
    read_frames,
    read_summary,
)
from ..summary import JsonValue

FRAME_COLUMNS: list[tuple[str, Callable[[FrameRow], int | None]]] = [
    ("segment", lambda r: r.segment),
    ("frameIndex", lambda r: r.frame_index),
    ("animationNs", lambda r: r.animation_ns),
    ("firstCaptureIndex", lambda r: r.first_capture_index),
    ("firstSeenNs", lambda r: r.first_seen_ns),
    ("onScreenNs", lambda r: r.on_screen_ns),
    ("captures", lambda r: r.captures),
    ("skippedBefore", lambda r: r.skipped_before),
    ("displayDeltaNs", lambda r: r.display_delta_ns),
    ("animationDeltaNs", lambda r: r.animation_delta_ns),
    ("animationErrorNs", lambda r: r.animation_error_ns),
    ("driftNs", lambda r: r.drift_ns),
    ("intendedDisplayNs", lambda r: r.intended_display_ns),
    ("markerTargetNs", lambda r: r.marker_target_ns),
    ("targetNs", lambda r: r.target_ns),
    ("markerPreferredNs", lambda r: r.marker_preferred_ns),
    ("preferredNs", lambda r: r.preferred_ns),
    ("pacingErrorNs", lambda r: r.pacing_error_ns),
    ("predictionErrorNs", lambda r: r.prediction_error_ns),
    ("latenessNs", lambda r: r.lateness_ns),
    ("lastSeenNs", lambda r: r.last_seen_ns),
    ("cpuStartNs", lambda r: r.cpu_start_ns),
    ("cpuBusyNs", lambda r: r.cpu_busy_ns),
    ("frameTimeNs", lambda r: r.frame_time_ns),
    ("cpuWaitNs", lambda r: r.cpu_wait_ns),
    ("mainMarkerFirstSeenNs", lambda r: r.main_marker_first_seen_ns),
    ("scanoutDelayNs", lambda r: r.scanout_delay_ns),
]


def compute(clip: Path) -> dict[str, JsonValue]:
    analysis = clip / DIRECTORY_NAME
    summary = read_summary(analysis / SUMMARY_FILE_NAME)
    return {
        "captureData": _capture_data(clip / CAPTURE_DATA_FILE_NAME),
        "summary": _summary(summary),
        "frames": [_frames(analysis / run.frames_file) for run in summary.runs],
        "captures": _captures(analysis / CAPTURES_FILE_NAME),
    }


def _rect(rect: Rectangle) -> list[JsonValue]:
    return [rect.x, rect.y, rect.width, rect.height]


def _counts(names: Iterable[str]) -> dict[str, JsonValue]:
    return dict(Counter(names))


def _capture_data(path: Path) -> dict[str, JsonValue]:
    with CaptureDataReader(path) as reader:
        header = reader.header
        records = reader.read_all()
    decoded = [d for d in (r.try_decode_main() for r in records) if d is not None]
    return {
        "header": {
            "width": header.width,
            "height": header.height,
            "frameRateNumerator": header.frame_rate_numerator,
            "frameRateDenominator": header.frame_rate_denominator,
            "sourceWidth": header.source_width,
            "sourceHeight": header.source_height,
            "region": _rect(header.region),
            "syncRegion": _rect(header.sync_region),
            "markers": [{"bounds": _rect(m.bounds), "moduleSizePx": m.module_size_px} for m in header.markers],
            "framesStored": header.frames_stored,
            "camera": header.camera,
        },
        "recordCount": len(records),
        "captureIndexSum": sum(r.capture_index for r in records),
        "hostNsSum": sum(r.host_ns for r in records),
        "deviceNsCount": sum(1 for r in records if r.has_device_ns),
        "deviceNsSum": sum(r.device_ns for r in records if r.has_device_ns),
        "sourceDropsSum": sum(r.source_drops for r in records),
        "statusCounts": _counts(r.capture_status.name.title() for r in records),
        "mainByteCount": sum(len(r.main_bytes or b"") for r in records),
        "secondByteCount": sum(len(r.second_bytes or b"") for r in records),
        "decodedMainPayloads": len(decoded),
        "frameIndexSum": sum(payload.frame_index for payload, _ in decoded),
        "animationNsSum": sum(payload.animation_ns for payload, _ in decoded),
    }


def _summary(summary: AnalysisSummary) -> dict[str, JsonValue]:
    return {
        "formatVersion": summary.format_version,
        "scanout": summary.scanout,
        "timeSource": summary.time_source,
        "capturePeriodNs": summary.capture_period_ns,
        "measurementResolutionNs": summary.measurement_resolution_ns,
        "errorThresholdNs": summary.error_threshold_ns,
        "markerCount": len(summary.markers),
        "warningCount": len(summary.warnings),
        "runs": [
            {
                "runId": run.run_id,
                "sequenceId": run.sequence_id,
                "framesFile": run.frames_file,
                "hasStartMarker": run.has_start_marker,
                "hasEndMarker": run.has_end_marker,
                "presentedFrames": run.counts.presented_frames,
                "droppedFrames": run.counts.dropped_frames,
                "sourceDroppedFrames": run.counts.source_dropped_frames,
                "missedCaptures": run.counts.missed_captures,
                "captures": run.counts.captures,
                "displayDeltaCount": run.statistics.display_delta_ms.count,
                "displayDeltaP50": run.statistics.display_delta_ms.p50,
                "animationErrorMax": run.statistics.animation_error_ms.max,
                "averageFps": run.statistics.average_fps,
                "excludedStaticFrames": run.statistics.excluded_static_frames,
                "uncertainSteps": run.statistics.uncertain_steps,
                "cpuBusyCount": run.statistics.cpu_busy_ms.count,
                "pacingSource": run.pacing.source if run.pacing else None,
                "lateFrames": run.pacing.late_frames if run.pacing else 0,
                "refreshPeriodNs": run.pacing.refresh_period_ns if run.pacing else 0,
                "targetFrameNs": run.pacing.target_frame_ns if run.pacing else 0,
                "histogramBins": len(run.histograms.animation_error_ms.bins) if run.histograms else 0,
            }
            for run in summary.runs
        ],
    }


def _frames(path: Path) -> dict[str, JsonValue]:
    rows = read_frames(path)
    columns: dict[str, JsonValue] = {}
    for name, value in FRAME_COLUMNS:
        values = [v for v in (value(r) for r in rows) if v is not None]
        columns[name] = {"count": len(values), "sum": sum(values)}
    return {
        "file": path.name,
        "rowCount": len(rows),
        "columns": columns,
        "flagCounts": _counts(flag for row in rows for flag in row.flags),
        "olderFrameCount": sum(len(row.older_frames) for row in rows),
        "olderFrameIndexSum": sum(index for row in rows for index, _ in row.older_frames),
    }


def _captures(path: Path) -> dict[str, JsonValue]:
    rows = read_captures(path)
    return {
        "rowCount": len(rows),
        "statusCounts": _counts(r.capture_status for r in rows),
        "kindCounts": _counts(r.kind for r in rows if r.kind is not None),
        "captureNsSum": sum(r.capture_ns or 0 for r in rows),
        "frameIndexSum": sum(r.frame_index or 0 for r in rows),
        "hostNsSum": sum(r.host_ns or 0 for r in rows),
        "sourceDropsSum": sum(r.source_drops_before for r in rows),
        "missedSum": sum(r.missed_before for r in rows),
        "syncCount": sum(1 for r in rows if r.sync_frame_index is not None),
        "syncFrameIndexSum": sum(r.sync_frame_index or 0 for r in rows),
        "payloadByteCount": sum(len(r.payload or b"") for r in rows),
    }
