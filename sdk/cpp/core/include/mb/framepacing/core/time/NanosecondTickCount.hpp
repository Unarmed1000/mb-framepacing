#ifndef MB_FRAMEPACING_CORE_TIME_NANOSECONDTICKCOUNT_HPP
#define MB_FRAMEPACING_CORE_TIME_NANOSECONDTICKCOUNT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <cstdint>
#include <limits>
#include <stdexcept>

namespace MB::FramePacing
{
  //! A point on a clock that counts in nanoseconds, any epoch (the same clock for the whole run): what a platform that counts in
  //! nanoseconds reports (CLOCK_MONOTONIC, Vulkan and EGL present times, Choreographer), kept as it is given. A TickCount64 counts
  //! in ticks of 100 ns; ToTickCount64() is the tick a point is in. The SDK never reads a clock; the application fills it from its
  //! own. The count is stored unsigned, so adding and subtracting wrap around with defined behaviour; Nanoseconds() is its signed
  //! view.
  //!
  //! Wrap-around safe, as TickCount64: two counts compare and subtract correctly while they are less than 2^63 nanoseconds apart
  //! (about 292 years), across the wrap too. So the comparisons are not a total order: counts exactly 2^63 apart are each "less"
  //! than the other. The From... factories throw std::overflow_error for a value outside the range, FromCounter
  //! std::out_of_range for a frequency outside its range; nothing else throws or allocates.
  class NanosecondTickCount
  {
    uint64_t m_nanoseconds{0};

  public:
    static constexpr int64_t NanosecondsPerTick = NanosecondTimeSpan::NanosecondsPerTick;
    static constexpr int64_t NanosecondsPerMicrosecond = NanosecondTimeSpan::NanosecondsPerMicrosecond;
    static constexpr int64_t NanosecondsPerMillisecond = NanosecondTimeSpan::NanosecondsPerMillisecond;
    static constexpr int64_t NanosecondsPerSecond = NanosecondTimeSpan::NanosecondsPerSecond;

    //! The range of each From... factory
    static constexpr int64_t MinSeconds = std::numeric_limits<int64_t>::min() / NanosecondsPerSecond;
    static constexpr int64_t MaxSeconds = std::numeric_limits<int64_t>::max() / NanosecondsPerSecond;
    static constexpr int64_t MinMilliseconds = std::numeric_limits<int64_t>::min() / NanosecondsPerMillisecond;
    static constexpr int64_t MaxMilliseconds = std::numeric_limits<int64_t>::max() / NanosecondsPerMillisecond;
    static constexpr int64_t MinMicroseconds = std::numeric_limits<int64_t>::min() / NanosecondsPerMicrosecond;
    static constexpr int64_t MaxMicroseconds = std::numeric_limits<int64_t>::max() / NanosecondsPerMicrosecond;
    static constexpr int64_t MinTicks = std::numeric_limits<int64_t>::min() / NanosecondsPerTick;
    static constexpr int64_t MaxTicks = std::numeric_limits<int64_t>::max() / NanosecondsPerTick;

    //! The fastest counter FromCounter takes (about 9.2 GHz, beyond any platform's clock): its rest times NanosecondsPerSecond fits
    //! int64_t.
    static constexpr int64_t MaxCounterFrequency = std::numeric_limits<int64_t>::max() / NanosecondsPerSecond;

    constexpr NanosecondTickCount() noexcept = default;

    constexpr explicit NanosecondTickCount(const int64_t nanoseconds) noexcept
      : m_nanoseconds(static_cast<uint64_t>(nanoseconds))
    {
    }

    //! The point sinceEpoch after the clock's epoch.
    constexpr explicit NanosecondTickCount(const NanosecondTimeSpan sinceEpoch) noexcept
      : NanosecondTickCount(sinceEpoch.Nanoseconds())
    {
    }

    static constexpr NanosecondTickCount FromNanoseconds(const int64_t nanoseconds) noexcept
    {
      return NanosecondTickCount(nanoseconds);
    }

    //! The count as stored (UnsignedNanoseconds()).
    static constexpr NanosecondTickCount FromUnsignedNanoseconds(const uint64_t nanoseconds) noexcept
    {
      NanosecondTickCount count;
      count.m_nanoseconds = nanoseconds;
      return count;
    }

    //! Throws std::overflow_error outside MinSeconds to MaxSeconds.
    static constexpr NanosecondTickCount FromSeconds(const int64_t seconds)
    {
      return FromUnits(seconds, MinSeconds, MaxSeconds, NanosecondsPerSecond);
    }

    //! Throws std::overflow_error outside MinMilliseconds to MaxMilliseconds.
    static constexpr NanosecondTickCount FromMilliseconds(const int64_t milliseconds)
    {
      return FromUnits(milliseconds, MinMilliseconds, MaxMilliseconds, NanosecondsPerMillisecond);
    }

    //! Throws std::overflow_error outside MinMicroseconds to MaxMicroseconds.
    static constexpr NanosecondTickCount FromMicroseconds(const int64_t microseconds)
    {
      return FromUnits(microseconds, MinMicroseconds, MaxMicroseconds, NanosecondsPerMicrosecond);
    }

    //! A point in ticks of 100 ns, exactly. Throws std::overflow_error when its ticks are outside MinTicks to MaxTicks (a
    //! TickCount64 reaches a hundred times as far).
    static constexpr NanosecondTickCount FromTickCount64(const TickCount64 count)
    {
      return FromUnits(count.Ticks(), MinTicks, MaxTicks, NanosecondsPerTick);
    }

