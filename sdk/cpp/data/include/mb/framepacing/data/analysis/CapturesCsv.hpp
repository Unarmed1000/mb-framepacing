#ifndef MB_FRAMEPACING_DATA_ANALYSIS_CAPTURESCSV_HPP
#define MB_FRAMEPACING_DATA_ANALYSIS_CAPTURESCSV_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/data/analysis/CaptureCsvRow.hpp>
#include <filesystem>
#include <vector>

namespace MB::FramePacing::Data
{
  //! captures.csv. Columns are found by name. Allocates; throws DataFormatError for content it cannot read.
  std::vector<CaptureCsvRow> ReadCaptures(const std::filesystem::path& path);
}

#endif
