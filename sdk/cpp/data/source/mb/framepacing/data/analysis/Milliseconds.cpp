// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/data/DataFormatError.hpp>
#include <mb/framepacing/data/analysis/Milliseconds.hpp>
#include <string>
#include "detail/CsvCells.hpp"

namespace MB::FramePacing::Data
{
  TimeSpan ParseMilliseconds(const std::string_view milliseconds)
  {
    // Decimal digits, not floating point: exact for any value the files hold
    std::string_view text = milliseconds;
    const bool negative = !text.empty() && text.front() == '-';
    if (negative)
    {
      text.remove_prefix(1);
    }
    const std::size_t point = text.find('.');
    const std::string_view whole = text.substr(0, point);
    const std::string_view fraction = point == std::string_view::npos ? std::string_view() : text.substr(point + 1);
    const auto isDigits = [](const std::string_view digits) { return digits.find_first_not_of("0123456789") == std::string_view::npos; };
    if (whole.empty() || !isDigits(whole) || !isDigits(fraction) || (point != std::string_view::npos && fraction.empty()))
    {
      throw DataFormatError("'" + std::string(milliseconds) + "' is not a milliseconds value");
    }
    int64_t ticks = Detail::ParseInteger<int64_t>(whole) * TimeSpan::TicksPerMillisecond;
    int64_t scale = TimeSpan::TicksPerMillisecond / 10;
    for (std::size_t i = 0; i < fraction.size() && scale > 0; ++i, scale /= 10)
    {
      ticks += static_cast<int64_t>(fraction[i] - '0') * scale;
    }
    // A fifth decimal rounds to the nearest tick
    if (fraction.size() > 4 && fraction[4] >= '5')
    {
      ++ticks;
    }
    return TimeSpan(negative ? -ticks : ticks);
  }
}
