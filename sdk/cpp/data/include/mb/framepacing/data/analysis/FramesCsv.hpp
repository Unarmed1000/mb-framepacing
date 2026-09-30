#ifndef MB_FRAMEPACING_DATA_ANALYSIS_FRAMESCSV_HPP
#define MB_FRAMEPACING_DATA_ANALYSIS_FRAMESCSV_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/data/analysis/FrameRow.hpp>
#include <filesystem>
#include <vector>

namespace MB::FramePacing::Data
{
  //! A run's frames CSV (its name is in summary.json: SummaryRun::FramesFile). Columns are found by name. Allocates; throws
  //! DataFormatError for content it cannot read.
  std::vector<FrameRow> ReadFrames(const std::filesystem::path& path);
}

#endif
