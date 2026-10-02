#ifndef MB_FRAMEPACING_PACER_REFRESHPERIOD_HPP
#define MB_FRAMEPACING_PACER_REFRESHPERIOD_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer.md). The display's refresh period, exact to 2^-32 of a tick (100 ns). A period in whole
  //! ticks would drift: 60 Hz is 166'666.67 ticks, and rounding it to 166'667 adds 2 µs a second. TimeFor computes the time of any number
  //! of refreshes from the exact period, and RefreshTime adds refreshes up exactly, so counting refreshes stays on the rate the period was
  //! made from.
  //!
  //! Always valid: from MinPeriod (100 µs, 10 kHz) to MaxPeriod (1 s, 1 Hz). The factories assert that; without asserts they clamp a
  //! period outside into the range. There is no default: the application gives the pacer its display's period.
  class RefreshPeriod
  {
    friend class RefreshTime;

    // The period in ticks times 2^32
    int64_t m_ticksQ32;

  public:
    //! The shortest period: 100 µs (10 kHz)
    static constexpr TimeSpan MinPeriod{TimeSpan::TicksPerMillisecond / 10};
    //! The longest period: 1 s (1 Hz)
    static constexpr TimeSpan MaxPeriod{TimeSpan::TicksPerSecond};

    //! A refresh rate of numerator / denominator Hz: a DXGI_RATIONAL (60000 / 1001), wl_output's mHz (59940 / 1000), a whole rate (60).
    //! From 1 Hz to 10 kHz; a numerator or denominator of 0 is outside.
    [[nodiscard]] static RefreshPeriod FromRate(uint32_t numerator, uint32_t denominator = 1) noexcept;

    //! A period in whole ticks: MinPeriod to MaxPeriod.
    [[nodiscard]] static RefreshPeriod FromTimeSpan(TimeSpan period) noexcept;

    //! A period in nanoseconds (Choreographer's vsync period, a display timing's refresh duration), exact to 2^-32 tick: 100 µs to 1 s.
    [[nodiscard]] static RefreshPeriod FromNanoseconds(int64_t nanoseconds) noexcept;

    //! The period rounded to the nearest tick.
    [[nodiscard]] TimeSpan ToTimeSpan() const noexcept;

    //! refreshes times the period, rounded to the nearest tick (half up). Exact for any refreshes from 0 to 2^31 (a month at 1000 Hz).
    [[nodiscard]] TimeSpan TimeFor(int64_t refreshes) const noexcept;

    //! The whole number of refreshes nearest to span (the later one on a tie; 0 for a span of 0 or less).
    [[nodiscard]] int64_t NearestRefreshes(TimeSpan span) const noexcept;

    //! The most refreshes that fit in span: the largest n with TimeFor(n) <= span (0 for a span of 0 or less).
    [[nodiscard]] int64_t FloorRefreshes(TimeSpan span) const noexcept;

    //! The fewest refreshes that take at least span: the smallest n with TimeFor(n) >= span (0 for a span of 0 or less).
    [[nodiscard]] int64_t RefreshesToFit(TimeSpan span) const noexcept;

    constexpr bool operator==(const RefreshPeriod&) const noexcept = default;

  private:
    explicit RefreshPeriod(int64_t ticksQ32) noexcept;
  };
}

#endif
