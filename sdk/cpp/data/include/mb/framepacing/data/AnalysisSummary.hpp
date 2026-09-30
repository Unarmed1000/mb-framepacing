#ifndef MB_FRAMEPACING_DATA_ANALYSISSUMMARY_HPP
#define MB_FRAMEPACING_DATA_ANALYSISSUMMARY_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/data/SummaryMarker.hpp>
#include <mb/framepacing/data/SummaryRun.hpp>
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
    double CapturePeriodMs{0.0};
    double MeasurementResolutionMs{0.0};
    double ErrorThresholdMs{0.0};
    std::vector<SummaryMarker> Markers;
    std::vector<std::string> Warnings;
    std::vector<SummaryRun> Runs;
  };

  //! Read summary.json (C#'s AnalysisSummary.Read). Allocates; throws DataFormatError for a newer format version or content that is not a
  //! summary.
  AnalysisSummary ReadSummary(const std::filesystem::path& path);

  //! Parse summary.json's text (C#'s AnalysisSummary.Parse). A file without formatVersion is format 1; fields it lacks keep their defaults,
  //! unknown fields are ignored.
  AnalysisSummary ParseSummary(std::string_view json);
}

#endif
