#ifndef MB_FRAMEPACING_DATA_ANALYSIS_SUMMARYHISTOGRAMS_HPP
#define MB_FRAMEPACING_DATA_ANALYSIS_SUMMARYHISTOGRAMS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/data/analysis/SummaryHistogram.hpp>

namespace MB::FramePacing::Data
{
  struct SummaryHistograms
  {
    SummaryHistogram AnimationErrorMs;
    SummaryHistogram DisplayDeltaMs;
  };
}

#endif
