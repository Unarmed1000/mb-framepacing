#ifndef MB_FRAMEPACING_CORE_TIME_CHRONOCONVERSION_HPP
#define MB_FRAMEPACING_CORE_TIME_CHRONOCONVERSION_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Conversions between the SDK's times and std::chrono, for an application that wants them. Optional and header only: nothing else in the
// SDK includes it, so the SDK's types do not depend on <chrono>.

#include <mb/framepacing/core/time/TickCount32.hpp>
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <chrono>
#include <cstdint>
#include <ratio>

namespace MB::FramePacing
{
  //! A std::chrono duration in ticks. A coarser duration (std::chrono::milliseconds) converts into it exactly and implicitly; a finer one
  //! (std::chrono::nanoseconds) needs std::chrono::floor<TickDuration> first, so nothing is cut off silently.
  using TickDuration = std::chrono::duration<int64_t, std::ratio<1, TimeSpan::TicksPerSecond>>;

  constexpr TimeSpan ToTimeSpan(const TickDuration duration) noexcept
  {
    return TimeSpan(duration.count());
  }

  constexpr TickDuration ToTickDuration(const TimeSpan span) noexcept
  {
    return TickDuration(span.Ticks());
  }

  //! Throws std::out_of_range for a negative duration or one longer than TimeSpan32::MaxValue(), as TimeSpan32::FromTimeSpan.
  constexpr TimeSpan32 ToTimeSpan32(const TickDuration duration)
  {
    return TimeSpan32::FromTimeSpan(ToTimeSpan(duration));
  }

  constexpr TickDuration ToTickDuration(const TimeSpan32 span) noexcept
  {
    return TickDuration(span.Ticks());
  }

  //! A time point of a std::chrono clock (steady_clock) as a TickCount64: its time since the clock's epoch, rounded down to the tick it is
  //! in.
  template <typename TClock, typename TDuration>
  constexpr TickCount64 ToTickCount64(const std::chrono::time_point<TClock, TDuration> timePoint) noexcept
  {
    return TickCount64(std::chrono::floor<TickDuration>(timePoint.time_since_epoch()).count());
  }

  //! A time point of a std::chrono clock as a TickCount32: the low 32 bits of ToTickCount64.
  template <typename TClock, typename TDuration>
  constexpr TickCount32 ToTickCount32(const std::chrono::time_point<TClock, TDuration> timePoint) noexcept
  {
    return TickCount32::FromTickCount64(ToTickCount64(timePoint));
  }

  //! A TickCount64 as a time point of the std::chrono clock TClock, when the count came from that clock.
  template <typename TClock>
  constexpr std::chrono::time_point<TClock, TickDuration> ToTimePoint(const TickCount64 count) noexcept
  {
    return std::chrono::time_point<TClock, TickDuration>(TickDuration(count.Ticks()));
  }

  //! Convert a wall clock time to C# DateTime UTC ticks (the marker's StartMetadata::UtcTicks format).
  constexpr int64_t ToDateTimeTicks(const std::chrono::system_clock::time_point timePoint) noexcept
  {
    // C# DateTime ticks (since 0001-01-01) at the Unix epoch, system_clock's epoch
    constexpr int64_t UnixEpochDateTimeTicks = 621'355'968'000'000'000;
    return UnixEpochDateTimeTicks + std::chrono::duration_cast<TickDuration>(timePoint.time_since_epoch()).count();
  }
}

#endif
