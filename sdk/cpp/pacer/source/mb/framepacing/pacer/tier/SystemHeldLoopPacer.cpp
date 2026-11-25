// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/pacer/tier/SystemHeldLoopPacer.hpp>
#include <algorithm>

namespace MB::FramePacing::Pacer
{
  SystemHeldLoopPacer::SystemHeldLoopPacer(const PacerSettings& settings)
    : m_rule(settings)
  {
  }

  void SystemHeldLoopPacer::SetPresentSwapIntervals(const uint32_t minSwapInterval, const uint32_t maxSwapInterval) noexcept
  {
    m_rule.SetMinSwapInterval(minSwapInterval);
    m_presentHoldsUpTo = std::max(maxSwapInterval, 1u);
  }

  bool SystemHeldLoopPacer::IsLimited() const noexcept
  {
    // The pacer limits the frame time where the system does not hold the loop, and where the swap interval is longer than the
    // present holds a frame for
    return m_notHolding || m_rule.SwapInterval() > m_presentHoldsUpTo;
  }

  int64_t SystemHeldLoopPacer::Capacity() const noexcept
  {
    // What the swap chain can queue: its images and the frames in flight, each a swap interval of display time. More frames
    // than that can not start ahead of the display while the system holds the loop
    const PacerSettings& settings = m_rule.Settings();
    const uint32_t images = settings.SwapChainImages() > 0 ? settings.SwapChainImages() : UnknownSwapChainImages;
    const int64_t frames = int64_t{images} + int64_t{settings.MaxFramesInFlight()};
    return frames * m_rule.Refresh().TimeFor(m_rule.SwapInterval()).Nanoseconds();
  }

  void SystemHeldLoopPacer::StartAgain() noexcept
  {
    // The queue fills from empty: as many frames as it holds may start back to back. The frame window's frames are counted on
    // from here
    m_budget = Capacity();
    m_heldInARow = 0;
    m_slots = 0;
    m_rule.RebaseNewest(NanosecondTimeSpan());
  }

  FrameStartPlan SystemHeldLoopPacer::PlanFrame(const NanosecondTickCount now) const noexcept
  {
    FrameStartPlan plan;
    // While the system holds the loop there is nothing to wait for: the loop runs into the system's hold
    if (m_hasFrame && !m_startsAgain && IsLimited() && m_limitTime > now)
    {
      plan.StartTime = m_limitTime;
    }
    return plan;
  }

