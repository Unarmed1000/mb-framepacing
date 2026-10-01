#ifndef MB_FRAMEPACING_DATA_ANALYSIS_DETAIL_CSVCELLS_HPP
#define MB_FRAMEPACING_DATA_ANALYSIS_DETAIL_CSVCELLS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Private to the data module: parsing the cells of the CSV files. Every number is a whole one, written as its digits with a '-' in front
// when negative; a time is its 100 ns ticks. An empty cell is a missing value.

#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <mb/framepacing/data/DataFormatError.hpp>
#include <charconv>
#include <cstdint>
#include <optional>
#include <string>
#include <string_view>

namespace MB::FramePacing::Data::Csv
{
  //! A whole number in T's range, all of the text. Throws DataFormatError for anything else: a fraction, an exponent, a '+', a space, an
  //! empty cell.
  template <typename T>
  T ParseInteger(const std::string_view text)
  {
    T value{};
    const char* const first = text.data();
    const char* const end = first + text.size();
    const auto result = std::from_chars(first, end, value);
    if (text.empty() || result.ec != std::errc() || result.ptr != end)
    {
      throw DataFormatError(text.empty() ? std::string("An empty cell where a whole number is required")
                                         : "'" + std::string(text) + "' is not a whole number in its range");
    }
    return value;
  }

  template <typename T>
  std::optional<T> OptionalInteger(const std::string_view text)
  {
    return text.empty() ? std::nullopt : std::optional<T>(ParseInteger<T>(text));
  }

  //! A span from its ticks.
  inline TimeSpan ParseTimeSpan(const std::string_view text)
  {
    return TimeSpan(ParseInteger<int64_t>(text));
  }

  //! A point on a clock from its ticks since the clock's zero.
  inline TickCount64 ParseTickCount64(const std::string_view text)
  {
    return TickCount64(ParseInteger<int64_t>(text));
  }

  //! A marker's 32-bit span (0 to OnDemandFrameTime) from its ticks, as the marker carried it.
  inline TimeSpan32 ParseTimeSpan32(const std::string_view text)
  {
    return TimeSpan32(ParseInteger<uint32_t>(text));
  }

  inline std::optional<TimeSpan> OptionalTimeSpan(const std::string_view text)
  {
    return text.empty() ? std::nullopt : std::optional<TimeSpan>(ParseTimeSpan(text));
  }

  inline std::optional<TickCount64> OptionalTickCount64(const std::string_view text)
  {
    return text.empty() ? std::nullopt : std::optional<TickCount64>(ParseTickCount64(text));
  }

  inline std::optional<TimeSpan32> OptionalTimeSpan32(const std::string_view text)
  {
    return text.empty() ? std::nullopt : std::optional<TimeSpan32>(ParseTimeSpan32(text));
  }
}

#endif
