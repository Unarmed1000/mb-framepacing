// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. A time counted in whole refreshes (sdk/doc/pacer.md): whole ticks and a fraction of a tick in 2^-32.
#include <mb/framepacing/pacer/RefreshTime.hpp>

namespace MB::FramePacing::Pacer
{
  namespace
  {
    constexpr uint32_t HalfATick = uint32_t{1} << 31u;
  }

  void RefreshTime::Add(const uint32_t refreshes, const RefreshPeriod period) noexcept
  {
    // The period's whole ticks and its fraction apart, so nothing overflows
    const int64_t wholeTicks = period.m_ticksQ32 >> 32u;
    const auto fraction = static_cast<uint64_t>(period.m_ticksQ32) & 0xFFFF'FFFFu;
    const uint64_t fractionSum = uint64_t{m_fraction} + (uint64_t{refreshes} * fraction);
    m_whole = TimeSpan(m_whole.Ticks() + (int64_t{refreshes} * wholeTicks) + static_cast<int64_t>(fractionSum >> 32u));
    m_fraction = static_cast<uint32_t>(fractionSum & 0xFFFF'FFFFu);
  }

  RefreshTime RefreshTime::After(const uint32_t refreshes, const RefreshPeriod period) const noexcept
  {
    RefreshTime later = *this;
    later.Add(refreshes, period);
    return later;
  }

  TimeSpan RefreshTime::ToTimeSpan() const noexcept
  {
    return TimeSpan(m_whole.Ticks() + (m_fraction >= HalfATick ? 1 : 0));
  }
}
