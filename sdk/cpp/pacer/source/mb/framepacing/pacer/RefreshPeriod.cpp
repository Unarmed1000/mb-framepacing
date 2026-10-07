// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The refresh period of sdk/doc/pacer.md: nanoseconds times 2^32, the whole nanoseconds and the fraction computed apart so
// nothing overflows.
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <algorithm>
#include <cassert>

namespace MB::FramePacing::Pacer
{
  namespace
  {
    constexpr int64_t OneNanosecondQ32 = int64_t{1} << 32;
    constexpr int64_t MinNanosecondsQ32 = RefreshPeriod::MinPeriod.Nanoseconds() * OneNanosecondQ32;
    constexpr int64_t MaxNanosecondsQ32 = RefreshPeriod::MaxPeriod.Nanoseconds() * OneNanosecondQ32;
  }

  RefreshPeriod::RefreshPeriod(const int64_t nanosecondsQ32) noexcept
    : m_nanosecondsQ32(std::clamp(nanosecondsQ32, MinNanosecondsQ32, MaxNanosecondsQ32))
  {
    assert(nanosecondsQ32 >= MinNanosecondsQ32 && nanosecondsQ32 <= MaxNanosecondsQ32);
  }

  RefreshPeriod RefreshPeriod::FromRate(const uint32_t numerator, const uint32_t denominator) noexcept
  {
    if (numerator == 0)
    {
      return RefreshPeriod(MaxNanosecondsQ32 + 1);
    }
    // NanosecondsPerSecond * denominator / numerator, the whole nanoseconds and the rest apart so nothing overflows (a period beyond the range stops
    // just past it)
    const auto dividend = static_cast<uint64_t>(NanosecondTimeSpan::NanosecondsPerSecond) * denominator;
    const uint64_t whole = std::min(dividend / numerator, static_cast<uint64_t>(NanosecondTimeSpan::NanosecondsPerSecond) + 1u);
    const uint64_t rest = dividend % numerator;
    const uint64_t fraction = ((rest << 32u) + (numerator / 2u)) / numerator;
    return RefreshPeriod(static_cast<int64_t>((whole << 32u) + fraction));
  }

  RefreshPeriod RefreshPeriod::FromNanosecondTimeSpan(const NanosecondTimeSpan period) noexcept
  {
    return RefreshPeriod(std::clamp(period.Nanoseconds(), int64_t{-1}, NanosecondTimeSpan::NanosecondsPerSecond + 1) * OneNanosecondQ32);
  }

  NanosecondTimeSpan RefreshPeriod::ToNanosecondTimeSpan() const noexcept
  {
    return NanosecondTimeSpan((m_nanosecondsQ32 + (OneNanosecondQ32 / 2)) >> 32u);
  }

  NanosecondTimeSpan RefreshPeriod::TimeFor(const int64_t refreshes) const noexcept
  {
    assert(refreshes >= 0);
    const int64_t count = std::max(refreshes, int64_t{0});
    const int64_t whole = m_nanosecondsQ32 >> 32u;
    const auto fraction = static_cast<uint64_t>(m_nanosecondsQ32) & 0xFFFF'FFFFu;
    const uint64_t fractionNanoseconds = ((static_cast<uint64_t>(count) * fraction) + (uint64_t{1} << 31u)) >> 32u;
    return NanosecondTimeSpan((count * whole) + static_cast<int64_t>(fractionNanoseconds));
  }

  uint32_t RefreshPeriod::RateMillihertz(const uint32_t refreshes) const noexcept
  {
    assert(refreshes >= 1u && refreshes <= MaxRateRefreshes);
    // 10^12 millihertz-nanoseconds over the frame's time, both times 2^24 so that the dividend fits 64 bits: the period keeps
    // 24 of its 32 fraction bits, far more than a millihertz needs
    constexpr uint64_t Dividend = uint64_t{1'000'000'000'000} << 24u;
    const uint64_t divisor = uint64_t{std::clamp(refreshes, 1u, MaxRateRefreshes)} * (static_cast<uint64_t>(m_nanosecondsQ32) >> 8u);
    const uint64_t whole = Dividend / divisor;
    const uint64_t rest = Dividend % divisor;
    return static_cast<uint32_t>(whole + (rest >= divisor - rest ? 1u : 0u));
  }

  int64_t RefreshPeriod::NearestRefreshes(const NanosecondTimeSpan span) const noexcept
  {
    if (span <= NanosecondTimeSpan())
    {
      return 0;
    }
    int64_t refreshes = FloorRefreshes(span);
    // The nearer of the two refreshes around the span, the later one on a tie
    const int64_t before = TimeFor(refreshes).Nanoseconds();
    if ((span.Nanoseconds() - before) * 2 >= TimeFor(refreshes + 1).Nanoseconds() - before)
    {
      ++refreshes;
    }
    return refreshes;
  }

  int64_t RefreshPeriod::FloorRefreshes(const NanosecondTimeSpan span) const noexcept
  {
    if (span <= NanosecondTimeSpan())
    {
      return 0;
    }
    // An estimate from the rounded period, then exact: the rounding is at most half a nanosecond a refresh
    int64_t refreshes = span.Nanoseconds() / ToNanosecondTimeSpan().Nanoseconds();
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

  int64_t RefreshPeriod::RefreshesToFit(const NanosecondTimeSpan span) const noexcept
  {
    const int64_t refreshes = FloorRefreshes(span);
    return TimeFor(refreshes) < span ? refreshes + 1 : refreshes;
  }
}
