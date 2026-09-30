#ifndef MB_FRAMEPACING_CORE_TIME_TIMESPAN_HPP
#define MB_FRAMEPACING_CORE_TIME_TIMESPAN_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <compare>
#include <concepts>
#include <cstdint>
#include <limits>
#include <stdexcept>
#include <utility>

namespace MB::FramePacing
{
  //! A signed time interval in ticks of 100 ns: C#'s System.TimeSpan (.NET 10), with its range, factories, components, rounding and
  //! errors. Out of range throws, as C# does: std::out_of_range where C# throws ArgumentOutOfRangeException (the integer factories and
  //! the constructors from parts), std::overflow_error for OverflowException (the double factories and the arithmetic), and
  //! std::invalid_argument for the ArgumentException of a NaN. Nothing allocates unless it throws.
  class TimeSpan
  {
    int64_t m_ticks{0};

    static constexpr int64_t MaxTicks = std::numeric_limits<int64_t>::max();
    static constexpr int64_t MinTicks = std::numeric_limits<int64_t>::min();

  public:
    static constexpr int64_t NanosecondsPerTick = 100;
    static constexpr int64_t TicksPerMicrosecond = 10;
    static constexpr int64_t TicksPerMillisecond = 10'000;
    static constexpr int64_t TicksPerSecond = 10'000'000;
    static constexpr int64_t TicksPerMinute = TicksPerSecond * 60;
    static constexpr int64_t TicksPerHour = TicksPerMinute * 60;
    static constexpr int64_t TicksPerDay = TicksPerHour * 24;

    constexpr TimeSpan() noexcept = default;

    constexpr explicit TimeSpan(const int64_t ticks) noexcept
      : m_ticks(ticks)
    {
    }

    //! The hours, minutes and seconds added up; each may be negative. Throws std::out_of_range if the total is outside the range.
    constexpr TimeSpan(const int32_t hours, const int32_t minutes, const int32_t seconds)
      : TimeSpan(0, hours, minutes, seconds)
    {
    }

    //! The parts added up; each may be negative. Throws std::out_of_range if the total is outside the range.
    constexpr TimeSpan(const int32_t days, const int32_t hours, const int32_t minutes, const int32_t seconds, const int32_t milliseconds = 0,
                       const int32_t microseconds = 0)
      : m_ticks(TicksFromParts(days, hours, minutes, seconds, milliseconds, microseconds))
    {
    }

    static constexpr TimeSpan Zero() noexcept
    {
      return {};
    }

    static constexpr TimeSpan MinValue() noexcept
    {
      return TimeSpan(MinTicks);
    }

    static constexpr TimeSpan MaxValue() noexcept
    {
      return TimeSpan(MaxTicks);
    }

    static constexpr TimeSpan FromTicks(const int64_t ticks) noexcept
    {
      return TimeSpan(ticks);
    }

    //! A whole number of days. Throws std::out_of_range if it is outside the range.
    template <std::integral T>
      requires(!std::same_as<T, bool>)
    static constexpr TimeSpan FromDays(const T days)
    {
      return FromUnits(days, TicksPerDay);
    }

    //! A whole number of hours. Throws std::out_of_range if it is outside the range.
    template <std::integral T>
      requires(!std::same_as<T, bool>)
    static constexpr TimeSpan FromHours(const T hours)
    {
      return FromUnits(hours, TicksPerHour);
    }

    //! A whole number of minutes. Throws std::out_of_range if it is outside the range.
    template <std::integral T>
      requires(!std::same_as<T, bool>)
    static constexpr TimeSpan FromMinutes(const T minutes)
    {
      return FromUnits(minutes, TicksPerMinute);
    }

    //! A whole number of seconds. Throws std::out_of_range if it is outside the range.
    template <std::integral T>
      requires(!std::same_as<T, bool>)
    static constexpr TimeSpan FromSeconds(const T seconds)
    {
      return FromUnits(seconds, TicksPerSecond);
    }

    //! A whole number of milliseconds. Throws std::out_of_range if it is outside the range.
    template <std::integral T>
      requires(!std::same_as<T, bool>)
    static constexpr TimeSpan FromMilliseconds(const T milliseconds)
    {
      return FromUnits(milliseconds, TicksPerMillisecond);
    }

