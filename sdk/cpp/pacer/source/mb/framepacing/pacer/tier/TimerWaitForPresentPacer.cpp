// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The pacer of a timer with a wait for a present (sdk/doc/pacer-design.md): TimerPeriodOnlyPacer's grid on the
// clock, a wait before each frame until the display took an earlier one, and a grid that follows the display through the ends of
// the waits that held the loop. What the two pacers share is still a copy in each: it is folded when the third pacer shows what
// all of them share.
#include <mb/framepacing/core/time/NanosecondTimeSpan32.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <mb/framepacing/pacer/tier/TimerWaitForPresentPacer.hpp>
#include <algorithm>
#include "../detail/MarkerValue.hpp"

namespace MB::FramePacing::Pacer
{
  namespace
  {
    //! A wait for a present held the loop when it took at least this share of a refresh period: a period divided by it
    constexpr int64_t BlockedDivisor = 8;
    //! How much of the way the grid is moved towards the end of such a wait: the distance divided by it
    constexpr int64_t FollowDivisor = 4;
  }

  TimerWaitForPresentPacer::TimerWaitForPresentPacer(const PacerSettings& settings)
    : m_rule(settings)
  {
  }

  bool TimerWaitForPresentPacer::StartsAgainAt(const NanosecondTickCount time) const noexcept
  {
    if (!m_hasGrid)
    {
      return true;
    }
    // A pause, or a clock that went back: nothing is measured across it
    const NanosecondTimeSpan gap = time - m_startTime;
    const NanosecondTimeSpan reach = std::max(m_rule.Settings().FrameWindowLength(), m_rule.Refresh().TimeFor(int64_t{2} * m_swapInterval));
    return gap < NanosecondTimeSpan() || gap > reach;
  }

  int64_t TimerWaitForPresentPacer::Reserve() const noexcept
  {
    // The frames made ahead of the display: with the aim of smoothness, and at one refresh per frame only (at more the display
    // takes a frame before the next one is made, and nothing can wait)
    const PacerSettings& settings = m_rule.Settings();
    return settings.Aim() == PacerAim::Smoothness && m_rule.SwapInterval() == 1 ? int64_t{settings.ReserveFrames()} : 0;
  }

  NanosecondTickCount TimerWaitForPresentPacer::DueTime(const int64_t slot) const noexcept
  {
    // A frame starts the reserve's refreshes before its step of the grid
    return TimeOfSlot(slot - Reserve());
  }

  int64_t TimerWaitForPresentPacer::SmoothSlotFor(const NanosecondTickCount time) const noexcept
  {
    const RefreshPeriod period = m_rule.Refresh();
    // How late the loop is for the step the frame is due at, in whole steps
    const int64_t startLate = period.NearestRefreshes(time - DueTime(m_nextSlot));
    // How late the frame before it was with its present: the steps it was behind when it started, and the whole periods its
    // present came after the time its swap interval gave it. That is one more than its start shows, as the late present took the
    // place of the next frame's in its step
    int64_t presentLate = 0;
    if (m_hasPresentTime)
    {
      presentLate = m_behind + std::max(period.FloorRefreshes(m_presentTime - m_startTime) - (int64_t{m_swapInterval} - 1), int64_t{0});
    }
    // The reserve covers that many steps: those the frames are made up for, back to back. What is beyond it the display has
    // shown a frame again for, and those steps are given up
    return m_nextSlot + std::max(std::max(startLate, presentLate) - Reserve(), int64_t{0});
  }

  int64_t TimerWaitForPresentPacer::SlotFor(const NanosecondTickCount time) const noexcept
  {
    if (m_rule.Settings().Aim() == PacerAim::Smoothness)
    {
      return SmoothSlotFor(time);
    }
    // The step nearest to the time, and never one before the step the frame is due at: early is waited for, not taken
    return std::max(m_nextSlot, m_rule.Refresh().NearestRefreshes(time - m_origin));
  }

  NanosecondTickCount TimerWaitForPresentPacer::TimeOfSlot(const int64_t slot) const noexcept
  {
    return m_origin + m_rule.Refresh().TimeFor(slot);
  }

