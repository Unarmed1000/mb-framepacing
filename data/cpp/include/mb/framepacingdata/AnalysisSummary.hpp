#ifndef MB_FRAMEPACINGDATA_ANALYSISSUMMARY_HPP
#define MB_FRAMEPACINGDATA_ANALYSISSUMMARY_HPP
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacingdata/SummaryMarker.hpp>
#include <mb/framepacingdata/SummaryRun.hpp>
#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace MB::FramePacingData
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
}

#endif
