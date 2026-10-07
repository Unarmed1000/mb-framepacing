#ifndef MB_FRAMEPACING_PACER_REFRESHPERIOD_HPP
#define MB_FRAMEPACING_PACER_REFRESHPERIOD_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer.md). The display's refresh period, exact to 2^-32 of a nanosecond. A period
  //! in whole nanoseconds would drift: 60 Hz is 16'666'666.67 ns, and rounding it to 16'666'667 adds 20 ns a second. TimeFor computes
  //! the time of any number of refreshes from the exact period, and RefreshTime adds refreshes up exactly, so counting refreshes stays on
  //! the rate the period was made from.
  //!
  //! Always valid: from MinPeriod (100 µs, 10 kHz) to MaxPeriod (1 s, 1 Hz). The factories assert that; without asserts they clamp a
  //! period outside into the range. There is no default: the application gives the pacer its display's period.
  class RefreshPeriod
  {
    friend class RefreshTime;

    // The period in nanoseconds times 2^32
    int64_t m_nanosecondsQ32;

  public:
    //! The shortest period: 100 µs (10 kHz)
    static constexpr NanosecondTimeSpan MinPeriod{NanosecondTimeSpan::NanosecondsPerMillisecond / 10};
    //! The most refreshes RateMillihertz gives a rate for
    static constexpr uint32_t MaxRateRefreshes = 1000;

    //! The longest period: 1 s (1 Hz)
    static constexpr NanosecondTimeSpan MaxPeriod{NanosecondTimeSpan::NanosecondsPerSecond};

    //! A refresh rate of numerator / denominator Hz: a DXGI_RATIONAL (60000 / 1001), wl_output's mHz (59940 / 1000), a whole rate (60).
    //! From 1 Hz to 10 kHz; a numerator or denominator of 0 is outside.
    [[nodiscard]] static RefreshPeriod FromRate(uint32_t numerator, uint32_t denominator = 1) noexcept;

    //! A period in whole nanoseconds, as a platform gives one (a vsync period, a display timing's refresh duration): MinPeriod to
    //! MaxPeriod.
    [[nodiscard]] static RefreshPeriod FromNanosecondTimeSpan(NanosecondTimeSpan period) noexcept;

    //! The period rounded to the nearest nanosecond.
    [[nodiscard]] NanosecondTimeSpan ToNanosecondTimeSpan() const noexcept;

    //! refreshes times the period, rounded to the nearest nanosecond (half up). Exact for any refreshes from 0 to 2^31 (a month at 1000 Hz).
    [[nodiscard]] NanosecondTimeSpan TimeFor(int64_t refreshes) const noexcept;

    //! The rate of a frame every so many refreshes, in millihertz, the nearest: 59'940 at 59.94 Hz, and 29'970 for every second
    //! refresh of it. A number to show; nothing is paced by it. refreshes from 1 to MaxRateRefreshes (asserted, then clamped).
    [[nodiscard]] uint32_t RateMillihertz(uint32_t refreshes = 1) const noexcept;

    //! The whole number of refreshes nearest to span (the later one on a tie; 0 for a span of 0 or less).
    [[nodiscard]] int64_t NearestRefreshes(NanosecondTimeSpan span) const noexcept;

    //! The most refreshes that fit in span: the largest n with TimeFor(n) <= span (0 for a span of 0 or less).
    [[nodiscard]] int64_t FloorRefreshes(NanosecondTimeSpan span) const noexcept;

    //! The fewest refreshes that take at least span: the smallest n with TimeFor(n) >= span (0 for a span of 0 or less).
    [[nodiscard]] int64_t RefreshesToFit(NanosecondTimeSpan span) const noexcept;

    constexpr bool operator==(const RefreshPeriod&) const noexcept = default;

  private:
    explicit RefreshPeriod(int64_t nanosecondsQ32) noexcept;
  };
}

#endif
