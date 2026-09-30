#ifndef MB_FRAMEPACING_CORE_TESTS_STEADYCLOCK_HPP
#define MB_FRAMEPACING_CORE_TESTS_STEADYCLOCK_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/Ticks.hpp>
#include <chrono>
#include <cstdint>

namespace MB::FramePacing
{
  //! A test helper: std::chrono::steady_clock in ticks, to check TickDuration against a real clock. The SDK itself never reads a clock;
  //! applications pass their own clock's times.
  struct SteadyClock
  {
    static int64_t NowTicks() noexcept
    {
      return std::chrono::duration_cast<TickDuration>(std::chrono::steady_clock::now().time_since_epoch()).count();
    }
  };
}

#endif
