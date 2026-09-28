#ifndef MB_FRAMEPACINGDATA_SUMMARYHISTOGRAMS_HPP
#define MB_FRAMEPACINGDATA_SUMMARYHISTOGRAMS_HPP
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacingdata/SummaryHistogram.hpp>

namespace MB::FramePacingData
{
  struct SummaryHistograms
  {
    SummaryHistogram AnimationErrorMs;
    SummaryHistogram DisplayDeltaMs;
  };
}

#endif
