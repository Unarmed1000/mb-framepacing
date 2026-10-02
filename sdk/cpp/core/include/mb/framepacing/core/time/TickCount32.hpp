#ifndef MB_FRAMEPACING_CORE_TIME_TICKCOUNT32_HPP
#define MB_FRAMEPACING_CORE_TIME_TICKCOUNT32_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <cstdint>
#include <limits>
#include <stdexcept>

namespace MB::FramePacing
{
  //! A point on a 32-bit clock in ticks of 100 ns: the count wraps around every 2^32 ticks (429.4967296 s). The SDK never reads a clock;
  //! the application fills it, or takes the low 32 bits of a TickCount64 (FromTickCount64). The count is stored unsigned, so adding and
  //! subtracting wrap around with defined behaviour; Ticks() is its signed view.
  //!
  //! Two counts compare and subtract correctly while they are less than 2^31 ticks (214.7483648 s) apart, across the wrap too: a < b
  //! when b is ahead of a, the serial number rule (RFC 1982). So the comparisons are not a total order, and counts exactly 2^31 apart
  //! are each "less" than the other. They order a set of counts (std::sort, std::map) only while all of them lie within 2^31 ticks of
  //! each other. The From... factories throw std::overflow_error for a value outside the range; nothing else throws or allocates.
  class TickCount32
  {
    uint32_t m_ticks{0};

  public:
    static constexpr int64_t NanosecondsPerTick = TimeSpan::NanosecondsPerTick;
    static constexpr int64_t TicksPerMicrosecond = TimeSpan::TicksPerMicrosecond;
    static constexpr int64_t TicksPerMillisecond = TimeSpan::TicksPerMillisecond;
    static constexpr int64_t TicksPerSecond = TimeSpan::TicksPerSecond;
    static constexpr int64_t TicksPerMinute = TimeSpan::TicksPerMinute;
    static constexpr int64_t TicksPerHour = TimeSpan::TicksPerHour;
    static constexpr int64_t TicksPerDay = TimeSpan::TicksPerDay;

    //! The range of each From... factory: what the signed 32-bit view holds (a day or an hour does not fit, so their range is 0)
    static constexpr int32_t MinDays = static_cast<int32_t>(std::numeric_limits<int32_t>::min() / TicksPerDay);
    static constexpr int32_t MaxDays = static_cast<int32_t>(std::numeric_limits<int32_t>::max() / TicksPerDay);
    static constexpr int32_t MinHours = static_cast<int32_t>(std::numeric_limits<int32_t>::min() / TicksPerHour);
    static constexpr int32_t MaxHours = static_cast<int32_t>(std::numeric_limits<int32_t>::max() / TicksPerHour);
    static constexpr int32_t MinMinutes = static_cast<int32_t>(std::numeric_limits<int32_t>::min() / TicksPerMinute);
    static constexpr int32_t MaxMinutes = static_cast<int32_t>(std::numeric_limits<int32_t>::max() / TicksPerMinute);
    static constexpr int32_t MinSeconds = static_cast<int32_t>(std::numeric_limits<int32_t>::min() / TicksPerSecond);
    static constexpr int32_t MaxSeconds = static_cast<int32_t>(std::numeric_limits<int32_t>::max() / TicksPerSecond);
    static constexpr int32_t MinMilliseconds = static_cast<int32_t>(std::numeric_limits<int32_t>::min() / TicksPerMillisecond);
    static constexpr int32_t MaxMilliseconds = static_cast<int32_t>(std::numeric_limits<int32_t>::max() / TicksPerMillisecond);
    static constexpr int32_t MinMicroseconds = static_cast<int32_t>(std::numeric_limits<int32_t>::min() / TicksPerMicrosecond);
    static constexpr int32_t MaxMicroseconds = static_cast<int32_t>(std::numeric_limits<int32_t>::max() / TicksPerMicrosecond);

    constexpr TickCount32() noexcept = default;

    constexpr explicit TickCount32(const uint32_t ticks) noexcept
      : m_ticks(ticks)
    {
    }

    constexpr explicit TickCount32(const int32_t ticks) noexcept
      : m_ticks(static_cast<uint32_t>(ticks))
    {
    }