  FrameSchedule SystemHeldLoopPacer::BeginFrame(const NanosecondTickCount cpuStartTime) noexcept
  {
    const RefreshPeriod period = m_rule.Refresh();
    const bool isFirstFrame = m_frameId == 0;
    SwapIntervalChange change = SwapIntervalChange::Unchanged;
    const NanosecondTimeSpan gap = cpuStartTime - m_startTime;
    const bool pause = !m_hasFrame || gap < NanosecondTimeSpan() || gap > m_rule.Settings().FrameWindowLength();
    m_limitedFrames += (m_hasFrame && !m_startsAgain && !pause && IsLimited()) ? 1u : 0u;
    // The limit's cadence starts at this frame: after a start, and where the hold was just found missing
    bool limitStartsHere = m_startsAgain || pause;
    if (m_startsAgain || pause)
    {
      // After another part placed the frames, a new swap chain or a pause: nothing is judged. Across a pause nothing is
      // measured, and the frame window starts empty
      if (!m_startsAgain)
      {
        m_rule.Clear();
      }
      StartAgain();
    }
    else
    {
      const NanosecondTimeSpan frameTime = period.TimeFor(m_swapInterval);
      if (m_notHolding)
      {
        // Limited, an eighth of a refresh faster than the display: frames in a row that start no sooner than the display can show
        // them say that something holds them to its rate, the system again (or work that takes longer, which needs no limit).
        // The queue is full then, and one frame's time of slack is left
        const bool atTheDisplaysRate = gap.Nanoseconds() >= frameTime.Nanoseconds() - (frameTime.Nanoseconds() / SlackDivisor);
        m_heldInARow = atTheDisplaysRate ? m_heldInARow + 1u : 0u;
        if (m_heldInARow >= HeldFramesToHoldAgain)
        {
          m_notHolding = false;
          m_heldInARow = 0;
          m_budget = frameTime.Nanoseconds();
        }
      }
      else
      {
        // The display takes a frame every swap interval: the time since the frame before refills the budget, no further than
        // the queue holds, and this frame takes its swap interval less the slack. A frame that starts with none left began
        // before the display could take the frames ahead of it: the system does not hold the loop
        m_budget = std::min(m_budget + gap.Nanoseconds(), Capacity());
        m_budget -= frameTime.Nanoseconds() - (frameTime.Nanoseconds() / SlackDivisor);
        if (m_budget < 0)
        {
          m_notHolding = true;
          m_heldInARow = 0;
          ++m_holdLosses;
          limitStartsHere = true;
        }
      }
      // The frame before, by its work against its swap interval's time: a lost refresh is not seen in this tier. Without an
      // EndFrame its work is not known, and it is not late
      const NanosecondTimeSpan cpuWork = m_frameEnded ? m_work : gap;
      const NanosecondTimeSpan work = m_frameEnded ? m_frameWork.WorkOf(cpuWork, m_rule.Settings().MaxFramesInFlight()) : cpuWork;
      const bool late = m_frameEnded && work > frameTime;
      m_slots += int64_t{m_swapInterval};
      change = m_rule.AddFrame(period.TimeFor(m_slots), work, late);
    }

    m_startsAgain = false;
    m_wait.BeginFrame();
    m_gpuWait.BeginFrame();
    m_swapInterval = m_rule.SwapInterval();
    m_hasFrame = true;
    m_startTime = cpuStartTime;
    m_work = NanosecondTimeSpan();
    m_frameOpen = true;
    m_frameEnded = false;
    ++m_frameId;
    m_frameWork.AddFrameStart(m_frameId, cpuStartTime);
    // The next frame's limit: a swap interval after this frame's on the cadence, an eighth of a refresh less while the system
    // does not hold the loop. It starts at this frame after a start, where the hold was just found missing, and where this frame
    // started more than a frame after its limit
    NanosecondTimeSpan frameTime = period.TimeFor(m_swapInterval);
    if (m_notHolding)
    {
      frameTime = NanosecondTimeSpan(frameTime.Nanoseconds() - (period.ToNanosecondTimeSpan().Nanoseconds() / ProbeDivisor));
    }
    const bool restarts = limitStartsHere || cpuStartTime > m_limitTime + frameTime;
    m_limitTime = (restarts ? cpuStartTime : m_limitTime) + frameTime;

    // The animation time: the first frame's is where the pacer starts, every other frame's is its swap interval after the one
    // before it, and nothing is caught up with
    if (!isFirstFrame)
    {
      m_animationTime.Add(m_swapInterval, period);
    }
    const NanosecondTimeSpan animationTime = m_animationTime.ToNanosecondTimeSpan();

    FrameSchedule schedule;
    schedule.FrameId = m_frameId;
    schedule.SwapInterval = m_swapInterval;
    schedule.AnimationTime = animationTime;
    schedule.AnimationStep = NanosecondTimeSpan(animationTime.Nanoseconds() - m_lastAnimationTime.Nanoseconds());
    // While the system holds the loop, when the next frame starts and when this one is shown are the system's: not known here
    if (IsLimited())
    {
      schedule.NextFrameStartTime = m_limitTime;
    }
    schedule.TargetFrameTime = NanosecondTimeDuration(period.TimeFor(m_swapInterval));
    schedule.PreferredFrameTime = NanosecondTimeDuration(period.TimeFor(m_rule.PreferredSwapInterval()));
    schedule.Change = change;
    schedule.Paced = true;
    m_lastAnimationTime = animationTime;
    return schedule;
  }

  PresentPlan SystemHeldLoopPacer::EndFrame(const NanosecondTickCount workDoneTime) noexcept
  {
    PresentPlan plan;
    if (!m_frameOpen)
    {
      return plan;
    }
    const NanosecondTimeDuration busy = NanosecondTimeDuration(workDoneTime - m_startTime);
    m_work = busy.Value();
    m_frameEnded = true;
    plan.FrameId = m_frameId;
    plan.CpuBusy = busy;
    return plan;
  }

  NanosecondTimeDuration SystemHeldLoopPacer::CpuBusyAt(const NanosecondTickCount now) const noexcept
  {
    return m_frameOpen ? NanosecondTimeDuration(now - m_startTime) : NanosecondTimeDuration();
  }

