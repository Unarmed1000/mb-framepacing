// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. A time counted in whole refreshes (sdk/doc/pacer.md): whole nanoseconds and a fraction of a nanosecond in 2^-32.
#include <mb/framepacing/pacer/RefreshTime.hpp>

namespace MB::FramePacing::Pacer
{
  namespace
  {
    constexpr uint32_t HalfANanosecond = uint32_t{1} << 31u;
  }

  void RefreshTime::Add(const uint32_t refreshes, const RefreshPeriod period) noexcept
  {
    // The period's whole nanoseconds and its fraction apart, so nothing overflows
    const int64_t wholeNanoseconds = period.m_nanosecondsQ32 >> 32u;
    const auto fraction = static_cast<uint64_t>(period.m_nanosecondsQ32) & 0xFFFF'FFFFu;
    const uint64_t fractionSum = uint64_t{m_fraction} + (uint64_t{refreshes} * fraction);
    m_whole = NanosecondTimeSpan(m_whole.Nanoseconds() + (int64_t{refreshes} * wholeNanoseconds) + static_cast<int64_t>(fractionSum >> 32u));
    m_fraction = static_cast<uint32_t>(fractionSum & 0xFFFF'FFFFu);
  }

  RefreshTime RefreshTime::After(const uint32_t refreshes, const RefreshPeriod period) const noexcept
  {
    RefreshTime later = *this;
    later.Add(refreshes, period);
    return later;
  }

  NanosecondTimeSpan RefreshTime::ToNanosecondTimeSpan() const noexcept
  {
    return NanosecondTimeSpan(m_whole.Nanoseconds() + (m_fraction >= HalfANanosecond ? 1 : 0));
  }
}
