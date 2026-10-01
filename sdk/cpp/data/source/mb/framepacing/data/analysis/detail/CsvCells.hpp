#ifndef MB_FRAMEPACING_DATA_ANALYSIS_DETAIL_CSVCELLS_HPP
#define MB_FRAMEPACING_DATA_ANALYSIS_DETAIL_CSVCELLS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Private to the data module: parsing the cells of the CSV files (an empty cell is a missing value).

#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <mb/framepacing/data/DataFormatError.hpp>
#include <mb/framepacing/data/analysis/Milliseconds.hpp>
#include <charconv>
#include <cstdint>
#include <optional>
#include <string>
#include <string_view>

namespace MB::FramePacing::Data::Detail
{
  //! A whole number, all of the text. Throws DataFormatError for anything else.
  template <typename T>
  T ParseInteger(const std::string_view text)
  {
    T value{};
    const char* const first = text.data();
    const char* const end = first + text.size();
    const auto result = std::from_chars(first, end, value);
    if (text.empty() || result.ec != std::errc() || result.ptr != end)
    {
      throw DataFormatError("'" + std::string(text) + "' is not an integer");
    }
    return value;
  }

  template <typename T>
  std::optional<T> OptionalInteger(const std::string_view text)
  {
    return text.empty() ? std::nullopt : std::optional<T>(ParseInteger<T>(text));
  }

  //! A point on a clock from its milliseconds since the clock's zero.
  inline TickCount64 ParseTickCount64(const std::string_view text)
  {
    return TickCount64(ParseMilliseconds(text));
  }

  //! A marker's 32-bit span (0 to OnDemandFrameTime). Throws DataFormatError for a value outside.
  inline TimeSpan32 ParseTimeSpan32(const std::string_view text)
  {
    const TimeSpan value = ParseMilliseconds(text);
    if (value < TimeSpan::Zero() || value > TimeSpan32::MaxValue().ToTimeSpan())
    {
      throw DataFormatError("'" + std::string(text) + "' is not a 32-bit span of ticks");
    }
    return TimeSpan32::FromTimeSpan(value);
  }

  inline std::optional<TimeSpan> OptionalTimeSpan(const std::string_view text)
  {
    return text.empty() ? std::nullopt : std::optional<TimeSpan>(ParseMilliseconds(text));
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
