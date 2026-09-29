# SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
# SPDX-License-Identifier: BSD-3-Clause

"""summary.json (doc/analysis-output-format.md): the capture, the analysis settings and every run's counts, statistics, pacing and
histograms. Its formatVersion covers the CSV files it names; a file without it is format 1, a newer one is refused. Fields a file lacks
read as None (or an empty statistics), and fields this reader does not know are ignored."""

import json
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import TypeAlias, cast

from .errors import DataFormatError

FORMAT_VERSION = 1
"""The analysis output format this library reads."""

JsonValue: TypeAlias = "None | bool | int | float | str | list[JsonValue] | dict[str, JsonValue]"


class _Object:
    """Typed access to a JSON object's fields."""

    def __init__(self, value: JsonValue, where: str) -> None:
        if not isinstance(value, dict):
            raise DataFormatError(f"{where} is not a JSON object")
        self._fields: dict[str, JsonValue] = value
        self._where: str = where

    def has(self, name: str) -> bool:
        return self._fields.get(name) is not None

    def value(self, name: str) -> JsonValue:
        return self._fields.get(name)

    def number(self, name: str) -> float:
        value = self._fields.get(name)
        if isinstance(value, bool) or not isinstance(value, int | float):
            raise DataFormatError(f"{self._where}.{name} is not a number")
        return float(value)

    def optional_number(self, name: str) -> float | None:
        return self.number(name) if self.has(name) else None

    def integer(self, name: str, default: int | None = None) -> int:
        value = self._fields.get(name)
        if value is None and default is not None:
            return default
        if isinstance(value, bool) or not isinstance(value, int):
            raise DataFormatError(f"{self._where}.{name} is not an integer")
        return value

    def boolean(self, name: str) -> bool:
        value = self._fields.get(name)
        if not isinstance(value, bool):
            raise DataFormatError(f"{self._where}.{name} is not true or false")
        return value

    def optional_boolean(self, name: str) -> bool | None:
        return self.boolean(name) if self.has(name) else None

    def text(self, name: str) -> str:
        value = self._fields.get(name)
        if not isinstance(value, str):
            raise DataFormatError(f"{self._where}.{name} is not a string")
        return value

    def optional_text(self, name: str) -> str | None:
        return self.text(name) if self.has(name) else None

    def optional_time(self, name: str) -> datetime | None:
        return _parse_time(self.text(name)) if self.has(name) else None

    def child(self, name: str) -> "_Object":
        return _Object(self._fields.get(name), f"{self._where}.{name}")

    def optional_child(self, name: str) -> "_Object | None":
        return self.child(name) if self.has(name) else None

    def items(self, name: str) -> list[JsonValue]:
        value = self._fields.get(name)
        if value is None:
            return []
        if not isinstance(value, list):
            raise DataFormatError(f"{self._where}.{name} is not an array")
        return value

    def texts(self, name: str) -> tuple[str, ...]:
        return tuple(item for item in self.items(name) if isinstance(item, str))


@dataclass(frozen=True)
class ValueStatistics:
    """The statistics of one quantity, in milliseconds: percentiles by linear interpolation between the closest ranks, the sample standard
    deviation. count 0 = no value (every other field is then 0)."""

    count: int
    min: float
    mean: float
    std_dev: float
    p50: float
    p95: float
    p99: float
    p999: float
    max: float

    @staticmethod
    def empty() -> "ValueStatistics":
        return ValueStatistics(0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0)


@dataclass(frozen=True)
class SummaryCounts:
    """What a run's captures held."""

    captures: int
    decoded: int
    undecodable: int
    torn: int
    not_recorded: int
    source_drop_events: int
    presented_frames: int
    skipped_frame_indices: int
    dropped_frames: int
    out_of_order_captures: int
    segments: int


