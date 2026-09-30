#ifndef MB_FRAMEPACING_CORE_TIME_TICKCOUNT64_HPP
#define MB_FRAMEPACING_CORE_TIME_TICKCOUNT64_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <cstdint>
#include <limits>
#include <stdexcept>

namespace MB::FramePacing
{
  //! A point on the application's steady clock in ticks of 100 ns, any epoch (the same clock for the whole run): the marker's intended
  //! display time and CPU start time. The SDK never reads a clock; the application fills it from its own. The count is stored unsigned, so
  //! adding and subtracting wrap around with defined behaviour; Ticks() is its signed view.
  //!
  //! Wrap-around safe, as TickCount32: two counts compare and subtract correctly while they are less than 2^63 ticks apart, across the
  //! wrap too (a < b when b is ahead of a, the serial number rule, RFC 1982). So the comparisons are not a total order. The From...
  //! factories throw std::overflow_error for a value outside the range; nothing else throws or allocates.
  class TickCount64
  {
    uint64_t m_ticks{0};

  public:
    static constexpr int64_t NanosecondsPerTick = TimeSpan::NanosecondsPerTick;
    static constexpr int64_t TicksPerMicrosecond = TimeSpan::TicksPerMicrosecond;
    static constexpr int64_t TicksPerMillisecond = TimeSpan::TicksPerMillisecond;
    static constexpr int64_t TicksPerSecond = TimeSpan::TicksPerSecond;
    static constexpr int64_t TicksPerMinute = TimeSpan::TicksPerMinute;
    static constexpr int64_t TicksPerHour = TimeSpan::TicksPerHour;
    static constexpr int64_t TicksPerDay = TimeSpan::TicksPerDay;

    //! The range of each From... factory
    static constexpr int64_t MinDays = std::numeric_limits<int64_t>::min() / TicksPerDay;
    static constexpr int64_t MaxDays = std::numeric_limits<int64_t>::max() / TicksPerDay;
    static constexpr int64_t MinHours = std::numeric_limits<int64_t>::min() / TicksPerHour;
    static constexpr int64_t MaxHours = std::numeric_limits<int64_t>::max() / TicksPerHour;
    static constexpr int64_t MinMinutes = std::numeric_limits<int64_t>::min() / TicksPerMinute;
    static constexpr int64_t MaxMinutes = std::numeric_limits<int64_t>::max() / TicksPerMinute;
    static constexpr int64_t MinSeconds = std::numeric_limits<int64_t>::min() / TicksPerSecond;
    static constexpr int64_t MaxSeconds = std::numeric_limits<int64_t>::max() / TicksPerSecond;
    static constexpr int64_t MinMilliseconds = std::numeric_limits<int64_t>::min() / TicksPerMillisecond;
    static constexpr int64_t MaxMilliseconds = std::numeric_limits<int64_t>::max() / TicksPerMillisecond;
    static constexpr int64_t MinMicroseconds = std::numeric_limits<int64_t>::min() / TicksPerMicrosecond;
    static constexpr int64_t MaxMicroseconds = std::numeric_limits<int64_t>::max() / TicksPerMicrosecond;

    //! The fastest counter FromCounter takes (about 922 GHz, beyond any platform's clock): its rest times TicksPerSecond fits int64_t.
    static constexpr int64_t MaxCounterFrequency = std::numeric_limits<int64_t>::max() / TicksPerSecond;

    constexpr TickCount64() noexcept = default;

    constexpr explicit TickCount64(const int64_t ticks) noexcept
      : m_ticks(static_cast<uint64_t>(ticks))
    {
    }

    //! The point sinceEpoch after the clock's epoch.
    constexpr explicit TickCount64(const TimeSpan sinceEpoch) noexcept
      : TickCount64(sinceEpoch.Ticks())
    {
    }

    static constexpr TickCount64 FromTicks(const int64_t ticks) noexcept
    {
      return TickCount64(ticks);
    }

    //! The count as stored (UnsignedTicks()).
    static constexpr TickCount64 FromUnsignedTicks(const uint64_t ticks) noexcept
    {
      TickCount64 count;
      count.m_ticks = ticks;
      return count;
    }

    //! Throws std::overflow_error outside MinDays to MaxDays.
    static constexpr TickCount64 FromDays(const int64_t days)
    {
      return FromUnits(days, MinDays, MaxDays, TicksPerDay);
    }

    //! Throws std::overflow_error outside MinHours to MaxHours.
    static constexpr TickCount64 FromHours(const int64_t hours)
    {
      return FromUnits(hours, MinHours, MaxHours, TicksPerHour);
    }

    //! Throws std::overflow_error outside MinMinutes to MaxMinutes.
    static constexpr TickCount64 FromMinutes(const int64_t minutes)
    {
      return FromUnits(minutes, MinMinutes, MaxMinutes, TicksPerMinute);
    }

    //! Throws std::overflow_error outside MinSeconds to MaxSeconds.
    static constexpr TickCount64 FromSeconds(const int64_t seconds)
    {
      return FromUnits(seconds, MinSeconds, MaxSeconds, TicksPerSecond);
    }

