#ifndef MB_FRAMEPACINGDATA_SUMMARYHISTOGRAM_HPP
#define MB_FRAMEPACINGDATA_SUMMARYHISTOGRAM_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacingdata/SummaryHistogramBin.hpp>
#include <cstdint>
#include <vector>

namespace MB::FramePacingData
{
  //! Bin k covers [(k - 0.5) * width, (k + 0.5) * width) and is listed by its center.
  struct SummaryHistogram
  {
    double BinWidthMs{0.0};
    int64_t Total{0};
    std::vector<SummaryHistogramBin> Bins;
  };
}

#endif