    //! A whole number of microseconds. Throws std::out_of_range if it is outside the range.
    template <std::integral T>
      requires(!std::same_as<T, bool>)
    static constexpr TimeSpan FromMicroseconds(const T microseconds)
    {
      return FromUnits(microseconds, TicksPerMicrosecond);
    }

    //! Days, truncated toward zero to a tick. Throws std::overflow_error outside the range, std::invalid_argument for NaN.
    static constexpr TimeSpan FromDays(const double days)
    {
      return FromDouble(days, TicksPerDay);
    }

    //! Hours, truncated toward zero to a tick. Throws std::overflow_error outside the range, std::invalid_argument for NaN.
    static constexpr TimeSpan FromHours(const double hours)
    {
      return FromDouble(hours, TicksPerHour);
    }

    //! Minutes, truncated toward zero to a tick. Throws std::overflow_error outside the range, std::invalid_argument for NaN.
    static constexpr TimeSpan FromMinutes(const double minutes)
    {
      return FromDouble(minutes, TicksPerMinute);
    }

    //! Seconds, truncated toward zero to a tick (1.0 / 60 is 166'666 ticks). Throws std::overflow_error outside the range,
    //! std::invalid_argument for NaN.
    static constexpr TimeSpan FromSeconds(const double seconds)
    {
      return FromDouble(seconds, TicksPerSecond);
    }

    //! Milliseconds, truncated toward zero to a tick. Throws std::overflow_error outside the range, std::invalid_argument for NaN.
    static constexpr TimeSpan FromMilliseconds(const double milliseconds)
    {
      return FromDouble(milliseconds, TicksPerMillisecond);
    }

    //! Microseconds, truncated toward zero to a tick. Throws std::overflow_error outside the range, std::invalid_argument for NaN.
    static constexpr TimeSpan FromMicroseconds(const double microseconds)
    {
      return FromDouble(microseconds, TicksPerMicrosecond);
    }

    [[nodiscard]] constexpr int64_t Ticks() const noexcept
    {
      return m_ticks;
    }

    //! The whole days. Every component is truncated toward zero and has the sign of the span.
    [[nodiscard]] constexpr int32_t Days() const noexcept
    {
      return static_cast<int32_t>(m_ticks / TicksPerDay);
    }

    //! The hours of the day: -23 to 23.
    [[nodiscard]] constexpr int32_t Hours() const noexcept
    {
      return static_cast<int32_t>((m_ticks / TicksPerHour) % 24);
    }

    //! The minutes of the hour: -59 to 59.
    [[nodiscard]] constexpr int32_t Minutes() const noexcept
    {
      return static_cast<int32_t>((m_ticks / TicksPerMinute) % 60);
    }

    //! The seconds of the minute: -59 to 59.
    [[nodiscard]] constexpr int32_t Seconds() const noexcept
    {
      return static_cast<int32_t>((m_ticks / TicksPerSecond) % 60);
    }

    //! The milliseconds of the second: -999 to 999.
    [[nodiscard]] constexpr int32_t Milliseconds() const noexcept
    {
      return static_cast<int32_t>((m_ticks / TicksPerMillisecond) % 1000);
    }

    //! The microseconds of the millisecond: -999 to 999.
    [[nodiscard]] constexpr int32_t Microseconds() const noexcept
    {
      return static_cast<int32_t>((m_ticks / TicksPerMicrosecond) % 1000);
    }

    //! The nanoseconds of the microsecond, a multiple of 100: -900 to 900.
    [[nodiscard]] constexpr int32_t Nanoseconds() const noexcept
    {
      return static_cast<int32_t>((m_ticks % TicksPerMicrosecond) * NanosecondsPerTick);
    }

    [[nodiscard]] constexpr double TotalDays() const noexcept
    {
      return static_cast<double>(m_ticks) / TicksPerDay;
    }

    [[nodiscard]] constexpr double TotalHours() const noexcept
    {
      return static_cast<double>(m_ticks) / TicksPerHour;
    }

    [[nodiscard]] constexpr double TotalMinutes() const noexcept
    {
      return static_cast<double>(m_ticks) / TicksPerMinute;
    }

    [[nodiscard]] constexpr double TotalSeconds() const noexcept
    {
      return static_cast<double>(m_ticks) / TicksPerSecond;
    }

