#ifndef MB_FRAMEPACING_CORE_TICKS_HPP
#define MB_FRAMEPACING_CORE_TICKS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The time unit of the SDK: 100 ns ticks, C# TimeSpan / DateTime ticks. Every time the markers carry and the data files store is in
// ticks. Platforms report other units (nanoseconds, a performance counter with its frequency); these convert them.

#include <chrono>
#include <cstdint>
#include <ratio>

namespace MB::FramePacing
{
  //! C# TimeSpan / DateTime resolution
  inline constexpr int64_t TicksPerSecond = 10'000'000;
  inline constexpr int64_t TicksPerMillisecond = 10'000;
  //! C# DateTime ticks (since 0001-01-01) at the Unix epoch
  inline constexpr int64_t UnixEpochDateTimeTicks = 621'355'968'000'000'000;

  //! A std::chrono duration in ticks: std::chrono::duration_cast<TickDuration>(steady_clock::now().time_since_epoch()).count()
  using TickDuration = std::chrono::duration<int64_t, std::ratio<1, TicksPerSecond>>;

  //! Nanoseconds (CLOCK_MONOTONIC, Vulkan and EGL present times, Choreographer) as ticks, rounded down: a time stays on the tick it is in.
  constexpr int64_t NanosecondsToTicks(const int64_t nanoseconds) noexcept
  {
    const int64_t ticks = nanoseconds / 100;
    return (nanoseconds % 100) < 0 ? ticks - 1 : ticks;
  }

  //! Ticks as nanoseconds (exact; valid for the next 29 000 years of any epoch).
  constexpr int64_t TicksToNanoseconds(const int64_t ticks) noexcept
  {
    return ticks * 100;
  }

  //! A counter value of a clock that counts frequency times a second (QueryPerformanceCounter with QueryPerformanceFrequency, .NET's
  //! Stopwatch) as ticks, rounded down. Exact for any counter value: the whole seconds and the rest are converted apart, so nothing
  //! overflows. frequency must be positive.
  constexpr int64_t CounterToTicks(const int64_t counter, const int64_t frequency) noexcept
  {
    int64_t seconds = counter / frequency;
    int64_t rest = counter % frequency;
    if (rest < 0)
    {
      --seconds;
      rest += frequency;
    }
    // rest < frequency, and a frequency above 2^63 / 10^7 (about 922 GHz) is not a clock any platform has
    return (seconds * TicksPerSecond) + ((rest * TicksPerSecond) / frequency);
  }

  //! Convert a wall clock time to C# DateTime UTC ticks (the marker's StartMetadata::UtcTicks format).
  constexpr int64_t ToDateTimeTicks(const std::chrono::system_clock::time_point timePoint) noexcept
  {
    return UnixEpochDateTimeTicks + std::chrono::duration_cast<TickDuration>(timePoint.time_since_epoch()).count();
  }
}

#endif
