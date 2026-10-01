#ifndef MB_FRAMEPACING_DATA_ANALYSIS_SUMMARYPACING_HPP
#define MB_FRAMEPACING_DATA_ANALYSIS_SUMMARYPACING_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/data/analysis/ValueStatistics.hpp>
#include <cstdint>
#include <optional>
#include <string>

namespace MB::FramePacing::Data
{
  //! The refresh, the target the frames are measured against, late frames and the verdict.
  struct SummaryPacing
  {
    //! The display's refresh period (refreshPeriodTicks): a capture card's capture period, or calculated from a camera's frames.
    TimeSpan RefreshPeriod;
    bool RefreshCalculated{false};
    //! The frame time the run is measured against, in whole refreshes (targetFrameTicks).
    TimeSpan TargetFrameTime;
    //! Schedule, TargetFrameTime, PreferredFrameTime, GivenTarget or NativeRefresh.
    std::string Source;
    int64_t LateFrames{0};
    double LateShare{0.0};
    double WorstLateShare{0.0};
    int64_t ErrorFramesWithUnevenDisplay{0};
    int64_t ErrorFramesWithEvenDisplay{0};
    //! None, BadPacing, DeltaTimeJitter or Both.
    std::string Verdict;
    std::optional<double> ExpectedRefreshHz;
    std::optional<ValueStatistics> PacingErrorMs;
    std::optional<ValueStatistics> PredictionErrorMs;
    double RefreshHz{0.0};
    std::optional<double> RefreshDeviation;
    std::optional<bool> MatchesExpectedRefresh;
  };
}

#endif