    //! As C#: kept within the whole milliseconds of the range (MaxValue() gives 922'337'203'685'477, not ...477.5807).
    [[nodiscard]] constexpr double TotalMilliseconds() const noexcept
    {
      constexpr int64_t WholeMilliseconds = MaxTicks / TicksPerMillisecond;
      constexpr auto Limit = static_cast<double>(WholeMilliseconds);
      const double milliseconds = static_cast<double>(m_ticks) / TicksPerMillisecond;
      if (milliseconds > Limit)
      {
        return Limit;
      }
      return milliseconds < -Limit ? -Limit : milliseconds;
    }

    [[nodiscard]] constexpr double TotalMicroseconds() const noexcept
    {
      return static_cast<double>(m_ticks) / TicksPerMicrosecond;
    }

    [[nodiscard]] constexpr double TotalNanoseconds() const noexcept
    {
      return static_cast<double>(m_ticks) * NanosecondsPerTick;
    }

    //! The span with the other sign. Throws std::overflow_error for MinValue(), which has no positive counterpart.
    [[nodiscard]] constexpr TimeSpan Negate() const
    {
      if (m_ticks == MinTicks)
      {
        throw std::overflow_error("TimeSpan: MinValue cannot be negated");
      }
      return TimeSpan(-m_ticks);
    }

    //! The absolute value. Throws std::overflow_error for MinValue(), which has no positive counterpart.
    [[nodiscard]] constexpr TimeSpan Duration() const
    {
      return m_ticks < 0 ? Negate() : *this;
    }

    constexpr TimeSpan operator+() const noexcept
    {
      return *this;
    }

    //! Throws std::overflow_error for MinValue().
    constexpr TimeSpan operator-() const
    {
      return Negate();
    }

    //! Throws std::overflow_error if the sum is outside the range.
    constexpr TimeSpan& operator+=(const TimeSpan other)
    {
      if ((other.m_ticks > 0 && m_ticks > MaxTicks - other.m_ticks) || (other.m_ticks < 0 && m_ticks < MinTicks - other.m_ticks))
      {
        throw std::overflow_error("TimeSpan: the sum is outside the range");
      }
      m_ticks += other.m_ticks;
      return *this;
    }

    //! Throws std::overflow_error if the difference is outside the range.
    constexpr TimeSpan& operator-=(const TimeSpan other)
    {
      if ((other.m_ticks < 0 && m_ticks > MaxTicks + other.m_ticks) || (other.m_ticks > 0 && m_ticks < MinTicks + other.m_ticks))
      {
        throw std::overflow_error("TimeSpan: the difference is outside the range");
      }
      m_ticks -= other.m_ticks;
      return *this;
    }

    //! Rounded to the nearest tick, a tie to the even one (C#'s Math.Round). Throws std::overflow_error outside the range (an infinite or
    //! NaN product too), std::invalid_argument for a NaN factor.
    constexpr TimeSpan& operator*=(const double factor)
    {
      if (IsNaN(factor))
      {
        throw std::invalid_argument("TimeSpan: the factor is NaN");
      }
      *this = FromDoubleTicks(RoundHalfToEven(static_cast<double>(m_ticks) * factor));
      return *this;
    }

    //! Rounded to the nearest tick, a tie to the even one (C#'s Math.Round). Throws std::overflow_error outside the range (a divisor of 0
    //! too), std::invalid_argument for a NaN divisor.
    constexpr TimeSpan& operator/=(const double divisor)
    {
      if (IsNaN(divisor))
      {
        throw std::invalid_argument("TimeSpan: the divisor is NaN");
      }
      *this = FromDoubleTicks(RoundHalfToEven(static_cast<double>(m_ticks) / divisor));
      return *this;
    }

    friend constexpr TimeSpan operator+(TimeSpan lhs, const TimeSpan rhs)
    {
      return lhs += rhs;
    }

    friend constexpr TimeSpan operator-(TimeSpan lhs, const TimeSpan rhs)
    {
      return lhs -= rhs;
    }

    friend constexpr TimeSpan operator*(TimeSpan timeSpan, const double factor)
    {
      return timeSpan *= factor;
    }

    friend constexpr TimeSpan operator*(const double factor, TimeSpan timeSpan)
    {
      return timeSpan *= factor;
    }

    friend constexpr TimeSpan operator/(TimeSpan timeSpan, const double divisor)
    {
      return timeSpan /= divisor;
    }