  FrameStartPlan TimerWaitForPresentPacer::PlanFrame(const NanosecondTickCount now) const noexcept
  {
    FrameStartPlan plan;
    // The present to wait for: the one WaitingPresents back from the frame that is about to be made. While the waits run out
    // nothing is waited for: the plan asks after a present that many frames older, which has had the time a wait would give
    const bool stopped = PresentWaitsStopped();
    const uint64_t back = (uint64_t{m_rule.Settings().WaitingPresents()} - 1u) + (stopped ? m_rule.Settings().PresentWaitSwapIntervals() : 0u);
    if (!m_waitReported && m_lastAcceptedId > back && (m_lastAcceptedId - back) >= m_oldestWaitableId && (m_lastAcceptedId - back) != m_waitedForId &&
        (!stopped || (m_lastAcceptedId - back) >= m_runOutFromId))
    {
      plan.WaitForPresentFrameId = m_lastAcceptedId - back;
      // As long as a few of the frame's own swap intervals: a present that is never shown holds the loop no longer
      const int64_t refreshes = int64_t{m_rule.Settings().PresentWaitSwapIntervals()} * m_rule.SwapInterval();
      plan.WaitForPresentTimeout = stopped ? NanosecondTimeDuration() : NanosecondTimeDuration(m_rule.Refresh().TimeFor(refreshes));
    }
    if (!StartsAgainAt(now))
    {
      const NanosecondTickCount due = DueTime(SlotFor(now));
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
    m_waitReported = true;
    const RefreshPeriod period = m_rule.Refresh();
    const bool heldTheLoop = report.Blocked().Nanoseconds() >= (period.ToNanosecondTimeSpan().Nanoseconds() / BlockedDivisor);
    if (!report.Shown)
    {
      ++m_presentWaitTimeouts;
      m_runOutFromId = m_waitsRunOut == 0 ? report.FrameId : m_runOutFromId;
      m_waitsRunOut = std::min(m_waitsRunOut + 1u, WaitsRunOutToStop);
      // The frame that starts after a wait that held the loop until it ran out starts late because the pacer asked for the wait
      m_waitRanOut = m_waitRanOut || heldTheLoop;
      return;
    }
    m_waitsRunOut = 0;
    // A wait that returned at once says nothing: the present was shown some time before. One that held the loop ended when the
    // display took a frame: the grid's step nearest to its end is moved a quarter of the way towards it
    if (!m_hasGrid || StartsAgainAt(report.EndTime) || !heldTheLoop)
    {
      return;
    }
    const int64_t nearest = period.NearestRefreshes(report.EndTime - m_origin);
    const int64_t offNanoseconds = (report.EndTime - TimeOfSlot(nearest)).Nanoseconds();
    m_origin = m_origin + NanosecondTimeSpan(offNanoseconds / FollowDivisor);
  }

  FrameSchedule TimerWaitForPresentPacer::BeginFrame(const NanosecondTickCount cpuStartTime) noexcept
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
      m_behind = 0;
      m_hasGrid = true;
    }
    else if (m_waitRanOut)
    {
      // The pacer's own wait held this frame's start until it ran out: the display is not taking the window's frames, so when
      // the previous frame was shown, or whether, is not known, and this start is late by the pacer's doing. Nothing is
      // judged and the frame window stays as it is; the grid goes on from this frame
      m_origin = cpuStartTime;
      m_slot = 0;
      m_behind = 0;
    }
    else
    {
      // The previous frame: the steps of the grid from its start to this one against its swap interval, and its work against its
      // swap interval's time. Without an EndFrame its work is not known, and the time to this start says nothing about it
      const int64_t slot = SlotFor(cpuStartTime);
      lost = slot - m_nextSlot;
      lostBefore = m_lost;
      const NanosecondTimeSpan cpuWork = m_frameEnded ? m_work : MarkerValue::Duration(cpuStartTime - m_startTime).ToNanosecondTimeSpan();
      const NanosecondTimeSpan work = m_frameEnded ? m_frameWork.WorkOf(cpuWork, m_rule.Settings().MaxFramesInFlight()) : cpuWork;
      const bool late = lost > 0 || (m_frameEnded && work > period.TimeFor(m_swapInterval));
      change = m_rule.AddFrame(period.TimeFor(slot), work, late, DueTime(m_nextSlot) - cpuStartTime);
      // The steps the frame is behind the one it takes: within the reserve, and made up for by the frames after it
      m_behind = std::max(period.NearestRefreshes(cpuStartTime - DueTime(slot)), int64_t{0});
      m_slot = slot;
    }
    // A loss that repeats: the frame before this one took refreshes more than it was given, and so did the one before that. A
    // swap interval the rule just changed is its answer to the losses before it
    const int64_t repeated = change == SwapIntervalChange::Unchanged ? std::min(lost, lostBefore) : 0;
    const auto repeatedLoss = static_cast<uint32_t>(std::min(repeated, int64_t{PacerSettings::MaxSwapInterval}));
    m_lost = change == SwapIntervalChange::Unchanged ? lost : 0;

