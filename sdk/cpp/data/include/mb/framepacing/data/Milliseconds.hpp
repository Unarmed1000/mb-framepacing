#ifndef MB_FRAMEPACING_DATA_MILLISECONDS_HPP
#define MB_FRAMEPACING_DATA_MILLISECONDS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>
#include <string_view>

namespace MB::FramePacing::Data
{
  //! The 100 ns ticks of a CSV milliseconds text ("16.6667"): exact for the at most four decimals the files have, rounded to the nearest
  //! tick beyond that. Throws DataFormatError for text that is not a number.
  int64_t ParseTicks(std::string_view milliseconds);
}

#endif
