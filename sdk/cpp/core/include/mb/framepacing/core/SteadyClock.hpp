#ifndef MB_FRAMEPACING_CORE_STEADYCLOCK_HPP
#define MB_FRAMEPACING_CORE_STEADYCLOCK_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/Ticks.hpp>
#include <chrono>
#include <cstdint>

namespace MB::FramePacing
{
  //! The steady clock in ticks: std::chrono::steady_clock (QueryPerformanceCounter on Windows, CLOCK_MONOTONIC on Linux and Android). Its
  //! epoch is arbitrary but the same for the whole process, which is what the marker's intended display time and CPU start time need.
  struct SteadyClock
  {
    static int64_t NowTicks() noexcept
    {
      return std::chrono::duration_cast<TickDuration>(std::chrono::steady_clock::now().time_since_epoch()).count();
    }
  };
}

#endif