@dataclass(frozen=True)
class SummaryStatistics:
    """A run's statistics: the animation time steps and the animation error of the frames with one, the display time steps and frame
    rates of the frames that count toward the frame rate (excluded_static_frames display time steps are a static frame's time on screen and
    left out), every frame's drift and time on screen, and the CPU side from the markers (CPU busy, frametime, CPU wait; count 0 when the
    markers carry none)."""

    display_delta_ms: ValueStatistics
    animation_delta_ms: ValueStatistics
    animation_error_ms: ValueStatistics
    absolute_animation_error_ms: ValueStatistics
    drift_ms: ValueStatistics
    on_screen_ms: ValueStatistics
    frames_with_animation_error: int
    error_per_frame_ms: float
    percent_error: float
    average_fps: float
    one_percent_low_fps: float | None
    point_one_percent_low_fps: float | None
    excluded_static_frames: int
    uncertain_steps: int
    cpu_busy_ms: ValueStatistics
    frame_time_ms: ValueStatistics
    cpu_wait_ms: ValueStatistics


@dataclass(frozen=True)
class SummaryPacing:
    """The refresh, the target the frames are measured against (source: Schedule, TargetFrameTime, PreferredFrameTime, GivenTarget or
    NativeRefresh), late
    frames and the verdict (None, BadPacing, DeltaTimeJitter or Both)."""

    refresh_period_ms: float
    refresh_calculated: bool
    target_frame_ms: float
    source: str
    late_frames: int
    late_share: float
    worst_late_share: float
    error_frames_with_uneven_display: int
    error_frames_with_even_display: int
    verdict: str
    expected_refresh_hz: float | None
    pacing_error_ms: ValueStatistics | None
    prediction_error_ms: ValueStatistics | None
    refresh_hz: float
    refresh_deviation: float | None
    matches_expected_refresh: bool | None


@dataclass(frozen=True)
class SummaryHistogramBin:
    center_ms: float
    count: int


@dataclass(frozen=True)
class SummaryHistogram:
    """Bin k covers [(k - 0.5) * width, (k + 0.5) * width) and is listed by its center."""

    bin_width_ms: float
    total: int
    bins: tuple[SummaryHistogramBin, ...]


@dataclass(frozen=True)
class SummaryHistograms:
    animation_error_ms: SummaryHistogram
    display_delta_ms: SummaryHistogram


@dataclass(frozen=True)
class SummaryCamera:
    """EXPERIMENTAL camera captures: the scanout delay between the zones and the tears the camera saw."""

    scanout_delay: ValueStatistics
    frames_seen_in_both_zones: int
    torn_frames: int
    second_zone_only_frames: int


@dataclass(frozen=True)
class SummaryRun:
    """One measured run. frames_file is the run's frames CSV, next to summary.json."""

    run_id: int
    name: str | None
    sequence_id: str | None
    start_time_utc: datetime | None
    has_start_marker: bool
    has_end_marker: bool
    frames_file: str
    counts: SummaryCounts
    statistics: SummaryStatistics
    pacing: SummaryPacing | None
    histograms: SummaryHistograms | None
    camera: SummaryCamera | None
    warnings: tuple[str, ...]


@dataclass(frozen=True)
class SummaryMarker:
    """Where a marker was found: 'x,y,width,height' in stored pixels, including the quiet zone."""

    bounds: str
    module_size_px: float


@dataclass(frozen=True)
class AnalysisSummary:
    """summary.json. scanout is 'SingleScanout' (a capture card) or 'Camera'; time_source 'Device' or 'Host'; capture is the capture's
    capture.json as it was when analysed."""

    format_version: int
    tool_version: str | None
    experimental: str | None
    scanout: str | None
    analysed_utc: datetime | None
    capture_directory: str | None
    capture: dict[str, JsonValue] | None
    frame_size: str | None
    time_source: str | None
    capture_period_ms: float
    measurement_resolution_ms: float
    error_threshold_ms: float
    markers: tuple[SummaryMarker, ...]
    warnings: tuple[str, ...]
    runs: tuple[SummaryRun, ...]