    //! The low 32 bits of the span's ticks: the point sinceEpoch after the clock's epoch, wrapped.
    constexpr explicit TickCount32(const TimeSpan sinceEpoch) noexcept
      : m_ticks(static_cast<uint32_t>(sinceEpoch.Ticks()))
    {
    }

    static constexpr TickCount32 FromTicks(const int32_t ticks) noexcept
    {
      return TickCount32(ticks);
    }

    //! The count as stored (UnsignedTicks()).
    static constexpr TickCount32 FromUnsignedTicks(const uint32_t ticks) noexcept
    {
      return TickCount32(ticks);
    }

    //! The same point on the 32-bit clock: the low 32 bits of the count.
    static constexpr TickCount32 FromTickCount64(const TickCount64 count) noexcept
    {
      return TickCount32(static_cast<uint32_t>(count.UnsignedTicks()));
    }

    //! Throws std::overflow_error outside MinDays to MaxDays (only 0 fits).
    static constexpr TickCount32 FromDays(const int32_t days)
    {
      return FromUnits(days, MinDays, MaxDays, TicksPerDay);
    }

    //! Throws std::overflow_error outside MinHours to MaxHours (only 0 fits).
    static constexpr TickCount32 FromHours(const int32_t hours)
    {
      return FromUnits(hours, MinHours, MaxHours, TicksPerHour);
    }

    //! Throws std::overflow_error outside MinMinutes to MaxMinutes.
    static constexpr TickCount32 FromMinutes(const int32_t minutes)
    {
      return FromUnits(minutes, MinMinutes, MaxMinutes, TicksPerMinute);
    }

    //! Throws std::overflow_error outside MinSeconds to MaxSeconds.
    static constexpr TickCount32 FromSeconds(const int32_t seconds)
    {
      return FromUnits(seconds, MinSeconds, MaxSeconds, TicksPerSecond);
    }

    //! Throws std::overflow_error outside MinMilliseconds to MaxMilliseconds.
    static constexpr TickCount32 FromMilliseconds(const int32_t milliseconds)
    {
      return FromUnits(milliseconds, MinMilliseconds, MaxMilliseconds, TicksPerMillisecond);
    }

    //! Throws std::overflow_error outside MinMicroseconds to MaxMicroseconds.
    static constexpr TickCount32 FromMicroseconds(const int32_t microseconds)
    {
      return FromUnits(microseconds, MinMicroseconds, MaxMicroseconds, TicksPerMicrosecond);
    }

    //! Nanoseconds rounded down to the tick they are in; every int32_t fits.
    static constexpr TickCount32 FromNanoseconds(const int32_t nanoseconds) noexcept
    {
      const auto ticks = static_cast<int32_t>(nanoseconds / NanosecondsPerTick);
      return TickCount32((nanoseconds % NanosecondsPerTick) < 0 ? ticks - 1 : ticks);
    }

    //! The count as a signed number of ticks.
    [[nodiscard]] constexpr int32_t Ticks() const noexcept
    {
      return static_cast<int32_t>(m_ticks);
    }

    //! The count as stored.
    [[nodiscard]] constexpr uint32_t UnsignedTicks() const noexcept
    {
      return m_ticks;
    }

    //! The whole days of Ticks() (always 0). Every component is truncated toward zero and has the sign of Ticks().
    [[nodiscard]] constexpr int32_t Days() const noexcept
    {
      return static_cast<int32_t>(Ticks() / TicksPerDay);
    }

    [[nodiscard]] constexpr int32_t Hours() const noexcept
    {
      return static_cast<int32_t>((Ticks() / TicksPerHour) % 24);
    }

    [[nodiscard]] constexpr int32_t Minutes() const noexcept
    {
      return static_cast<int32_t>((Ticks() / TicksPerMinute) % 60);
    }

    [[nodiscard]] constexpr int32_t Seconds() const noexcept
    {
      return static_cast<int32_t>((Ticks() / TicksPerSecond) % 60);
    }

    [[nodiscard]] constexpr int32_t Milliseconds() const noexcept
    {
      return static_cast<int32_t>((Ticks() / TicksPerMillisecond) % 1000);
    }

