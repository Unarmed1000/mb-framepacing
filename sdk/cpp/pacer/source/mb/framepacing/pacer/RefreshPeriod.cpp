// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The refresh period of sdk/doc/pacer.md: ticks times 2^32, the whole ticks and the fraction computed apart so nothing
// overflows.
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <algorithm>
#include <cassert>

namespace MB::FramePacing::Pacer
{
  namespace
  {
    constexpr int64_t OneTickQ32 = int64_t{1} << 32;
    constexpr int64_t MinTicksQ32 = RefreshPeriod::MinPeriod.Ticks() * OneTickQ32;
    constexpr int64_t MaxTicksQ32 = RefreshPeriod::MaxPeriod.Ticks() * OneTickQ32;
  }

  RefreshPeriod::RefreshPeriod(const int64_t ticksQ32) noexcept
    : m_ticksQ32(std::clamp(ticksQ32, MinTicksQ32, MaxTicksQ32))
  {
    assert(ticksQ32 >= MinTicksQ32 && ticksQ32 <= MaxTicksQ32);
  }

  RefreshPeriod RefreshPeriod::FromRate(const uint32_t numerator, const uint32_t denominator) noexcept
  {
    if (numerator == 0)
    {
      return RefreshPeriod(MaxTicksQ32 + 1);
    }
    // TicksPerSecond * denominator / numerator, the whole ticks and the rest apart so nothing overflows (a period beyond the range stops
    // just past it)
    const auto dividend = static_cast<uint64_t>(TimeSpan::TicksPerSecond) * denominator;
    const uint64_t whole = std::min(dividend / numerator, static_cast<uint64_t>(TimeSpan::TicksPerSecond) + 1u);
    const uint64_t rest = dividend % numerator;
    const uint64_t fraction = ((rest << 32u) + (numerator / 2u)) / numerator;
    return RefreshPeriod(static_cast<int64_t>((whole << 32u) + fraction));
  }

  RefreshPeriod RefreshPeriod::FromTimeSpan(const TimeSpan period) noexcept
  {
    return RefreshPeriod(std::clamp(period.Ticks(), int64_t{-1}, TimeSpan::TicksPerSecond + 1) * OneTickQ32);
  }

  RefreshPeriod RefreshPeriod::FromNanoseconds(const int64_t nanoseconds) noexcept
  {
    const int64_t limited = std::clamp(nanoseconds, int64_t{-1}, (TimeSpan::TicksPerSecond * TimeSpan::NanosecondsPerTick) + 1);
    const int64_t whole = limited / TimeSpan::NanosecondsPerTick;
    const int64_t rest = limited % TimeSpan::NanosecondsPerTick;
    return RefreshPeriod((whole * OneTickQ32) + (((rest * OneTickQ32) + (TimeSpan::NanosecondsPerTick / 2)) / TimeSpan::NanosecondsPerTick));
  }

  TimeSpan RefreshPeriod::ToTimeSpan() const noexcept
  {
    return TimeSpan((m_ticksQ32 + (OneTickQ32 / 2)) >> 32u);
  }

  TimeSpan RefreshPeriod::TimeFor(const int64_t refreshes) const noexcept
  {
    assert(refreshes >= 0);
    const int64_t count = std::max(refreshes, int64_t{0});
    const int64_t whole = m_ticksQ32 >> 32u;
    const auto fraction = static_cast<uint64_t>(m_ticksQ32) & 0xFFFF'FFFFu;
    const uint64_t fractionTicks = ((static_cast<uint64_t>(count) * fraction) + (uint64_t{1} << 31u)) >> 32u;
    return TimeSpan((count * whole) + static_cast<int64_t>(fractionTicks));
  }

  int64_t RefreshPeriod::NearestRefreshes(const TimeSpan span) const noexcept
  {
    if (span <= TimeSpan())
    {
      return 0;
    }
    int64_t refreshes = FloorRefreshes(span);
    // The nearer of the two refreshes around the span, the later one on a tie
    const int64_t before = TimeFor(refreshes).Ticks();
    if ((span.Ticks() - before) * 2 >= TimeFor(refreshes + 1).Ticks() - before)
    {
      ++refreshes;
    }
    return refreshes;
  }

  int64_t RefreshPeriod::FloorRefreshes(const TimeSpan span) const noexcept
  {
    if (span <= TimeSpan())
    {
      return 0;
    }
    // An estimate from the rounded period, then exact: the rounding is at most half a tick a refresh
    int64_t refreshes = span.Ticks() / ToTimeSpan().Ticks();
    while (refreshes > 0 && TimeFor(refreshes) > span)
    {
      --refreshes;
    }
    while (TimeFor(refreshes + 1) <= span)
    {
      ++refreshes;
    }
    return refreshes;
  }

  int64_t RefreshPeriod::RefreshesToFit(const TimeSpan span) const noexcept
  {
    const int64_t refreshes = FloorRefreshes(span);
    return TimeFor(refreshes) < span ? refreshes + 1 : refreshes;
  }
}
