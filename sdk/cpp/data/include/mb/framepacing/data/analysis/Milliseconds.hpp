#ifndef MB_FRAMEPACING_DATA_ANALYSIS_MILLISECONDS_HPP
#define MB_FRAMEPACING_DATA_ANALYSIS_MILLISECONDS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <string_view>

namespace MB::FramePacing::Data
{
  //! The span a CSV milliseconds text ("16.6667") says: exact for the at most four decimals the files have, rounded to the nearest tick
  //! beyond that. Throws DataFormatError for text that is not a number.
  TimeSpan ParseMilliseconds(std::string_view milliseconds);
}

#endif