    [[nodiscard]] constexpr int32_t Microseconds() const noexcept
    {
      return static_cast<int32_t>((Ticks() / TicksPerMicrosecond) % 1000);
    }

    [[nodiscard]] constexpr double TotalNanoseconds() const noexcept
    {
      return static_cast<double>(Ticks()) * NanosecondsPerTick;
    }

    [[nodiscard]] constexpr double TotalMicroseconds() const noexcept
    {
      return static_cast<double>(Ticks()) / TicksPerMicrosecond;
    }

    [[nodiscard]] constexpr double TotalMilliseconds() const noexcept
    {
      return static_cast<double>(Ticks()) / TicksPerMillisecond;
    }

    [[nodiscard]] constexpr double TotalSeconds() const noexcept
    {
      return static_cast<double>(Ticks()) / TicksPerSecond;
    }

    [[nodiscard]] constexpr double TotalMinutes() const noexcept
    {
      return static_cast<double>(Ticks()) / TicksPerMinute;
    }

    [[nodiscard]] constexpr double TotalHours() const noexcept
    {
      return static_cast<double>(Ticks()) / TicksPerHour;
    }

    [[nodiscard]] constexpr double TotalDays() const noexcept
    {
      return static_cast<double>(Ticks()) / TicksPerDay;
    }

    //! Ticks() as a span since the clock's epoch.
    [[nodiscard]] constexpr TimeSpan ToTimeSpan() const noexcept
    {
      return TimeSpan(Ticks());
    }

    //! Adds the span modulo 2^32: wraps around.
    constexpr TickCount32& operator+=(const TimeSpan span) noexcept
    {
      m_ticks += static_cast<uint32_t>(span.Ticks());
      return *this;
    }

    //! Subtracts the span modulo 2^32: wraps around.
    constexpr TickCount32& operator-=(const TimeSpan span) noexcept
    {
      m_ticks -= static_cast<uint32_t>(span.Ticks());
      return *this;
    }

    friend constexpr TickCount32 operator+(TickCount32 count, const TimeSpan span) noexcept
    {
      return count += span;
    }

    friend constexpr TickCount32 operator-(TickCount32 count, const TimeSpan span) noexcept
    {
      return count -= span;
    }

    //! The time from rhs to lhs, -2^31 to 2^31 - 1 ticks: correct while the counts are less than 2^31 ticks apart.
    friend constexpr TimeSpan operator-(const TickCount32 lhs, const TickCount32 rhs) noexcept
    {
      return TimeSpan(Distance(lhs, rhs));
    }

    constexpr bool operator==(const TickCount32&) const noexcept = default;

    //! lhs is before rhs (see the class: less than 2^31 ticks apart).
    friend constexpr bool operator<(const TickCount32 lhs, const TickCount32 rhs) noexcept
    {
      return Distance(lhs, rhs) < 0;
    }

    friend constexpr bool operator<=(const TickCount32 lhs, const TickCount32 rhs) noexcept
    {
      return Distance(lhs, rhs) <= 0;
    }

    friend constexpr bool operator>(const TickCount32 lhs, const TickCount32 rhs) noexcept
    {
      return Distance(lhs, rhs) > 0;
    }

    friend constexpr bool operator>=(const TickCount32 lhs, const TickCount32 rhs) noexcept
    {
      return Distance(lhs, rhs) >= 0;
    }

  private:
    //! The ticks from rhs to lhs as a signed 32-bit number: the unsigned difference wraps, its signed view is the shorter way round.
    static constexpr int32_t Distance(const TickCount32 lhs, const TickCount32 rhs) noexcept
    {
      return static_cast<int32_t>(static_cast<uint32_t>(lhs.m_ticks - rhs.m_ticks));
    }

    static constexpr TickCount32 FromUnits(const int32_t value, const int32_t minValue, const int32_t maxValue, const int64_t ticksPerUnit)
    {
      if (value < minValue || value > maxValue)
      {
        throw std::overflow_error("TickCount32: the value is outside the range of a TickCount32");
      }
      return TickCount32(static_cast<int32_t>(value * ticksPerUnit));
    }
  };
}

#endif
