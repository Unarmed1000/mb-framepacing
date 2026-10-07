// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The pacer of a timer with a wait for a present (sdk/doc/pacer-design.md): TimerPeriodOnlyPacer's grid on the
// clock, a wait before each frame until the display took an earlier one, and a grid that follows the display through the ends of
// the waits that held the loop.
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <mb/framepacing/pacer/tier/TimerWaitForPresentPacer.hpp>
#include <algorithm>
#include <limits>

namespace MB::FramePacing::Pacer
{
  namespace
  {
    //! A wait for a present held the loop when it took at least this share of a refresh period: a period divided by it
    constexpr int64_t BlockedDivisor = 8;
    //! How much of the way the grid is moved towards the end of such a wait: the distance divided by it
    constexpr int64_t FollowDivisor = 4;

    //! A span that the marker's 32-bit fields hold: zero (unknown) when it is negative or too long for them.
    TimeSpan32 ToTimeSpan32(const TimeSpan span) noexcept
    {
      const int64_t ticks = span.Ticks();
      return ticks >= 0 && ticks <= int64_t{std::numeric_limits<uint32_t>::max()} ? TimeSpan32(static_cast<uint32_t>(ticks)) : TimeSpan32();
    }
  }

  TimerWaitForPresentPacer::TimerWaitForPresentPacer(const PacerSettings& settings)
    : m_rule(settings)
  {
  }

  bool TimerWaitForPresentPacer::StartsAgainAt(const TickCount64 time) const noexcept
  {
    if (!m_hasGrid)
    {
      return true;
    }
    // A pause, or a clock that went back: nothing is measured across it
    const TimeSpan gap = time - m_startTime;
    const TimeSpan reach = std::max(m_rule.Settings().FrameWindowLength(), m_rule.Refresh().TimeFor(int64_t{2} * m_swapInterval));
    return gap < TimeSpan() || gap > reach;
  }

  int64_t TimerWaitForPresentPacer::SlotFor(const TickCount64 time) const noexcept
  {
    // The step nearest to the time, and never one before the step the frame is due at: early is waited for, not taken
    return std::max(m_nextSlot, m_rule.Refresh().NearestRefreshes(time - m_origin));
  }

  TickCount64 TimerWaitForPresentPacer::TimeOfSlot(const int64_t slot) const noexcept
  {
    return m_origin + m_rule.Refresh().TimeFor(slot);
  }

  FrameStartPlan TimerWaitForPresentPacer::PlanFrame(const TickCount64 now) const noexcept
  {
    FrameStartPlan plan;
    // The present to wait for: the one WaitingPresents back from the frame that is about to be made
    const uint64_t back = uint64_t{m_rule.Settings().WaitingPresents()} - 1u;
    if (m_lastAcceptedId > back && (m_lastAcceptedId - back) >= m_oldestWaitableId)
    {
      plan.WaitForPresentFrameId = m_lastAcceptedId - back;
      plan.WaitForPresentTimeout = TimeDuration(m_rule.Settings().PresentWaitTimeout());
    }
    if (!StartsAgainAt(now))
    {
      const TickCount64 due = TimeOfSlot(SlotFor(now));
      if (due > now)
      {
        plan.StartTime = due;
      }
    }
    return plan;
  }

  void TimerWaitForPresentPacer::AddPresentWait(const PresentWaitReport& report) noexcept
  {
    if (!report.Shown)
    {
      ++m_presentWaitTimeouts;
      return;
    }
    // A wait that returned at once says nothing: the present was shown some time before. One that held the loop ended when the
    // display took a frame: the grid's step nearest to its end is moved a quarter of the way towards it
    const RefreshPeriod period = m_rule.Refresh();
    if (!m_hasGrid || StartsAgainAt(report.EndTime) || report.Blocked().Ticks() < (period.ToTimeSpan().Ticks() / BlockedDivisor))
    {
      return;
    }
    const int64_t nearest = period.NearestRefreshes(report.EndTime - m_origin);
    const int64_t offTicks = (report.EndTime - TimeOfSlot(nearest)).Ticks();
    m_origin = m_origin + TimeSpan(offTicks / FollowDivisor);
  }

