#ifndef MB_FRAMEPACING_DATA_ANALYSIS_SUMMARYMARKER_HPP
#define MB_FRAMEPACING_DATA_ANALYSIS_SUMMARYMARKER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <string>

namespace MB::FramePacing::Data
{
  //! Where a marker was found: "x,y,width,height" in stored pixels, including the quiet zone.
  struct SummaryMarker
  {
    std::string Bounds;
    double ModuleSizePx{0.0};
  };
}

#endif
