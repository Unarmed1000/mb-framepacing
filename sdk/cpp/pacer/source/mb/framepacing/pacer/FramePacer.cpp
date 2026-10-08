// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The frame pacer of sdk/doc/pacer.md: the refresh clock measures, the swap interval rule decides, and the frame is planned
// from the two.
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/clock/AnimationTime.hpp>
#include <mb/framepacing/pacer/clock/FrameMeasurement.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>

namespace MB::FramePacing::Pacer
{
  FramePacer::FramePacer(const PacerSettings& settings)
    : m_rule(settings)
    , m_clock(settings.Refresh(), settings.FrameWindowLength())
    , m_inFlight(settings.Refresh())
  {
  }

  FrameSchedule FramePacer::BeginFrame(const NanosecondTickCount cpuStartTime) noexcept
  {
    // The work EndFrame was given says whether the previous frame fitted its swap interval. Without an EndFrame the work is the time
    // to this frame's start, which says nothing about that
    const NanosecondTimeSpan knownWork = m_frameOpen && m_frameEnded ? m_work : NanosecondTimeSpan();
    if (m_frameOpen && !m_frameEnded)
    {
      // No EndFrame: the frame is taken as presented now
      m_work = NanosecondTimeDuration(cpuStartTime - m_cpuStartTime).Value();
    }
    // The previous frame: how it did goes to the rule. After a pause (and on the first frame) nothing was measured: the window starts
    // empty, and the swap interval stays. Present feedback changes none of this: it gives statistics and the intended display time
    const FrameMeasurement previous = m_clock.Measure(cpuStartTime, knownWork);
    const RefreshPeriod period = m_rule.Refresh();
    SwapIntervalChange change = SwapIntervalChange::Unchanged;
    if (previous.Restarted)
    {
      m_rule.Clear();
      m_inFlight.Restart();
    }
    else
    {
      // How long before the time the previous frame gave for it this frame began: what shows a loop that nothing holds
      change = m_rule.AddFrame(previous.DisplayTime, m_work, previous.Late, m_nextFrameStartTime - cpuStartTime);
    }

    const uint32_t swapInterval = m_rule.SwapInterval();
    const AnimationTime animation = m_clock.Step(swapInterval);
    m_frameOpen = true;
    m_frameEnded = false;
    m_cpuStartTime = cpuStartTime;
    m_work = NanosecondTimeSpan();

    const bool useFeedback = m_rule.Settings().UsePresentFeedback();
    FrameSchedule schedule;
    schedule.FrameId = m_inFlight.Begin(swapInterval, cpuStartTime);
    if (!useFeedback)
    {
      // Only the frame's id is wanted: no feedback is taken for it, so the frame is not kept and none is missing for it
      m_inFlight.Restart();
    }
    schedule.SwapInterval = swapInterval;
    schedule.AnimationTime = animation.Time;
    schedule.AnimationStep = animation.Step;
    // The frame starts when the previous one is shown, at the refresh the display's clock is on: the frame is aimed its swap interval
    // of refreshes later, in the display clock's exact nanoseconds. With present feedback the aim is counted from a display time instead
    schedule.NextFrameStartTime =
      cpuStartTime + NanosecondTimeSpan(m_clock.DisplayTimeAfter(swapInterval).Nanoseconds() - previous.DisplayTime.Nanoseconds());
    m_nextFrameStartTime = schedule.NextFrameStartTime;
    schedule.IntendedDisplayTime = useFeedback ? m_inFlight.IntendedDisplayTime() : schedule.NextFrameStartTime;
    schedule.TargetFrameTime = NanosecondTimeDuration(period.TimeFor(swapInterval));
    schedule.PreferredFrameTime = NanosecondTimeDuration(period.TimeFor(m_rule.PreferredSwapInterval()));
    schedule.Change = change;
    return schedule;
  }

  NanosecondTimeDuration FramePacer::EndFrame(const NanosecondTickCount presentTime, const NanosecondTimeSpan work) noexcept
  {
    if (!m_frameOpen)
    {
      return {};
    }
    const NanosecondTimeDuration busy = NanosecondTimeDuration(presentTime - m_cpuStartTime);
    m_work = work > NanosecondTimeSpan() ? work : busy.Value();
    m_frameEnded = true;
    m_inFlight.End(presentTime);
    return busy;
  }

  void FramePacer::AddPresentFeedback(const PresentFeedback& feedback) noexcept
  {
    if (m_rule.Settings().UsePresentFeedback())
    {
      m_inFlight.Add(feedback);
    }
  }

  void FramePacer::SetRefreshPeriod(const RefreshPeriod period) noexcept
  {
    if (period != m_rule.Refresh())
    {
      m_rule.SetRefreshPeriod(period);
      m_clock.SetRefreshPeriod(period);
      m_inFlight.SetRefreshPeriod(period);
    }
  }

  void FramePacer::SetSettings(const PacerSettings& settings)
  {
    if (settings != m_rule.Settings())
    {
      m_rule.SetSettings(settings);
      // Nothing is measured across the change: the frame before it was paced by the old settings
      m_clock.SetLongestGap(settings.FrameWindowLength());
      m_clock.SetRefreshPeriod(settings.Refresh());
      m_inFlight.SetRefreshPeriod(settings.Refresh());
    }
  }

  void FramePacer::Reset() noexcept
  {
    m_rule.Reset(m_rule.PreferredSwapInterval());
    m_clock.Restart();
    m_inFlight.Restart();
    m_frameOpen = false;
    m_frameEnded = false;
  }
}
