#ifndef MB_FRAMEPACING_CORE_TIME_NANOSECONDTIMESPAN_HPP
#define MB_FRAMEPACING_CORE_TIME_NANOSECONDTIMESPAN_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <compare>
#include <cstdint>
#include <limits>
#include <stdexcept>

namespace MB::FramePacing
{
  //! A signed time interval in nanoseconds: what a platform that counts in nanoseconds reports (a refresh period, a present's
  //! duration, the time between two display times), kept as it is given. A TimeSpan counts in ticks of 100 ns, so a value that goes
  //! through it loses up to 99 ns: a refresh period of 4'166'389 ns is 41'663 ticks, 21 parts in a million short. About 292 years
  //! either way.
  //!
  //! Out of range throws, as TimeSpan does: std::out_of_range from the factories, std::overflow_error from the arithmetic. Nothing
  //! allocates unless it throws.
  class NanosecondTimeSpan
  {
    int64_t m_nanoseconds{0};

    static constexpr int64_t MaxNanoseconds = std::numeric_limits<int64_t>::max();
    static constexpr int64_t MinNanoseconds = std::numeric_limits<int64_t>::min();

  public:
    static constexpr int64_t NanosecondsPerTick = TimeSpan::NanosecondsPerTick;
    static constexpr int64_t NanosecondsPerMicrosecond = 1'000;
    static constexpr int64_t NanosecondsPerMillisecond = 1'000'000;
    static constexpr int64_t NanosecondsPerSecond = 1'000'000'000;

    constexpr NanosecondTimeSpan() noexcept = default;

    constexpr explicit NanosecondTimeSpan(const int64_t nanoseconds) noexcept
      : m_nanoseconds(nanoseconds)
    {
    }

    static constexpr NanosecondTimeSpan Zero() noexcept
    {
      return {};
    }

    static constexpr NanosecondTimeSpan MinValue() noexcept
    {
      return NanosecondTimeSpan(MinNanoseconds);
    }

    static constexpr NanosecondTimeSpan MaxValue() noexcept
    {
      return NanosecondTimeSpan(MaxNanoseconds);
    }

    static constexpr NanosecondTimeSpan FromNanoseconds(const int64_t nanoseconds) noexcept
    {
      return NanosecondTimeSpan(nanoseconds);
    }

    //! A whole number of microseconds. Throws std::out_of_range if it is outside the range.
    static constexpr NanosecondTimeSpan FromMicroseconds(const int64_t microseconds)
    {
      return FromUnits(microseconds, NanosecondsPerMicrosecond);
    }

    //! A whole number of milliseconds. Throws std::out_of_range if it is outside the range.
    static constexpr NanosecondTimeSpan FromMilliseconds(const int64_t milliseconds)
    {
      return FromUnits(milliseconds, NanosecondsPerMillisecond);
    }

    //! A whole number of seconds. Throws std::out_of_range if it is outside the range.
    static constexpr NanosecondTimeSpan FromSeconds(const int64_t seconds)
    {
      return FromUnits(seconds, NanosecondsPerSecond);
    }

    //! A span in ticks of 100 ns, exactly. Throws std::out_of_range if it is outside the range (a TimeSpan reaches a hundred times
    //! as far).
    static constexpr NanosecondTimeSpan FromTimeSpan(const TimeSpan span)
    {
      return FromUnits(span.Ticks(), NanosecondsPerTick);
    }

    [[nodiscard]] constexpr int64_t Nanoseconds() const noexcept
    {
      return m_nanoseconds;
    }

    //! The span in ticks of 100 ns, truncated toward zero to a tick, as every conversion to a TimeSpan is.
    [[nodiscard]] constexpr TimeSpan ToTimeSpan() const noexcept
    {
      return TimeSpan(m_nanoseconds / NanosecondsPerTick);
    }

    [[nodiscard]] constexpr double TotalMicroseconds() const noexcept
    {
      return static_cast<double>(m_nanoseconds) / NanosecondsPerMicrosecond;
    }

    [[nodiscard]] constexpr double TotalMilliseconds() const noexcept
    {
      return static_cast<double>(m_nanoseconds) / NanosecondsPerMillisecond;
    }

    [[nodiscard]] constexpr double TotalSeconds() const noexcept
    {
      return static_cast<double>(m_nanoseconds) / NanosecondsPerSecond;
    }

    //! The span with the other sign. Throws std::overflow_error for MinValue(), which has no positive counterpart.
    [[nodiscard]] constexpr NanosecondTimeSpan Negate() const
    {
      if (m_nanoseconds == MinNanoseconds)
      {
        throw std::overflow_error("NanosecondTimeSpan: MinValue cannot be negated");
      }
      return NanosecondTimeSpan(-m_nanoseconds);
    }

    //! The absolute value. Throws std::overflow_error for MinValue(), which has no positive counterpart.
    [[nodiscard]] constexpr NanosecondTimeSpan Duration() const
    {
      return m_nanoseconds < 0 ? Negate() : *this;
    }

    constexpr NanosecondTimeSpan operator+() const noexcept
    {
      return *this;
    }

    //! Throws std::overflow_error for MinValue().
    constexpr NanosecondTimeSpan operator-() const
    {
      return Negate();
    }

    //! Throws std::overflow_error if the sum is outside the range.
    constexpr NanosecondTimeSpan& operator+=(const NanosecondTimeSpan other)
    {
      if ((other.m_nanoseconds > 0 && m_nanoseconds > MaxNanoseconds - other.m_nanoseconds) ||
          (other.m_nanoseconds < 0 && m_nanoseconds < MinNanoseconds - other.m_nanoseconds))
      {
        throw std::overflow_error("NanosecondTimeSpan: the sum is outside the range");
      }
      m_nanoseconds += other.m_nanoseconds;
      return *this;
    }

    //! Throws std::overflow_error if the difference is outside the range.
    constexpr NanosecondTimeSpan& operator-=(const NanosecondTimeSpan other)
    {
      if ((other.m_nanoseconds < 0 && m_nanoseconds > MaxNanoseconds + other.m_nanoseconds) ||
          (other.m_nanoseconds > 0 && m_nanoseconds < MinNanoseconds + other.m_nanoseconds))
      {
        throw std::overflow_error("NanosecondTimeSpan: the difference is outside the range");
      }
      m_nanoseconds -= other.m_nanoseconds;
      return *this;
    }

    friend constexpr NanosecondTimeSpan operator+(NanosecondTimeSpan lhs, const NanosecondTimeSpan rhs)
    {
      return lhs += rhs;
    }

    friend constexpr NanosecondTimeSpan operator-(NanosecondTimeSpan lhs, const NanosecondTimeSpan rhs)
    {
      return lhs -= rhs;
    }

    constexpr bool operator==(const NanosecondTimeSpan&) const noexcept = default;
    constexpr std::strong_ordering operator<=>(const NanosecondTimeSpan&) const noexcept = default;

  private:
    static constexpr NanosecondTimeSpan FromUnits(const int64_t value, const int64_t nanosecondsPerUnit)
    {
      if (value < MinNanoseconds / nanosecondsPerUnit || value > MaxNanoseconds / nanosecondsPerUnit)
      {
        throw std::out_of_range("NanosecondTimeSpan: the value is outside the range of a NanosecondTimeSpan");
      }
      return NanosecondTimeSpan(value * nanosecondsPerUnit);
    }
  };
}

#endif
