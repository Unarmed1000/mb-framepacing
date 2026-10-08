#ifndef MB_FRAMEPACING_DATA_ANALYSIS_ANALYSISSUMMARY_HPP
#define MB_FRAMEPACING_DATA_ANALYSIS_ANALYSISSUMMARY_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/data/analysis/SummaryMarker.hpp>
#include <mb/framepacing/data/analysis/SummaryRun.hpp>
#include <cstdint>
#include <filesystem>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace MB::FramePacing::Data
{
  //! summary.json: the capture, the analysis settings and every run. Its FormatVersion covers the CSV files it names.
  struct AnalysisSummary
  {
    int32_t FormatVersion{1};
    std::optional<std::string> ToolVersion;
    std::optional<std::string> Experimental;
    //! SingleScanout (a capture card) or Camera.
    std::optional<std::string> Scanout;
    std::optional<std::string> AnalysedUtc;
    std::optional<std::string> CaptureDirectory;
    //! The capture's capture.json as it was when analysed, as JSON text (empty when absent).
    std::string CaptureJson;
    std::optional<std::string> FrameSize;
    //! Device or Host.
    std::optional<std::string> TimeSource;
    //! The capture period (capturePeriodNs).
    NanosecondTimeSpan CapturePeriod;
    //! How precisely a display time is known (measurementResolutionNs); a file without it, or with 0, reads as the capture period.
    NanosecondTimeSpan MeasurementResolution;
    //! The |animation error| above which a frame counts as off (errorThresholdNs).
    NanosecondTimeSpan ErrorThreshold;
    std::vector<SummaryMarker> Markers;
    std::vector<std::string> Warnings;
    std::vector<SummaryRun> Runs;
  };

  //! Read summary.json (C#'s AnalysisSummary.Read). Allocates; throws DataFormatError for a newer format version or content that is not a
  //! summary, std::runtime_error when the file cannot be opened.
  AnalysisSummary ReadSummary(const std::filesystem::path& path);

  //! Parse summary.json's text (C#'s AnalysisSummary.Parse). A file without formatVersion is format 1 and unknown fields are ignored.
  //! Throws DataFormatError for text that is not a summary: the fields every summary has are required (capturePeriodNs,
  //! errorThresholdNs, and a run's runId, hasStartMarker, hasEndMarker, framesFile, counts and statistics; doc/analysis-output-format.md
  //! marks them), and a value must have its field's type and fit its range: a time ("...Ns") is a whole number of nanoseconds. An
  //! optional field the file lacks is empty or 0. A summary from before the nanoseconds (its times in ticks of 100 ns, "...Ticks") lacks
  //! the required fields and is refused.
  AnalysisSummary ParseSummary(std::string_view json);
}

#endif