    //! The ratio of two spans; a span of 0 gives an infinity or NaN, as in C#.
    friend constexpr double operator/(const TimeSpan dividend, const TimeSpan divisor) noexcept
    {
      return static_cast<double>(dividend.m_ticks) / static_cast<double>(divisor.m_ticks);
    }

    constexpr bool operator==(const TimeSpan&) const noexcept = default;
    constexpr std::strong_ordering operator<=>(const TimeSpan&) const noexcept = default;

  private:
    //! value is NaN (std::isnan is not constexpr in C++20)
    static constexpr bool IsNaN(const double value) noexcept
    {
      // NaN is the one value that differs from itself
      // NOLINTNEXTLINE(misc-redundant-expression)
      return value != value;
    }

    template <std::integral T>
    static constexpr TimeSpan FromUnits(const T value, const int64_t ticksPerUnit)
    {
      if (std::cmp_less(value, MinTicks / ticksPerUnit) || std::cmp_greater(value, MaxTicks / ticksPerUnit))
      {
        throw std::out_of_range("TimeSpan: the value is outside the range of a TimeSpan");
      }
      return TimeSpan(static_cast<int64_t>(value) * ticksPerUnit);
    }

    static constexpr TimeSpan FromDouble(const double value, const int64_t ticksPerUnit)
    {
      if (IsNaN(value))
      {
        throw std::invalid_argument("TimeSpan: the value is NaN");
      }
      return FromDoubleTicks(value * static_cast<double>(ticksPerUnit));
    }

    //! ticks truncated toward zero. 2^63, which is what MaxValue's ticks round to as a double, is MaxValue.
    static constexpr TimeSpan FromDoubleTicks(const double ticks)
    {
      constexpr double Limit = 9'223'372'036'854'775'808.0;
      if (IsNaN(ticks) || ticks < -Limit || ticks > Limit)
      {
        throw std::overflow_error("TimeSpan: the value is outside the range of a TimeSpan");
      }
      return ticks == Limit ? MaxValue() : TimeSpan(static_cast<int64_t>(ticks));
    }

    //! value rounded to the nearest whole number, a tie to the even one (std::nearbyint is not constexpr in C++20). Values from 2^52 on are
    //! whole already; an infinity or NaN is returned as it is.
    static constexpr double RoundHalfToEven(const double value) noexcept
    {
      constexpr double Whole = 4'503'599'627'370'496.0;
      if (IsNaN(value) || value <= -Whole || value >= Whole)
      {
        return value;
      }
      const auto truncated = static_cast<int64_t>(value);
      const double fraction = value - static_cast<double>(truncated);
      const bool odd = (truncated % 2) != 0;
      if (fraction > 0.5 || (fraction == 0.5 && odd))
      {
        return static_cast<double>(truncated + 1);
      }
      if (fraction < -0.5 || (fraction == -0.5 && odd))
      {
        return static_cast<double>(truncated - 1);
      }
      return static_cast<double>(truncated);
    }

    //! The ticks of the parts added up. Whole milliseconds first: for any int32_t parts they stay within about 1.9e17, so nothing
    //! overflows before the range check.
    static constexpr int64_t TicksFromParts(const int32_t days, const int32_t hours, const int32_t minutes, const int32_t seconds,
                                            const int32_t milliseconds, const int32_t microseconds)
    {
      constexpr int64_t MaxMicroseconds = MaxTicks / TicksPerMicrosecond;
      // Beyond this many milliseconds no int32_t of microseconds brings the total back into the range
      constexpr int64_t MillisecondLimit = (MaxMicroseconds / 1000) + (int64_t{std::numeric_limits<int32_t>::max()} / 1000) + 1;
      const int64_t totalMilliseconds = (((((((int64_t{days} * 24) + hours) * 60) + minutes) * 60) + seconds) * 1000) + milliseconds;
      if (totalMilliseconds > MillisecondLimit || totalMilliseconds < -MillisecondLimit)
      {
        throw std::out_of_range("TimeSpan: the parts add up to more than the range of a TimeSpan");
      }
      const int64_t totalMicroseconds = (totalMilliseconds * 1000) + microseconds;
      if (totalMicroseconds > MaxMicroseconds || totalMicroseconds < -MaxMicroseconds)
      {
        throw std::out_of_range("TimeSpan: the parts add up to more than the range of a TimeSpan");
      }
      return totalMicroseconds * TicksPerMicrosecond;
    }
  };
}

#endif