    //! Throws std::overflow_error outside MinMilliseconds to MaxMilliseconds.
    static constexpr TickCount64 FromMilliseconds(const int64_t milliseconds)
    {
      return FromUnits(milliseconds, MinMilliseconds, MaxMilliseconds, TicksPerMillisecond);
    }

    //! Throws std::overflow_error outside MinMicroseconds to MaxMicroseconds.
    static constexpr TickCount64 FromMicroseconds(const int64_t microseconds)
    {
      return FromUnits(microseconds, MinMicroseconds, MaxMicroseconds, TicksPerMicrosecond);
    }

    //! Nanoseconds (CLOCK_MONOTONIC, Vulkan and EGL present times, Choreographer) rounded down to the tick they are in.
    static constexpr TickCount64 FromNanoseconds(const int64_t nanoseconds) noexcept
    {
      const int64_t ticks = nanoseconds / NanosecondsPerTick;
      return TickCount64((nanoseconds % NanosecondsPerTick) < 0 ? ticks - 1 : ticks);
    }

    //! A counter value of a clock that counts frequency times a second (QueryPerformanceCounter with QueryPerformanceFrequency, .NET's
    //! Stopwatch), rounded down to the tick it is in. Exact for any counter value: the whole seconds and the rest are converted apart,
    //! so nothing overflows. Throws std::out_of_range for a frequency that is not 1 to MaxCounterFrequency.
    static constexpr TickCount64 FromCounter(const int64_t counter, const int64_t frequency)
    {
      if (frequency <= 0 || frequency > MaxCounterFrequency)
      {
        throw std::out_of_range("TickCount64: the counter frequency must be 1 to MaxCounterFrequency");
      }
      int64_t seconds = counter / frequency;
      int64_t rest = counter % frequency;
      if (rest < 0)
      {
        --seconds;
        rest += frequency;
      }
      // rest < frequency <= MaxCounterFrequency, so rest * TicksPerSecond fits
      return TickCount64((seconds * TicksPerSecond) + ((rest * TicksPerSecond) / frequency));
    }

    //! The count as a signed number of ticks.
    [[nodiscard]] constexpr int64_t Ticks() const noexcept
    {
      return static_cast<int64_t>(m_ticks);
    }

    //! The count as stored.
    [[nodiscard]] constexpr uint64_t UnsignedTicks() const noexcept
    {
      return m_ticks;
    }

    //! The whole days of Ticks(). Every component is truncated toward zero and has the sign of Ticks().
    [[nodiscard]] constexpr int64_t Days() const noexcept
    {
      return Ticks() / TicksPerDay;
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

    //! The time since the clock's epoch.
    [[nodiscard]] constexpr TimeSpan ToTimeSpan() const noexcept
    {
      return TimeSpan(Ticks());
    }

    //! Wraps around.
    constexpr TickCount64& operator+=(const TimeSpan span) noexcept
    {
      m_ticks += static_cast<uint64_t>(span.Ticks());
      return *this;
    }

    //! Wraps around.
    constexpr TickCount64& operator-=(const TimeSpan span) noexcept
    {
      m_ticks -= static_cast<uint64_t>(span.Ticks());
      return *this;
    }

    friend constexpr TickCount64 operator+(TickCount64 count, const TimeSpan span) noexcept
    {
      return count += span;
    }

    friend constexpr TickCount64 operator-(TickCount64 count, const TimeSpan span) noexcept
    {
      return count -= span;
    }

    //! The time from rhs to lhs, the shorter way round: correct while the counts are less than 2^63 ticks apart.
    friend constexpr TimeSpan operator-(const TickCount64 lhs, const TickCount64 rhs) noexcept
    {
      return TimeSpan(static_cast<int64_t>(lhs.m_ticks - rhs.m_ticks));
    }

    constexpr bool operator==(const TickCount64&) const noexcept = default;

    //! lhs is before rhs: the wrap-safe order of TickCount32, for counts less than 2^63 ticks apart.
    friend constexpr bool operator<(const TickCount64 lhs, const TickCount64 rhs) noexcept
    {
      return Distance(lhs, rhs) < 0;
    }

    friend constexpr bool operator<=(const TickCount64 lhs, const TickCount64 rhs) noexcept
    {
      return Distance(lhs, rhs) <= 0;
    }

    friend constexpr bool operator>(const TickCount64 lhs, const TickCount64 rhs) noexcept
    {
      return Distance(lhs, rhs) > 0;
    }

    friend constexpr bool operator>=(const TickCount64 lhs, const TickCount64 rhs) noexcept
    {
      return Distance(lhs, rhs) >= 0;
    }

  private:
    //! The ticks from rhs to lhs as a signed 64-bit number: the unsigned difference wraps, its signed view is the shorter way round.
    static constexpr int64_t Distance(const TickCount64 lhs, const TickCount64 rhs) noexcept
    {
      return static_cast<int64_t>(lhs.m_ticks - rhs.m_ticks);
    }

    static constexpr TickCount64 FromUnits(const int64_t value, const int64_t minValue, const int64_t maxValue, const int64_t ticksPerUnit)
    {
      if (value < minValue || value > maxValue)
      {
        throw std::overflow_error("TickCount64: the value is outside the range of a TickCount64");
      }
      return TickCount64(value * ticksPerUnit);
    }
  };
}

#endif
