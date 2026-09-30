#ifndef MB_FRAMEPACING_PACER_REFRESHPERIOD_HPP
#define MB_FRAMEPACING_PACER_REFRESHPERIOD_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/Ticks.hpp>
#include <algorithm>
#include <cassert>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! The display's refresh period, exact to 2^-32 of a tick (100 ns). A period in whole ticks would drift: 60 Hz is 166'666.67 ticks, and
  //! rounding it to 166'667 adds 2 µs a second. TicksFor computes the time of any number of refreshes from the exact period, so a grid of
  //! refreshes stays on the rate it was made from.
  //!
  //! Always valid: from MinTicksQ32 (1 tick, a 10 MHz rate) to MaxTicksQ32 (1 s, 1 Hz). The factories assert that; without asserts they
  //! clamp a period outside into the range. There is no default: the application gives the pacer its display's period.
  class RefreshPeriod
  {
    int64_t m_ticksQ32;

    constexpr explicit RefreshPeriod(const int64_t ticksQ32) noexcept
      : m_ticksQ32(std::clamp(ticksQ32, MinTicksQ32, MaxTicksQ32))
    {
      assert(ticksQ32 >= MinTicksQ32 && ticksQ32 <= MaxTicksQ32);
    }

  public:
    //! One tick in the fixed point of TicksQ32
    static constexpr int64_t OneTickQ32 = int64_t{1} << 32;
    //! The shortest period: 1 tick (100 ns)
    static constexpr int64_t MinTicksQ32 = OneTickQ32;
    //! The longest period: 1 s
    static constexpr int64_t MaxTicksQ32 = TicksPerSecond * OneTickQ32;

    //! A refresh rate of numerator / denominator Hz: a DXGI_RATIONAL (60000 / 1001), wl_output's mHz (59940 / 1000), a whole rate (60).
    //! From 1 Hz to 10 MHz; a numerator or denominator of 0 is outside.
    static constexpr RefreshPeriod FromRate(const uint32_t numerator, const uint32_t denominator = 1) noexcept
    {
      if (numerator == 0)
      {
        return RefreshPeriod(MaxTicksQ32 + 1);
      }
      // TicksPerSecond * denominator / numerator, the whole ticks and the rest apart so nothing overflows (a period beyond the range
      // stops just past it)
      const auto dividend = static_cast<uint64_t>(TicksPerSecond) * denominator;
      const uint64_t whole = std::min(dividend / numerator, static_cast<uint64_t>(TicksPerSecond) + 1u);
      const uint64_t rest = dividend % numerator;
      const uint64_t fraction = ((rest << 32u) + (numerator / 2u)) / numerator;
      return RefreshPeriod(static_cast<int64_t>((whole << 32u) + fraction));
    }

    //! A period in whole ticks: 1 to TicksPerSecond.
    static constexpr RefreshPeriod FromTicks(const int64_t ticks) noexcept
    {
      return RefreshPeriod(std::clamp(ticks, int64_t{-1}, TicksPerSecond + 1) * OneTickQ32);
    }

    //! A period in nanoseconds (Choreographer's vsync period, VK_GOOGLE_display_timing's refreshDuration), exact to 2^-32 tick: 100 ns
    //! to 1 s.
    static constexpr RefreshPeriod FromNanoseconds(const int64_t nanoseconds) noexcept
    {
      const int64_t limited = std::clamp(nanoseconds, int64_t{-1}, TicksToNanoseconds(TicksPerSecond) + 1);
      const int64_t whole = limited / 100;
      const int64_t rest = limited % 100;
      return RefreshPeriod((whole * OneTickQ32) + (((rest * OneTickQ32) + 50) / 100));
    }

    //! A period in ticks times 2^32 (TicksQ32 of another period): MinTicksQ32 to MaxTicksQ32.
    static constexpr RefreshPeriod FromTicksQ32(const int64_t ticksQ32) noexcept
    {
      return RefreshPeriod(ticksQ32);
    }

    //! The period in ticks times 2^32.
    [[nodiscard]] constexpr int64_t TicksQ32() const noexcept
    {
      return m_ticksQ32;
    }

    //! The period rounded to the nearest tick.
    [[nodiscard]] constexpr int64_t Ticks() const noexcept
    {
      return (m_ticksQ32 + (OneTickQ32 / 2)) >> 32u;
    }

    //! The period rounded to the nearest nanosecond: what a platform reports it as (FrameInput::RefreshPeriodNanoseconds).
    [[nodiscard]] constexpr int64_t Nanoseconds() const noexcept
    {
      return ((m_ticksQ32 * 100) + (OneTickQ32 / 2)) >> 32u;
    }

    //! refreshes times the period, rounded to the nearest tick (half up). Exact for any refreshes up to 2^31 (a month at 1000 Hz).
    [[nodiscard]] constexpr int64_t TicksFor(const int64_t refreshes) const noexcept
    {
      if (refreshes < 0)
      {
        return -TicksFor(-refreshes);
      }
      const int64_t whole = m_ticksQ32 >> 32u;
      const auto fraction = static_cast<uint64_t>(m_ticksQ32) & 0xFFFF'FFFFu;
      const uint64_t fractionTicks = ((static_cast<uint64_t>(refreshes) * fraction) + (uint64_t{1} << 31u)) >> 32u;
      return (refreshes * whole) + static_cast<int64_t>(fractionTicks);
    }

    //! The whole number of refreshes nearest to ticks (half up; 0 for ticks <= 0).
    [[nodiscard]] constexpr int64_t NearestRefreshes(const int64_t ticks) const noexcept
    {
      if (ticks <= 0)
      {
        return 0;
      }
      int64_t refreshes = FloorRefreshes(ticks);
      // The nearer of the two refreshes around ticks, the later one on a tie
      if ((ticks - TicksFor(refreshes)) * 2 >= TicksFor(refreshes + 1) - TicksFor(refreshes))
      {
        ++refreshes;
      }
      return refreshes;
    }

    //! The most refreshes that fit in ticks: the largest n with TicksFor(n) <= ticks (0 for ticks <= 0).
    [[nodiscard]] constexpr int64_t FloorRefreshes(const int64_t ticks) const noexcept
    {
      if (ticks <= 0)
      {
        return 0;
      }
      // An estimate from the rounded period (at least 1 tick), then exact: the rounding is at most half a tick a refresh
      int64_t refreshes = ticks / Ticks();
      while (refreshes > 0 && TicksFor(refreshes) > ticks)
      {
        --refreshes;
      }
      while (TicksFor(refreshes + 1) <= ticks)
      {
        ++refreshes;
      }
      return refreshes;
    }

    constexpr bool operator==(const RefreshPeriod&) const noexcept = default;
  };
}

#endif
