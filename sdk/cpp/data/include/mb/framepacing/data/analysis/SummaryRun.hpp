#ifndef MB_FRAMEPACING_DATA_ANALYSIS_SUMMARYRUN_HPP
#define MB_FRAMEPACING_DATA_ANALYSIS_SUMMARYRUN_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/data/analysis/SummaryCamera.hpp>
#include <mb/framepacing/data/analysis/SummaryCounts.hpp>
#include <mb/framepacing/data/analysis/SummaryHistograms.hpp>
#include <mb/framepacing/data/analysis/SummaryPacing.hpp>
#include <mb/framepacing/data/analysis/SummaryStatistics.hpp>
#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace MB::FramePacing::Data
{
  //! One measured run. FramesFile is the run's frames CSV, next to summary.json. Times are ISO 8601 text as written.
  struct SummaryRun
  {
    uint32_t RunId{0};
    std::optional<std::string> Name;
    std::optional<std::string> SequenceId;
    std::optional<std::string> StartTimeUtc;
    bool HasStartMarker{false};
    bool HasEndMarker{false};
    std::string FramesFile;
    SummaryCounts Counts;
    SummaryStatistics Statistics;
    std::optional<SummaryPacing> Pacing;
    std::optional<SummaryHistograms> Histograms;
    std::optional<SummaryCamera> Camera;
    std::vector<std::string> Warnings;
  };
}

#endif
