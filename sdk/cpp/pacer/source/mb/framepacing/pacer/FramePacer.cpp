// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The frame pacer of sdk/doc/pacer.md: the animation clock measures, the swap interval rule decides, and the frame is planned
// from the two.
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/animation/AnimationTime.hpp>
#include <mb/framepacing/pacer/animation/FrameMeasurement.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <limits>

namespace MB::FramePacing::Pacer
{
  namespace
  {
    //! A span that the marker's 32-bit fields hold: zero (unknown) when it is negative or too long for them.
    TimeSpan32 ToTimeSpan32(const TimeSpan span) noexcept
    {
      const int64_t ticks = span.Ticks();
      return ticks >= 0 && ticks <= int64_t{std::numeric_limits<uint32_t>::max()} ? TimeSpan32(static_cast<uint32_t>(ticks)) : TimeSpan32();
    }
  }

  FramePacer::FramePacer(const PacerSettings& settings)
    : m_rule(settings)
    , m_clock(settings.Refresh(), settings.FrameWindowLength())
  {
  }

  FrameSchedule FramePacer::BeginFrame(const TickCount64 cpuStartTime) noexcept
  {
    if (m_frameOpen && !m_frameEnded)
    {
      // No EndFrame: the frame is taken as presented now
      m_work = ToTimeSpan32(cpuStartTime - m_cpuStartTime).ToTimeSpan();
    }
    // The previous frame: how it did goes to the rule. After a pause (and on the first frame) nothing was measured: the window starts
    // empty, and the swap interval stays
    const FrameMeasurement previous = m_clock.Measure(cpuStartTime);
    const RefreshPeriod period = m_rule.Refresh();
    SwapIntervalChange change = SwapIntervalChange::None;
    if (previous.Restarted)
    {
      m_rule.Clear();
    }
    else
    {
      change = m_rule.AddFrame(previous.DisplayTime, m_work, previous.Late);
    }

    const uint32_t swapInterval = m_rule.SwapInterval();
    const AnimationTime animation = m_clock.Step(swapInterval);
    m_frameOpen = true;
    m_frameEnded = false;
    m_cpuStartTime = cpuStartTime;
    m_work = TimeSpan();

    FrameSchedule schedule;
    schedule.SwapInterval = swapInterval;
    schedule.AnimationTime = animation.Time;
    schedule.AnimationStep = animation.Step;
    // The frame starts when the previous one is shown, at the refresh the display's clock is on: the frame is aimed its swap interval
    // of refreshes later, in the display clock's exact ticks
    schedule.IntendedDisplayTime = cpuStartTime + TimeSpan(m_clock.DisplayTimeAfter(swapInterval).Ticks() - previous.DisplayTime.Ticks());
    schedule.TargetFrameTime = ToTimeSpan32(period.TimeFor(swapInterval));
    schedule.PreferredFrameTime = ToTimeSpan32(period.TimeFor(m_rule.PreferredSwapInterval()));
    schedule.Change = change;
    return schedule;
  }

  TimeSpan32 FramePacer::EndFrame(const TickCount64 presentTime, const TimeSpan work) noexcept
  {
    if (!m_frameOpen)
    {
      return {};
    }
    const TimeSpan32 busy = ToTimeSpan32(presentTime - m_cpuStartTime);
    m_work = work > TimeSpan() ? work : busy.ToTimeSpan();
    m_frameEnded = true;
    return busy;
  }

  void FramePacer::SetRefreshPeriod(const RefreshPeriod period) noexcept
  {
    if (period != m_rule.Refresh())
    {
      m_rule.SetRefreshPeriod(period);
      m_clock.SetRefreshPeriod(period);
    }
  }

  void FramePacer::Reset() noexcept
  {
    m_rule.Reset(m_rule.PreferredSwapInterval());
    m_clock.Restart();
    m_frameOpen = false;
    m_frameEnded = false;
  }
}