    //! A counter value of a clock that counts frequency times a second (QueryPerformanceCounter with QueryPerformanceFrequency, .NET's
    //! Stopwatch), rounded down to the nanosecond it is in, as TickCount64::FromCounter is to the tick. The whole seconds and the rest
    //! are converted apart, so it is exact for every counter value whose time fits a NanosecondTickCount (every one, from 1 GHz on); a
    //! slower counter's time past that wraps, as the count does. Throws std::out_of_range for a frequency that is not 1 to
    //! MaxCounterFrequency.
    static constexpr NanosecondTickCount FromCounter(const int64_t counter, const int64_t frequency)
    {
      if (frequency <= 0 || frequency > MaxCounterFrequency)
      {
        throw std::out_of_range("NanosecondTickCount: the counter frequency must be 1 to MaxCounterFrequency");
      }
      int64_t seconds = counter / frequency;
      int64_t rest = counter % frequency;
      if (rest < 0)
      {
        --seconds;
        rest += frequency;
      }
      // rest < frequency <= MaxCounterFrequency, so rest * NanosecondsPerSecond fits. The seconds are multiplied unsigned: they wrap
      // when a slow counter's time is past the range, where the signed product would be undefined.
      const uint64_t wholeSeconds = static_cast<uint64_t>(seconds) * static_cast<uint64_t>(NanosecondsPerSecond);
      return FromUnsignedNanoseconds(wholeSeconds + static_cast<uint64_t>((rest * NanosecondsPerSecond) / frequency));
    }

    //! The count as a signed number of nanoseconds.
    [[nodiscard]] constexpr int64_t Nanoseconds() const noexcept
    {
      return static_cast<int64_t>(m_nanoseconds);
    }

    //! The count as stored.
    [[nodiscard]] constexpr uint64_t UnsignedNanoseconds() const noexcept
    {
      return m_nanoseconds;
    }

    //! The tick of 100 ns the point is in: rounded down, as TickCount64::FromNanoseconds.
    [[nodiscard]] constexpr TickCount64 ToTickCount64() const noexcept
    {
      return TickCount64::FromNanoseconds(Nanoseconds());
    }

    //! The time since the clock's epoch.
    [[nodiscard]] constexpr NanosecondTimeSpan ToNanosecondTimeSpan() const noexcept
    {
      return NanosecondTimeSpan(Nanoseconds());
    }

    [[nodiscard]] constexpr double TotalMicroseconds() const noexcept
    {
      return static_cast<double>(Nanoseconds()) / NanosecondsPerMicrosecond;
    }

    [[nodiscard]] constexpr double TotalMilliseconds() const noexcept
    {
      return static_cast<double>(Nanoseconds()) / NanosecondsPerMillisecond;
    }

    [[nodiscard]] constexpr double TotalSeconds() const noexcept
    {
      return static_cast<double>(Nanoseconds()) / NanosecondsPerSecond;
    }

    //! Wraps around.
    constexpr NanosecondTickCount& operator+=(const NanosecondTimeSpan span) noexcept
    {
      m_nanoseconds += static_cast<uint64_t>(span.Nanoseconds());
      return *this;
    }

    //! Wraps around.
    constexpr NanosecondTickCount& operator-=(const NanosecondTimeSpan span) noexcept
    {
      m_nanoseconds -= static_cast<uint64_t>(span.Nanoseconds());
      return *this;
    }

    friend constexpr NanosecondTickCount operator+(NanosecondTickCount count, const NanosecondTimeSpan span) noexcept
    {
      return count += span;
    }

    friend constexpr NanosecondTickCount operator-(NanosecondTickCount count, const NanosecondTimeSpan span) noexcept
    {
      return count -= span;
    }

    //! The time from rhs to lhs, the shorter way round: correct while the counts are less than 2^63 nanoseconds apart.
    friend constexpr NanosecondTimeSpan operator-(const NanosecondTickCount lhs, const NanosecondTickCount rhs) noexcept
    {
      return NanosecondTimeSpan(Distance(lhs, rhs));
    }

    constexpr bool operator==(const NanosecondTickCount&) const noexcept = default;

    //! lhs is before rhs: the wrap-safe order of TickCount64, for counts less than 2^63 nanoseconds apart.
    friend constexpr bool operator<(const NanosecondTickCount lhs, const NanosecondTickCount rhs) noexcept
    {
      return Distance(lhs, rhs) < 0;
    }

    friend constexpr bool operator<=(const NanosecondTickCount lhs, const NanosecondTickCount rhs) noexcept
    {
      return Distance(lhs, rhs) <= 0;
    }

    friend constexpr bool operator>(const NanosecondTickCount lhs, const NanosecondTickCount rhs) noexcept
    {
      return Distance(lhs, rhs) > 0;
    }

    friend constexpr bool operator>=(const NanosecondTickCount lhs, const NanosecondTickCount rhs) noexcept
    {
      return Distance(lhs, rhs) >= 0;
    }

  private:
    //! The nanoseconds from rhs to lhs as a signed 64-bit number: the unsigned difference wraps, its signed view is the shorter
    //! way round.
    static constexpr int64_t Distance(const NanosecondTickCount lhs, const NanosecondTickCount rhs) noexcept
    {
      return static_cast<int64_t>(lhs.m_nanoseconds - rhs.m_nanoseconds);
    }

    static constexpr NanosecondTickCount FromUnits(const int64_t value, const int64_t minValue, const int64_t maxValue,
                                                   const int64_t nanosecondsPerUnit)
    {
      if (value < minValue || value > maxValue)
      {
        throw std::overflow_error("NanosecondTickCount: the value is outside the range of a NanosecondTickCount");
      }
      return NanosecondTickCount(value * nanosecondsPerUnit);
    }
  };
}

#endif
