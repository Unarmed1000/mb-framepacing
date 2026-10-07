// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The pacer of a timer with a wait for a present (sdk/doc/pacer-design.md): TimerPeriodOnlyPacer's grid on the
// clock, a wait before each frame until the display took an earlier one, and a grid that follows the display through the ends of
// the waits that held the loop. What the two pacers share is still a copy in each: it is folded when the third pacer shows what
// all of them share.
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
    if (m_lastAcceptedId > back && (m_lastAcceptedId - back) >= m_oldestWaitableId && (m_lastAcceptedId - back) != m_waitedForId)
    {
      plan.WaitForPresentFrameId = m_lastAcceptedId - back;
      // As long as a few of the frame's own swap intervals: a present that is never shown holds the loop no longer
      const int64_t refreshes = int64_t{m_rule.Settings().PresentWaitSwapIntervals()} * m_rule.SwapInterval();
      plan.WaitForPresentTimeout = TimeDuration(m_rule.Refresh().TimeFor(refreshes));
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
    m_waitedForId = report.FrameId;
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
    // The steps of the grid the previous frame took more than it was given, and the frame before it
    int64_t lost = 0;
    int64_t lostBefore = 0;
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
      lost = slot - m_nextSlot;
      lostBefore = m_lost;
      const TimeSpan cpuWork = m_frameEnded ? m_work : ToTimeSpan32(cpuStartTime - m_startTime).ToTimeSpan();
      const TimeSpan work = m_frameEnded ? m_frameWork.WorkOf(cpuWork, m_rule.Settings().MaxFramesInFlight()) : cpuWork;
      const bool late = lost > 0 || (m_frameEnded && work > period.TimeFor(m_swapInterval));
      change = m_rule.AddFrame(period.TimeFor(slot), work, late, TimeOfSlot(m_nextSlot) - cpuStartTime);
      m_slot = slot;
    }
    // A loss that repeats: the frame before this one took refreshes more than it was given, and so did the one before that. A
    // swap interval the rule just changed is its answer to the losses before it
    const int64_t repeated = change == SwapIntervalChange::Unchanged ? std::min(lost, lostBefore) : 0;
    const auto repeatedLoss = static_cast<uint32_t>(std::min(repeated, int64_t{PacerSettings::MaxSwapInterval}));
    m_lost = change == SwapIntervalChange::Unchanged ? lost : 0;

    m_swapInterval = m_rule.SwapInterval();
    m_nextSlot = m_slot + int64_t{m_swapInterval};
    m_startTime = cpuStartTime;
    m_work = TimeSpan();
    m_frameOpen = true;
    m_frameEnded = false;
    ++m_frameId;
    m_frameWork.AddFrameStart(m_frameId, cpuStartTime);

    // The animation time: the first frame's is where the pacer starts, every other frame's is its swap interval after the one
    // before it. A refresh that was lost is not caught up with. With a loss that repeats the display shows every frame for that
    // much longer, and the step is longer by it: by the fewer of the two frames' losses
    if (!isFirstFrame)
    {
      m_animationTime.Add(m_swapInterval + repeatedLoss, period);
    }
    const TimeSpan animationTime = m_animationTime.ToTimeSpan();
    // How far the animation time is behind the clock: what was lost, less what the step is longer by
    m_refreshesBehindClock += static_cast<uint64_t>(lost) - repeatedLoss;

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

  TimeSpan32 TimerWaitForPresentPacer::CpuBusyAt(const TickCount64 now) const noexcept
  {
    return m_frameOpen ? ToTimeSpan32(now - m_startTime) : TimeSpan32();
  }

  void TimerWaitForPresentPacer::AddPresent(const PresentReport& report) noexcept
  {
    m_lastPresentBlocked = report.Blocked();
    if (report.Accepted)
    {
      m_lastAcceptedId = report.FrameId;
      // A frame presented again after its first present was not taken: this present is one to wait for
      m_oldestWaitableId = std::min(m_oldestWaitableId, report.FrameId);
    }
    else
    {
      // The present will not be shown, and the swap chain it was made for is gone with the presents before it
      m_oldestWaitableId = report.FrameId + 1u;
    }
  }

  void TimerWaitForPresentPacer::AddGpuWork(const GpuWorkReport& report) noexcept
  {
    m_frameWork.AddGpuWork(report, m_rule.Settings().FrameMargin());
  }

  void TimerWaitForPresentPacer::ForgetPresents() noexcept
  {
    m_oldestWaitableId = m_lastAcceptedId + 1u;
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
    m_frameWork.Clear();
    m_hasGrid = false;
    ForgetPresents();
    m_frameOpen = false;
    m_frameEnded = false;
  }
}
