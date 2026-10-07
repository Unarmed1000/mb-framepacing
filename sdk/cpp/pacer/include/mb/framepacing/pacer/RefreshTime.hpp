#ifndef MB_FRAMEPACING_PACER_REFRESHTIME_HPP
#define MB_FRAMEPACING_PACER_REFRESHTIME_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer.md). A time counted in whole refreshes: refreshes of a RefreshPeriod added up exactly
  //! (the period's fraction of a nanosecond is carried, so nothing drifts), read as a NanosecondTimeSpan rounded to the nearest nanosecond. An hour
  //! of 60 Hz refreshes added one at a time is an hour to the nanosecond.
  class RefreshTime
  {
    NanosecondTimeSpan m_whole;
    // The rest: a fraction of a nanosecond in 2^-32
    uint32_t m_fraction{0};

  public:
    constexpr RefreshTime() noexcept = default;

    //! A time that starts at a whole nanosecond.
    constexpr explicit RefreshTime(const NanosecondTimeSpan start) noexcept
      : m_whole(start)
    {
    }

    //! Moves the time on by refreshes of period.
    void Add(uint32_t refreshes, RefreshPeriod period) noexcept;

    //! The time refreshes of period later.
    [[nodiscard]] RefreshTime After(uint32_t refreshes, RefreshPeriod period) const noexcept;

    //! The time rounded to the nearest nanosecond (half up).
    [[nodiscard]] NanosecondTimeSpan ToNanosecondTimeSpan() const noexcept;

    constexpr bool operator==(const RefreshTime&) const noexcept = default;
  };
}

#endif