  void SystemHeldLoopPacer::AddPresent(const PresentReport& report) noexcept
  {
    m_lastPresentBlocked = report.Blocked();
    // A present the system did not take says the swap chain is gone: none of the new one's was taken yet
    m_presentTaken = report.Accepted;
    m_wait.AddPresent(report);
    m_gpuWait.AddPresent(report);
  }

  void SystemHeldLoopPacer::AddGpuWork(const GpuWorkReport& report) noexcept
  {
    m_frameWork.AddGpuWork(report, m_rule.Settings().FrameMargin());
  }

  void SystemHeldLoopPacer::AddSystemWait(const SystemWaitReport& /*report*/) noexcept
  {
    // A report only: the rate of the frames' starts says whether the system holds the loop, whichever wait held it
  }

  void SystemHeldLoopPacer::ForgetPresents() noexcept
  {
    m_presentTaken = false;
    m_wait.ForgetPresents();
    m_gpuWait.ForgetPresents();
    m_startsAgain = true;
  }

  void SystemHeldLoopPacer::SetRefreshPeriod(const RefreshPeriod period) noexcept
  {
    if (period == m_rule.Refresh())
    {
      return;
    }
    m_rule.SetRefreshPeriod(period);
    m_startsAgain = true;
    m_notHolding = false;
  }

  void SystemHeldLoopPacer::SetSettings(const PacerSettings& settings)
  {
    if (settings == m_rule.Settings())
    {
      return;
    }
    m_rule.SetSettings(settings);
    m_startsAgain = true;
    m_notHolding = false;
  }

  void SystemHeldLoopPacer::Reset() noexcept
  {
    m_rule.Reset(m_rule.PreferredSwapInterval());
    m_frameWork.Clear();
    m_wait.Reset();
    m_gpuWait.Reset();
    m_frameOpen = false;
    m_frameEnded = false;
    m_presentTaken = false;
    m_startsAgain = true;
    m_notHolding = false;
    m_heldInARow = 0;
  }

  SystemHoldState SystemHeldLoopPacer::Hold() const noexcept
  {
    if (m_notHolding)
    {
      return SystemHoldState::NotHolding;
    }
    return m_rule.SwapInterval() > m_presentHoldsUpTo ? SystemHoldState::Limited : SystemHoldState::Holding;
  }

  PacerHandover SystemHeldLoopPacer::GiveOver() const noexcept
  {
    PacerHandover handover;
    handover.HasFrame = m_hasFrame;
    handover.FrameId = m_frameId;
    handover.StartTime = m_startTime;
    // The next frame may start at once, or at the limit where the frames are limited
    handover.NextFrameStartTime = (m_hasFrame && IsLimited()) ? m_limitTime : m_startTime;
    handover.AnimationTime = m_animationTime;
    handover.LastAnimationTime = m_lastAnimationTime;
    handover.RefreshesBehindClock = m_refreshesBehindClock;
    handover.FrameWork = m_frameWork;
    handover.Wait = m_wait;
    handover.GpuWait = m_gpuWait;
    handover.PausePending = m_pausePending;
    handover.PauseHeldByWait = m_pauseHeldByWait;
    handover.HasPauseFirstFrame = m_pauseHasFirstFrame;
    handover.PauseFirstFrameTime = m_pauseFirstFrameTime;
    handover.PresentTaken = m_presentTaken;
    return handover;
  }

  void SystemHeldLoopPacer::TakeOver(const PacerHandover& handover, const SwapIntervalRule& rule) noexcept
  {
    m_rule.TakeOver(rule);
    m_frameWork = handover.FrameWork;
    m_wait = handover.Wait;
    m_gpuWait = handover.GpuWait;
    m_hasFrame = handover.HasFrame;
    m_frameId = handover.FrameId;
    m_startTime = handover.StartTime;
    m_frameOpen = false;
    m_frameEnded = false;
    m_swapInterval = m_rule.SwapInterval();
    m_animationTime = handover.AnimationTime;
    m_lastAnimationTime = handover.LastAnimationTime;
    m_refreshesBehindClock = handover.RefreshesBehindClock;
    m_pausePending = handover.PausePending;
    m_pauseHeldByWait = handover.PauseHeldByWait;
    m_pauseHasFirstFrame = handover.HasPauseFirstFrame;
    m_pauseFirstFrameTime = handover.PauseFirstFrameTime;
    m_presentTaken = handover.PresentTaken;
    // The system is taken to hold the loop, and checked from the next frame on
    m_startsAgain = true;
    m_notHolding = false;
    m_heldInARow = 0;
  }
}