def read_summary(path: str | Path) -> AnalysisSummary:
    """Read summary.json. Raises DataFormatError for a newer format version or content that is not a summary."""
    return parse_summary(Path(path).read_text(encoding="utf-8"))


def parse_summary(text: str) -> AnalysisSummary:
    root = _Object(cast(JsonValue, json.loads(text)), "summary.json")
    version = root.integer("formatVersion", default=1)
    if version > FORMAT_VERSION:
        raise DataFormatError(
            f"The analysis output has format version {version}, newer than this reader reads ({FORMAT_VERSION}): update the tools or the library"
        )
    capture = root.value("capture")
    return AnalysisSummary(
        format_version=version,
        tool_version=root.optional_text("toolVersion"),
        experimental=root.optional_text("experimental"),
        scanout=root.optional_text("scanout"),
        analysed_utc=root.optional_time("analysedUtc"),
        capture_directory=root.optional_text("captureDirectory"),
        capture=capture if isinstance(capture, dict) else None,
        frame_size=root.optional_text("frameSize"),
        time_source=root.optional_text("timeSource"),
        capture_period_ms=root.number("capturePeriodMs"),
        measurement_resolution_ms=root.optional_number("measurementResolutionMs") or root.number("capturePeriodMs"),
        error_threshold_ms=root.number("errorThresholdMs"),
        markers=tuple(_marker(_Object(item, "markers[]")) for item in root.items("markers")),
        warnings=root.texts("warnings"),
        runs=tuple(_run(_Object(item, "runs[]")) for item in root.items("runs")),
    )


def _marker(value: _Object) -> SummaryMarker:
    return SummaryMarker(bounds=value.text("bounds"), module_size_px=value.number("moduleSizePx"))


def _run(value: _Object) -> SummaryRun:
    return SummaryRun(
        run_id=value.integer("runId"),
        name=value.optional_text("name"),
        sequence_id=value.optional_text("sequenceId"),
        start_time_utc=value.optional_time("startTimeUtc"),
        has_start_marker=value.boolean("hasStartMarker"),
        has_end_marker=value.boolean("hasEndMarker"),
        frames_file=value.text("framesFile"),
        counts=_counts(value.child("counts")),
        statistics=_statistics(value.child("statistics")),
        pacing=_pacing(pacing) if (pacing := value.optional_child("pacing")) is not None else None,
        histograms=_histograms(histograms) if (histograms := value.optional_child("histograms")) is not None else None,
        camera=_camera(camera) if (camera := value.optional_child("camera")) is not None else None,
        warnings=value.texts("warnings"),
    )


def _counts(value: _Object) -> SummaryCounts:
    return SummaryCounts(
        captures=value.integer("captures"),
        decoded=value.integer("decoded"),
        undecodable=value.integer("undecodable"),
        torn=value.integer("torn"),
        not_recorded=value.integer("notRecorded"),
        source_drop_events=value.integer("sourceDropEvents"),
        presented_frames=value.integer("presentedFrames"),
        skipped_frame_indices=value.integer("skippedFrameIndices"),
        dropped_frames=value.integer("droppedFrames"),
        out_of_order_captures=value.integer("outOfOrderCaptures"),
        segments=value.integer("segments"),
    )


