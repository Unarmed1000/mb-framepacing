#ifndef MB_FRAMEPACING_PACER_CLOCK_PACERREFRESHCLOCK_HPP
#define MB_FRAMEPACING_PACER_CLOCK_PACERREFRESHCLOCK_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/RefreshTime.hpp>
#include <mb/framepacing/pacer/clock/AnimationTime.hpp>
#include <mb/framepacing/pacer/clock/FrameMeasurement.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer.md). The pacer's refresh clock: the display's refreshes, counted from the frame
  //! starts of a loop paced by vsync, and the animation time that follows from them (the technique mb-framepacing-explained calls the
  //! vsync timer). It is not the application's animation clock: it has no speed, no pause and no time of its own, only the refreshes
  //! the display has shown. It needs nothing from the platform but a steady clock.
  //!
  //! On a display with a fixed refresh rate and vsync on, every frame is shown a whole number of refreshes after the previous one, and
  //! the loop waits in Present, so every frame starts when the previous one is shown. The time from one frame start to the next, on the
  //! CPU's clock, rounded to whole refreshes and at least the swap interval, is how many refreshes passed on the display's clock
  //! (Measure). A frame animates for its predicted display time: the previous frame's display plus its own swap interval (Step). So the
  //! animation time advances in whole refreshes, a late frame shows as a whole extra refresh in the next measurement, and the frame
  //! after it catches up exactly.
  //!
  //! Rounding removes the frame starts' jitter while it stays under half a refresh. Every step is measured on its own, so a display
  //! that runs a little off its nominal rate never adds up to a jump; the steps add up exactly (RefreshTime), so the animation time is
  //! the refreshes counted. No allocation.
  class PacerRefreshClock
  {
    RefreshPeriod m_period;
    TimeSpan m_longestGap;
    RefreshTime m_animationTime;
    // The display's clock: the refreshes counted
    RefreshTime m_displayTime;
    TickCount64 m_lastStartTime;
    uint32_t m_lastSwapInterval{0};
    bool m_hasLast{false};
    bool m_stepped{false};
    FrameMeasurement m_measurement;
    AnimationTime m_current;

  public:
    //! longestGap: a frame that starts longer than this after the previous one (or two of its frames, when that is longer), or before
    //! it, starts the clock again instead of stepping by the gap: after a pause, a suspended process, a stop in a debugger. start: the
    //! animation time of the first frame.
    explicit PacerRefreshClock(RefreshPeriod period, TimeSpan longestGap, TimeSpan start = {}) noexcept;

    //! A frame starts at frameStartTime (the application's steady clock) and is held for swapInterval refreshes (at least 1): Measure,
    //! then Step.
    AnimationTime Advance(TickCount64 frameStartTime, uint32_t swapInterval) noexcept;

    //! A frame starts at frameStartTime: what that says about the previous frame. For a loop that decides the frame's swap interval
    //! from it (FramePacer does); Step follows.
    FrameMeasurement Measure(TickCount64 frameStartTime) noexcept;

    //! The frame measured last is held for swapInterval refreshes (at least 1): its animation time. The first frame's is the clock's
    //! start; after a restart the step is the swap interval. Without a Measure since the last Step it counts as restarted.
    AnimationTime Step(uint32_t swapInterval) noexcept;

    //! The display's refresh period changed: the clock starts again on it, and the animation time goes on from where it is.
    void SetRefreshPeriod(RefreshPeriod period) noexcept;

    //! Start again with the next frame (after a pause the application knows of): nothing is measured across it.
    void Restart() noexcept;

    //! The longest gap between two frame starts that is measured (see the constructor).
    [[nodiscard]] TimeSpan LongestGap() const noexcept
    {
      return m_longestGap;
    }

    //! Another longest gap, from the next frame on. The clock goes on as it is.
    void SetLongestGap(const TimeSpan longestGap) noexcept
    {
      m_longestGap = longestGap;
    }

    //! The display's clock that many refreshes after the previous frame's display (FrameMeasurement::DisplayTime is the time at 0):
    //! exact, rounded to the nearest tick.
    [[nodiscard]] TimeSpan DisplayTimeAfter(uint32_t refreshes) const noexcept;

    //! The refresh period the clock counts in.
    [[nodiscard]] RefreshPeriod Refresh() const noexcept
    {
      return m_period;
    }

    //! The last frame's animation time.
    [[nodiscard]] AnimationTime Current() const noexcept
    {
      return m_current;
    }
  };
}

#endif
