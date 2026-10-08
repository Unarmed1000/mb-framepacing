#ifndef MB_FRAMEPACING_CORE_TIME_TIMESPAN32_HPP
#define MB_FRAMEPACING_CORE_TIME_TIMESPAN32_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <compare>
#include <cstdint>
#include <limits>
#include <stdexcept>

namespace MB::FramePacing
{
  //! A time interval of 0 to 429.4967295 s in ticks of 100 ns, unsigned 32-bit: an interval as four bytes of a file or a message hold it
  //! in ticks (the tools' data still does; the marker's own durations are nanoseconds, a NanosecondTimeDuration). It has no arithmetic:
  //! compute with TimeSpan and convert the result.
  class TimeSpan32
  {
    uint32_t m_ticks{0};

  public:
    constexpr TimeSpan32() noexcept = default;

    constexpr explicit TimeSpan32(const uint32_t ticks) noexcept
      : m_ticks(ticks)
    {
    }

    static constexpr TimeSpan32 Zero() noexcept
    {
      return {};
    }

    static constexpr TimeSpan32 MaxValue() noexcept
    {
      return TimeSpan32(std::numeric_limits<uint32_t>::max());
    }

    static constexpr TimeSpan32 FromTicks(const uint32_t ticks) noexcept
    {
      return TimeSpan32(ticks);
    }

    //! The span exactly. Throws std::out_of_range for a negative span or one longer than MaxValue().
    static constexpr TimeSpan32 FromTimeSpan(const TimeSpan span)
    {
      if (span.Ticks() < 0 || span.Ticks() > int64_t{std::numeric_limits<uint32_t>::max()})
      {
        throw std::out_of_range("TimeSpan32: the span is negative or longer than 429.4967295 s");
      }
      return TimeSpan32(static_cast<uint32_t>(span.Ticks()));
    }

    [[nodiscard]] constexpr uint32_t Ticks() const noexcept
    {
      return m_ticks;
    }

    [[nodiscard]] constexpr TimeSpan ToTimeSpan() const noexcept
    {
      return TimeSpan(m_ticks);
    }

    constexpr bool operator==(const TimeSpan32&) const noexcept = default;
    constexpr std::strong_ordering operator<=>(const TimeSpan32&) const noexcept = default;
  };
}

#endif