def _statistics(value: _Object) -> SummaryStatistics:
    def stats(name: str) -> ValueStatistics:
        return _value_statistics(value.optional_child(name))

    return SummaryStatistics(
        display_delta_ms=stats("displayDeltaMs"),
        animation_delta_ms=stats("animationDeltaMs"),
        animation_error_ms=stats("animationErrorMs"),
        absolute_animation_error_ms=stats("absoluteAnimationErrorMs"),
        drift_ms=stats("driftMs"),
        on_screen_ms=stats("onScreenMs"),
        frames_with_animation_error=value.integer("framesWithAnimationError"),
        error_per_frame_ms=value.number("errorPerFrameMs"),
        percent_error=value.number("percentError"),
        average_fps=value.optional_number("averageFps") or 0.0,
        one_percent_low_fps=value.optional_number("onePercentLowFps"),
        point_one_percent_low_fps=value.optional_number("pointOnePercentLowFps"),
        excluded_static_frames=value.integer("excludedStaticFrames"),
        uncertain_steps=value.integer("uncertainSteps"),
        cpu_busy_ms=stats("cpuBusyMs"),
        frame_time_ms=stats("frameTimeMs"),
        cpu_wait_ms=stats("cpuWaitMs"),
    )


def _pacing(value: _Object) -> SummaryPacing:
    def stats(name: str) -> ValueStatistics | None:
        child = value.optional_child(name)
        return _value_statistics(child) if child is not None else None

    return SummaryPacing(
        refresh_period_ms=value.number("refreshPeriodMs"),
        refresh_calculated=value.boolean("refreshCalculated"),
        target_frame_ms=value.number("targetFrameMs"),
        source=value.text("source"),
        late_frames=value.integer("lateFrames"),
        late_share=value.number("lateShare"),
        worst_late_share=value.number("worstLateShare"),
        error_frames_with_uneven_display=value.integer("errorFramesWithUnevenDisplay"),
        error_frames_with_even_display=value.integer("errorFramesWithEvenDisplay"),
        verdict=value.text("verdict"),
        expected_refresh_hz=value.optional_number("expectedRefreshHz"),
        pacing_error_ms=stats("pacingErrorMs"),
        prediction_error_ms=stats("predictionErrorMs"),
        refresh_hz=value.optional_number("refreshHz") or 0.0,
        refresh_deviation=value.optional_number("refreshDeviation"),
        matches_expected_refresh=value.optional_boolean("matchesExpectedRefresh"),
    )


def _histogram(value: _Object) -> SummaryHistogram:
    bins = tuple(SummaryHistogramBin(center_ms=(b := _Object(item, "bins[]")).number("centerMs"), count=b.integer("count")) for item in value.items("bins"))
    return SummaryHistogram(bin_width_ms=value.number("binWidthMs"), total=value.integer("total"), bins=bins)


def _histograms(value: _Object) -> SummaryHistograms:
    return SummaryHistograms(animation_error_ms=_histogram(value.child("animationErrorMs")), display_delta_ms=_histogram(value.child("displayDeltaMs")))


def _camera(value: _Object) -> SummaryCamera:
    return SummaryCamera(
        scanout_delay=_value_statistics(value.child("scanoutDelay")),
        frames_seen_in_both_zones=value.integer("framesSeenInBothZones"),
        torn_frames=value.integer("tornFrames"),
        second_zone_only_frames=value.integer("secondZoneOnlyFrames"),
    )


def _value_statistics(value: _Object | None) -> ValueStatistics:
    if value is None:
        return ValueStatistics.empty()
    return ValueStatistics(
        count=value.integer("count"),
        min=value.number("min"),
        mean=value.number("mean"),
        std_dev=value.number("stdDev"),
        p50=value.number("p50"),
        p95=value.number("p95"),
        p99=value.number("p99"),
        p999=value.optional_number("p999") or 0.0,
        max=value.number("max"),
    )


def _parse_time(text: str) -> datetime:
    """An ISO 8601 time as .NET writes it ('2026-09-28T20:12:40.4753868Z'): Python keeps microseconds, so a seventh fractional digit is
    dropped."""
    if text.endswith("Z"):
        text = text[:-1] + "+00:00"
    if "." in text:
        head, _, rest = text.partition(".")
        digits = len(rest) - len(rest.lstrip("0123456789"))
        text = head + "." + rest[:digits][:6].ljust(6, "0") + rest[digits:]
    return datetime.fromisoformat(text)