    m_waitRanOut = false;
    m_waitReported = false;
    m_swapInterval = m_rule.SwapInterval();
    m_nextSlot = m_slot + int64_t{m_swapInterval};
    m_startTime = cpuStartTime;
    m_hasPresentTime = false;
    m_work = NanosecondTimeSpan();
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
    const NanosecondTimeSpan animationTime = m_animationTime.ToNanosecondTimeSpan();
    // How far the animation time is behind the clock: what was lost, less what the step is longer by
    m_refreshesBehindClock += static_cast<uint64_t>(lost) - repeatedLoss;

    FrameSchedule schedule;
    schedule.FrameId = m_frameId;
    schedule.SwapInterval = m_swapInterval;
    schedule.AnimationTime = animationTime;
    schedule.AnimationStep = NanosecondTimeSpan(animationTime.Nanoseconds() - m_lastAnimationTime.Nanoseconds());
    // The step the next frame is due at is where this frame is expected to leave the grid's refresh for the screen: without a
    // display to ask, it is the pacer's aim for the frame
    schedule.NextFrameStartTime = DueTime(m_nextSlot);
    schedule.IntendedDisplayTime = TimeOfSlot(m_nextSlot);
    schedule.TargetFrameTime = MarkerValue::FrameTime(period.TimeFor(m_swapInterval));
    schedule.PreferredFrameTime = MarkerValue::FrameTime(period.TimeFor(m_rule.PreferredSwapInterval()));
    schedule.Change = change;
    m_lastAnimationTime = animationTime;
    return schedule;
  }

  PresentPlan TimerWaitForPresentPacer::EndFrame(const NanosecondTickCount workDoneTime) noexcept
  {
    PresentPlan plan;
    if (!m_frameOpen)
    {
      return plan;
    }
    const NanosecondTimeSpan32 busy = MarkerValue::Duration(workDoneTime - m_startTime);
    m_work = busy.ToNanosecondTimeSpan();
    m_frameEnded = true;
    plan.FrameId = m_frameId;
    plan.CpuBusy = busy;
    if (m_swapInterval > 1)
    {
      // The present holds a frame for one refresh. A frame of more is held by the loop: presented in the period before the step
      // the next frame is due at, the margin into it
      const NanosecondTickCount presentTime = TimeOfSlot(m_nextSlot - 1) + m_rule.Settings().FrameMargin();
      if (presentTime > workDoneTime)
      {
        plan.PresentTime = presentTime;
      }
    }
    // When the present is made, as far as it is known here: AddPresent says when it was
    m_presentTime = plan.WaitsForPresentTime() ? plan.PresentTime : workDoneTime;
    m_hasPresentTime = true;
    return plan;
  }

  NanosecondTimeSpan32 TimerWaitForPresentPacer::CpuBusyAt(const NanosecondTickCount now) const noexcept
  {
    return m_frameOpen ? MarkerValue::Duration(now - m_startTime) : NanosecondTimeSpan32();
  }

  void TimerWaitForPresentPacer::AddPresent(const PresentReport& report) noexcept
  {
    m_lastPresentBlocked = report.Blocked();
    if (m_frameEnded && report.FrameId == m_frameId)
    {
      m_presentTime = report.CallTime;
    }
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
    // And with them the waits that ran out: a new swap chain's presents are waited for again
    m_waitsRunOut = 0;
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
    m_hasPresentTime = false;
  }
}
