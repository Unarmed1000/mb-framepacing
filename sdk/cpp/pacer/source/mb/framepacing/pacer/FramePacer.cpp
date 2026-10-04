// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The frame pacer of sdk/doc/pacer.md: the refresh clock measures, the swap interval rule decides, and the frame is planned
// from the two.
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/clock/AnimationTime.hpp>
#include <mb/framepacing/pacer/clock/FrameMeasurement.hpp>
#include <mb/framepacing/pacer/frame/MeasuredFrame.hpp>
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
    , m_inFlight(settings.Refresh())
  {
  }

  FrameSchedule FramePacer::BeginFrame(const TickCount64 cpuStartTime) noexcept
  {
    // The work EndFrame was given says whether the previous frame fitted its swap interval. Without an EndFrame the work is the time
    // to this frame's start, which says nothing about that
    const TimeSpan knownWork = m_frameOpen && m_frameEnded ? m_work : TimeSpan();
    if (m_frameOpen && !m_frameEnded)
    {
      // No EndFrame: the frame is taken as presented now
      m_work = ToTimeSpan32(cpuStartTime - m_cpuStartTime).ToTimeSpan();
      m_inFlight.End(m_work, m_cpuStartTime, false);
    }
    // The previous frame: how it did goes to the rule. After a pause (and on the first frame) nothing was measured: the window starts
    // empty, and the swap interval stays. With present feedback the frames are measured by their display times, as those come in:
    // the frame start only says whether this is a pause
    const bool useFeedback = m_rule.Settings().UsePresentFeedback();
    const FrameMeasurement previous =
      useFeedback ? m_clock.MeasureLate(cpuStartTime, m_inFlight.TakeLateRefreshes()) : m_clock.Measure(cpuStartTime, knownWork);
    const RefreshPeriod period = m_rule.Refresh();
    SwapIntervalChange change = SwapIntervalChange::Unchanged;
    if (previous.Restarted)
    {
      m_rule.Clear();
      m_inFlight.Restart();
    }
    else if (useFeedback)
    {
      change = AddMeasuredFrames();
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
    schedule.FrameId = m_inFlight.Begin(swapInterval, animation.Time, cpuStartTime);
    schedule.SwapInterval = swapInterval;
    schedule.AnimationTime = animation.Time;
    schedule.AnimationStep = animation.Step;
    // The frame starts when the previous one is shown, at the refresh the display's clock is on: the frame is aimed its swap interval
    // of refreshes later, in the display clock's exact ticks. With present feedback the aim is counted from a display time instead
    schedule.NextFrameStartTime = cpuStartTime + TimeSpan(m_clock.DisplayTimeAfter(swapInterval).Ticks() - previous.DisplayTime.Ticks());
    schedule.IntendedDisplayTime = useFeedback ? m_inFlight.IntendedDisplayTime() : schedule.NextFrameStartTime;
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
    m_inFlight.End(m_work, presentTime);
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

  SwapIntervalChange FramePacer::AddMeasuredFrames() noexcept
  {
    // The frames their feedback has decided since the last frame began, oldest first. A change of swap interval empties the rule's
    // window: the frames still in flight were paced at the old one, and are left out
    SwapIntervalChange change = SwapIntervalChange::Unchanged;
    MeasuredFrame frame;
    while (m_inFlight.TakeMeasured(frame))
    {
      if (frame.FrameId >= m_firstRuleFrameId)
      {
        change = m_rule.AddFrame(frame.AnimationTime, frame.Work, frame.Late);
        if (change != SwapIntervalChange::Unchanged)
        {
          m_firstRuleFrameId = m_inFlight.NewestFrameId() + 1u;
        }
      }
    }
    return change;
  }
}
