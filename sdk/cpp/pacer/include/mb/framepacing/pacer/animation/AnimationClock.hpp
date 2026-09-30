#ifndef MB_FRAMEPACING_PACER_ANIMATION_ANIMATIONCLOCK_HPP
#define MB_FRAMEPACING_PACER_ANIMATION_ANIMATIONCLOCK_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/animation/AnimationTime.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! The animation timer aligned to display time slots: the vsync timer of mb-framepacing-explained (sdk/doc/pacer.md). On a display
  //! with a fixed refresh every frame is shown a whole number of refreshes after the previous one, and animates for its predicted display
  //! time: the previous frame's display plus its swap interval. So the animation time advances in whole refreshes: by the refreshes
  //! between the pacer's intended display times (Advance), or, without a pacer, by the time since the previous frame rounded to whole
  //! refreshes, corrected for a change of swap interval (AdvanceMeasured). A late frame shows a moment already past, and the next frame
  //! catches up exactly. The steps add up in 2^-32 ticks, so the animation time never drifts from the refreshes, and is never pulled back
  //! in whole refreshes. No allocation.
  class AnimationClock
  {
    RefreshPeriod m_period;
    int64_t m_maxStepRefreshes{0};
    // The animation time: m_ticks + m_fraction / 2^32
    int64_t m_ticks{0};
    uint32_t m_fraction{0};
    int64_t m_lastTicks{0};
    int64_t m_lastSwapInterval{0};
    bool m_hasLast{false};
    bool m_paused{false};
    AnimationTime m_current;

  public:
    //! startTicks: the animation time of the first frame. maxStepRefreshes: the longest step (0 = no limit; asserted not negative, and
    //! taken as 0 without asserts), for a process that was suspended or a debugger that stopped it.
    explicit AnimationClock(RefreshPeriod period, int64_t startTicks = 0, int64_t maxStepRefreshes = 0) noexcept;

    //! With the pacer: the step is the whole refreshes from the previous frame's intended display time to this one's.
    AnimationTime Advance(const FrameSchedule& schedule) noexcept;

    //! Without a pacer, in a loop paced by vsync (it waits in Present, so every frame starts when the previous one is shown): the time
    //! since the previous frame's wake-up is how long the frame before stayed on screen, rounded to whole refreshes and at least that
    //! frame's swap interval. The step is that, minus the previous frame's swap interval, plus this frame's (swapInterval): each frame
    //! animates for the previous frame's display plus its own swap interval. With one swap interval the step is the rounded time.
    //! Rounding removes the wake-ups' jitter while it stays under half a refresh.
    AnimationTime AdvanceMeasured(int64_t wakeUpTicks, int64_t swapInterval) noexcept;

    //! While paused the animation time holds; after Resume it goes on from there, without a jump.
    void Pause() noexcept
    {
      m_paused = true;
    }

    void Resume() noexcept
    {
      m_paused = false;
    }

    [[nodiscard]] bool IsPaused() const noexcept
    {
      return m_paused;
    }

    //! The display's refresh period changed; the animation time goes on from where it is.
    void SetRefreshPeriod(RefreshPeriod period) noexcept;

    //! The last frame's animation time.
    [[nodiscard]] AnimationTime Current() const noexcept
    {
      return m_current;
    }

  private:
    AnimationTime Step(int64_t refreshes) noexcept;
  };
}

#endif