  FrameSchedule TimerWaitForPresentPacer::BeginFrame(const TickCount64 cpuStartTime) noexcept
  {
    const RefreshPeriod period = m_rule.Refresh();
    SwapIntervalChange change = SwapIntervalChange::Unchanged;
    const bool isFirstFrame = m_frameId == 0;
    if (StartsAgainAt(cpuStartTime))
    {
      // The grid starts at this frame: nothing was measured, the frame window starts empty and the swap interval stays
      m_rule.Clear();
      m_origin = cpuStartTime;
      m_slot = 0;
      m_hasGrid = true;
    }
    else
    {
      // The previous frame: the steps of the grid from its start to this one against its swap interval, and its work against its
      // swap interval's time. Without an EndFrame its work is not known, and the time to this start says nothing about it
      const int64_t slot = SlotFor(cpuStartTime);
      const int64_t lost = (slot - m_slot) - int64_t{m_swapInterval};
      const TimeSpan knownWork = m_frameEnded ? m_work : TimeSpan();
      const bool late = lost > 0 || knownWork > period.TimeFor(m_swapInterval);
      const TimeSpan work = m_frameEnded ? m_work : ToTimeSpan32(cpuStartTime - m_startTime).ToTimeSpan();
      change = m_rule.AddFrame(period.TimeFor(slot), work, late, TimeOfSlot(m_nextSlot) - cpuStartTime);
      m_refreshesBehindClock += static_cast<uint64_t>(lost);
      m_slot = slot;
    }

    m_swapInterval = m_rule.SwapInterval();
    m_nextSlot = m_slot + int64_t{m_swapInterval};
    m_startTime = cpuStartTime;
    m_work = TimeSpan();
    m_frameOpen = true;
    m_frameEnded = false;
    ++m_frameId;

    // The animation time: the first frame's is where the pacer starts, every other frame's is its swap interval after the one
    // before it, whatever the grid lost in between
    if (!isFirstFrame)
    {
      m_animationTime.Add(m_swapInterval, period);
    }
    const TimeSpan animationTime = m_animationTime.ToTimeSpan();

    FrameSchedule schedule;
    schedule.FrameId = m_frameId;
    schedule.SwapInterval = m_swapInterval;
    schedule.AnimationTime = animationTime;
    schedule.AnimationStep = TimeSpan(animationTime.Ticks() - m_lastAnimationTime.Ticks());
    // The step the next frame is due at is where this frame is expected to leave the grid's refresh for the screen: without a
    // display to ask, it is the pacer's aim for the frame
    schedule.NextFrameStartTime = TimeOfSlot(m_nextSlot);
    schedule.IntendedDisplayTime = schedule.NextFrameStartTime;
    schedule.TargetFrameTime = ToTimeSpan32(period.TimeFor(m_swapInterval));
    schedule.PreferredFrameTime = ToTimeSpan32(period.TimeFor(m_rule.PreferredSwapInterval()));
    schedule.Change = change;
    m_lastAnimationTime = animationTime;
    return schedule;
  }

  PresentPlan TimerWaitForPresentPacer::EndFrame(const TickCount64 workDoneTime) noexcept
  {
    PresentPlan plan;
    if (!m_frameOpen)
    {
      return plan;
    }
    const TimeSpan32 busy = ToTimeSpan32(workDoneTime - m_startTime);
    m_work = busy.ToTimeSpan();
    m_frameEnded = true;
    plan.FrameId = m_frameId;
    plan.CpuBusy = busy;
    if (m_swapInterval > 1)
    {
      // The present holds a frame for one refresh. A frame of more is held by the loop: presented in the period before the step
      // the next frame is due at, the margin into it
      const TickCount64 presentTime = TimeOfSlot(m_nextSlot - 1) + m_rule.Settings().FrameMargin();
      if (presentTime > workDoneTime)
      {
        plan.PresentTime = presentTime;
      }
    }
    return plan;
  }

  void TimerWaitForPresentPacer::AddPresent(const PresentReport& report) noexcept
  {
    m_lastPresentBlocked = report.Blocked();
    if (report.Accepted)
    {
      m_lastAcceptedId = report.FrameId;
    }
    else
    {
      // The present will not be shown, and the swap chain it was made for is gone with the presents before it
      m_oldestWaitableId = report.FrameId + 1u;
    }
  }

  void TimerWaitForPresentPacer::SetRefreshPeriod(const RefreshPeriod period) noexcept
  {
    if (period != m_rule.Refresh())
    {
      m_rule.SetRefreshPeriod(period);
      m_hasGrid = false;
    }
  }

  void TimerWaitForPresentPacer::SetSettings(const PacerSettings& settings)
  {
    if (settings != m_rule.Settings())
    {
      m_rule.SetSettings(settings);
      m_hasGrid = false;
    }
  }

  void TimerWaitForPresentPacer::Reset() noexcept
  {
    m_rule.Reset(m_rule.PreferredSwapInterval());
    m_hasGrid = false;
    m_oldestWaitableId = m_frameId + 1u;
    m_frameOpen = false;
    m_frameEnded = false;
  }
}
