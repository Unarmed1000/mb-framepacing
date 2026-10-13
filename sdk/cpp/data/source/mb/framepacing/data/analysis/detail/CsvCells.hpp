#ifndef MB_FRAMEPACING_DATA_ANALYSIS_DETAIL_CSVCELLS_HPP
#define MB_FRAMEPACING_DATA_ANALYSIS_DETAIL_CSVCELLS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Private to the data module: parsing the cells of the CSV files. Every number is a whole one, written as its digits with a '-' in front
// when negative; a time is its nanoseconds, and a length of time is never negative. An empty cell is a missing value.

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/data/DataFormatError.hpp>
#include <charconv>
#include <cstdint>
#include <optional>
#include <string>
#include <string_view>

namespace MB::FramePacing::Data::Csv
{
  //! What a cell that is no whole number in its range is refused with.
  [[noreturn]] inline void ThrowNotInRange(const std::string_view text)
  {
    throw DataFormatError(text.empty() ? std::string("An empty cell where a whole number is required")
                                       : "'" + std::string(text) + "' is not a whole number in its range");
  }

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
      ThrowNotInRange(text);
    }
    return value;
  }

  template <typename T>
  std::optional<T> OptionalInteger(const std::string_view text)
  {
    return text.empty() ? std::nullopt : std::optional<T>(ParseInteger<T>(text));
  }

  //! A span from its nanoseconds.
  inline NanosecondTimeSpan ParseNanosecondTimeSpan(const std::string_view text)
  {
    return NanosecondTimeSpan(ParseInteger<int64_t>(text));
  }

  //! A point on a clock from its nanoseconds since the clock's zero.
  inline NanosecondTickCount ParseNanosecondTickCount(const std::string_view text)
  {
    return NanosecondTickCount(ParseInteger<int64_t>(text));
  }

  //! A length of time from its nanoseconds: 64 bits signed in the file, and never negative. Throws DataFormatError for a negative
  //! number, as for any other outside its range.
  inline NanosecondTimeDuration ParseNanosecondTimeDuration(const std::string_view text)
  {
    const auto nanoseconds = ParseInteger<int64_t>(text);
    if (nanoseconds < 0)
    {
      ThrowNotInRange(text);
    }
    return NanosecondTimeDuration::FromNanoseconds(nanoseconds);
  }

  //! One of a marker's durations from its nanoseconds, as the marker carried it: 32 bits unsigned in the marker and in the file (0 to
  //! 4294967295, which is on demand for a frame time: the marker library's Payload::OnDemandFrameTime). Throws DataFormatError for a
  //! number outside those 32 bits.
  inline NanosecondTimeDuration ParseMarkerDuration(const std::string_view text)
  {
    return NanosecondTimeDuration::FromNanoseconds(int64_t{ParseInteger<uint32_t>(text)});
  }

  inline std::optional<NanosecondTimeSpan> OptionalNanosecondTimeSpan(const std::string_view text)
  {
    return text.empty() ? std::nullopt : std::optional<NanosecondTimeSpan>(ParseNanosecondTimeSpan(text));
  }

  inline std::optional<NanosecondTickCount> OptionalNanosecondTickCount(const std::string_view text)
  {
    return text.empty() ? std::nullopt : std::optional<NanosecondTickCount>(ParseNanosecondTickCount(text));
  }

  inline std::optional<NanosecondTimeDuration> OptionalNanosecondTimeDuration(const std::string_view text)
  {
    return text.empty() ? std::nullopt : std::optional<NanosecondTimeDuration>(ParseNanosecondTimeDuration(text));
  }

  inline std::optional<NanosecondTimeDuration> OptionalMarkerDuration(const std::string_view text)
  {
    return text.empty() ? std::nullopt : std::optional<NanosecondTimeDuration>(ParseMarkerDuration(text));
  }
}

#endif
