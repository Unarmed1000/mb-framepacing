#ifndef MB_FRAMEPACINGDATA_SUMMARYHISTOGRAMBIN_HPP
#define MB_FRAMEPACINGDATA_SUMMARYHISTOGRAMBIN_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacingData
{
  struct SummaryHistogramBin
  {
    double CenterMs{0.0};
    int64_t Count{0};
  };
}

#endif
