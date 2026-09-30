// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include <mb/framepacing/pacer/animation/AnimationClock.hpp>
#include <algorithm>
#include <cassert>

namespace MB::FramePacing::Pacer
{
  namespace
  {
    constexpr int64_t OneTickQ32 = RefreshPeriod::OneTickQ32;
  }

  AnimationClock::AnimationClock(const RefreshPeriod period, const int64_t startTicks, const int64_t maxStepRefreshes) noexcept
    : m_period(period)
    , m_maxStepRefreshes(std::max(maxStepRefreshes, int64_t{0}))
    , m_ticks(startTicks)
    , m_current{startTicks, 0, 0}
  {
    assert(maxStepRefreshes >= 0);
  }

  AnimationTime AnimationClock::Advance(const FrameSchedule& schedule) noexcept
  {
    // The intended display times are the previous display plus the swap interval already: the step is the refreshes between them
    const int64_t refreshes = m_hasLast ? m_period.NearestRefreshes(schedule.IntendedDisplayTicks - m_lastTicks) : 0;
    m_lastTicks = schedule.IntendedDisplayTicks;
    m_lastSwapInterval = schedule.SwapInterval;
    m_hasLast = true;
    return Step(refreshes);
  }

  AnimationTime AnimationClock::AdvanceMeasured(const int64_t wakeUpTicks, const int64_t swapInterval) noexcept
  {
    const int64_t interval = std::max(swapInterval, int64_t{1});
    int64_t refreshes = 0;
    if (m_hasLast)
    {
      // How long the previous frame's predecessor stayed on screen, at least the previous frame's swap interval (the jitter can make the
      // wake-up early), then the change from the previous frame's swap interval to this one's
      const int64_t stayed = std::max(m_period.NearestRefreshes(wakeUpTicks - m_lastTicks), m_lastSwapInterval);
      refreshes = stayed - m_lastSwapInterval + interval;
    }
    m_lastTicks = wakeUpTicks;
    m_lastSwapInterval = interval;
    m_hasLast = true;
    return Step(refreshes);
  }

  void AnimationClock::SetRefreshPeriod(const RefreshPeriod period) noexcept
  {
    m_period = period;
  }

  AnimationTime AnimationClock::Step(int64_t refreshes) noexcept
  {
    if (m_maxStepRefreshes > 0)
    {
      refreshes = std::min(refreshes, m_maxStepRefreshes);
    }
    if (m_paused || refreshes < 0)
    {
      refreshes = 0;
    }

    const int64_t before = m_ticks + (static_cast<int64_t>(m_fraction) >= OneTickQ32 / 2 ? 1 : 0);
    // refreshes times the period, added in 2^-32 ticks: the whole ticks and the fraction apart, so a long step cannot overflow
    const int64_t whole = m_period.TicksQ32() >> 32;
    const auto fraction = static_cast<uint64_t>(m_period.TicksQ32()) & 0xFFFF'FFFFu;
    const uint64_t fractionSum = static_cast<uint64_t>(m_fraction) + (static_cast<uint64_t>(refreshes) * fraction);
    m_ticks += (refreshes * whole) + static_cast<int64_t>(fractionSum >> 32u);
    m_fraction = static_cast<uint32_t>(fractionSum & 0xFFFF'FFFFu);
    const int64_t after = m_ticks + (static_cast<int64_t>(m_fraction) >= OneTickQ32 / 2 ? 1 : 0);
    m_current = AnimationTime{after, after - before, refreshes};
    return m_current;
  }
}
