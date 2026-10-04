// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The pacer's refresh clock of sdk/doc/pacer.md: frame starts measured on the CPU's clock, counted in whole refreshes on
// the display's.
#include <mb/framepacing/pacer/clock/PacerRefreshClock.hpp>
#include <algorithm>
#include <utility>

namespace MB::FramePacing::Pacer
{
  PacerRefreshClock::PacerRefreshClock(const RefreshPeriod period, const TimeSpan longestGap, const TimeSpan start) noexcept
    : m_period(period)
    , m_longestGap(longestGap)
    , m_animationTime(start)
    , m_current{start, TimeSpan(), 0}
  {
  }

  AnimationTime PacerRefreshClock::Advance(const TickCount64 frameStartTime, const uint32_t swapInterval) noexcept
  {
    static_cast<void>(Measure(frameStartTime));
    return Step(swapInterval);
  }

  FrameMeasurement PacerRefreshClock::Measure(const TickCount64 frameStartTime, const TimeSpan work) noexcept
  {
    m_measurement = FrameMeasurement{};
    // What rounding the frames that worked over their time left over: kept only from one such frame to the next
    const TimeSpan carried = std::exchange(m_carried, TimeSpan());
    if (m_hasLast)
    {
      // The frame starts when the previous one is shown, so the time between two starts is the refreshes between two displays: the
      // previous frame was aimed its swap interval after the frame before it, and can not have been shown sooner
      const TimeSpan gap = frameStartTime - m_lastStartTime;
      const TimeSpan reach = std::max(m_longestGap, m_period.TimeFor(int64_t{2} * m_lastSwapInterval));
      if (gap >= TimeSpan() && gap <= reach)
      {
        // A frame that worked longer than its swap interval's time did not make it, and its loop is not held by vsync (a swap chain
        // with room takes the present at once): the next frame starts when the work is done, 1.4 refreshes later, say, and not on a
        // refresh. Each rounded on its own, such frames would all count one refresh; so their time is taken as real time, and what
        // rounding leaves is carried to the next one. Frames that fit are rounded each on its own: no grid of the clock's own
        const TimeSpan frameTime = m_period.TimeFor(m_lastSwapInterval);
        const bool overTime = work > frameTime;
        const TimeSpan counted(gap.Ticks() + (overTime ? carried.Ticks() : 0));
        const auto refreshes = static_cast<uint32_t>(std::max(m_period.NearestRefreshes(counted), int64_t{m_lastSwapInterval}));
        if (overTime)
        {
          const int64_t one = m_period.ToTimeSpan().Ticks();
          m_carried = TimeSpan(std::clamp(counted.Ticks() - m_period.TimeFor(refreshes).Ticks(), -one, one));
        }
        m_displayTime.Add(refreshes, m_period);
        m_measurement.Restarted = false;
        m_measurement.Refreshes = refreshes;
        m_measurement.Late = overTime || refreshes > m_lastSwapInterval;
      }
    }
    m_measurement.DisplayTime = m_displayTime.ToTimeSpan();
    m_lastStartTime = frameStartTime;
    m_hasLast = true;
    return m_measurement;
  }

  AnimationTime PacerRefreshClock::Step(const uint32_t swapInterval) noexcept
  {
    const uint32_t interval = std::max(swapInterval, 1u);
    // The previous frame's display plus this frame's swap interval. The previous frame animated for the display before it plus its own
    // swap interval, so the step is the refreshes measured, minus its swap interval, plus this frame's
    uint32_t refreshes = interval;
    if (!m_stepped)
    {
      refreshes = 0;
    }
    else if (!m_measurement.Restarted)
    {
      refreshes = m_measurement.Refreshes - m_lastSwapInterval + interval;
    }
    m_stepped = true;
    m_lastSwapInterval = interval;
    m_measurement = FrameMeasurement{};

    const TimeSpan before = m_animationTime.ToTimeSpan();
    m_animationTime.Add(refreshes, m_period);
    const TimeSpan after = m_animationTime.ToTimeSpan();
    m_current = AnimationTime{after, TimeSpan(after.Ticks() - before.Ticks()), refreshes};
    return m_current;
  }

  void PacerRefreshClock::SetRefreshPeriod(const RefreshPeriod period) noexcept
  {
    m_period = period;
    Restart();
  }

  void PacerRefreshClock::Restart() noexcept
  {
    m_hasLast = false;
    m_measurement = FrameMeasurement{};
  }

  TimeSpan PacerRefreshClock::DisplayTimeAfter(const uint32_t refreshes) const noexcept
  {
    return m_displayTime.After(refreshes, m_period).ToTimeSpan();
  }
}
