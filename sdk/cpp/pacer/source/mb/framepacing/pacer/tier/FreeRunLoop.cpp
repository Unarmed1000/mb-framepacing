// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The frames of an application that is not pacing (sdk/doc/pacer-design.md, decision 7): nothing is placed and
// nothing is held, and what a run hands over is kept current for the part that paces next.
#include <mb/framepacing/pacer/tier/FreeRunLoop.hpp>

namespace MB::FramePacing::Pacer
{
  FrameSchedule FreeRunLoop::BeginFrame(const NanosecondTickCount cpuStartTime, const RefreshPeriod period,
                                        const NanosecondTimeSpan pauseAfter) noexcept
  {
    const bool isFirstFrame = m_frameId == 0;
    // The animation time: the first frame's is where the pacer starts. Every other frame's is the time since the frame before
    // it started later, as an application without a pacer steps. Across a pause, a clock that went back or frames that
    // started again, nothing is measured: it goes on by one refresh
    if (!isFirstFrame)
    {
      const NanosecondTimeSpan gap = cpuStartTime - m_startTime;
      const bool measured = m_hasFrame && gap >= NanosecondTimeSpan() && gap <= pauseAfter;
      const NanosecondTimeSpan step = measured ? gap : period.ToNanosecondTimeSpan();
      m_animationTime = RefreshTime(NanosecondTimeSpan(m_animationTime.ToNanosecondTimeSpan().Nanoseconds() + step.Nanoseconds()));
    }
    const NanosecondTimeSpan animationTime = m_animationTime.ToNanosecondTimeSpan();

    if (!m_frameOpen)
    {
      // The first frame here since a part that paced handed over: the frames that pile up from now on are what the pause
      // after start-up is for, when pacing starts again
      m_pausePending = true;
      m_pauseHeldByWait = false;
      m_pauseHasFirstFrame = false;
      m_presentTaken = false;
    }
    m_wait.BeginFrame();
    m_gpuWait.BeginFrame();
    m_hasFrame = true;
    m_startTime = cpuStartTime;
    m_frameOpen = true;
    m_frameEnded = false;
    ++m_frameId;
    m_frameWork.AddFrameStart(m_frameId, cpuStartTime);

    // No intended display time, no time for the next frame's start and no target frame time: nothing is aimed for
    FrameSchedule schedule;
    schedule.FrameId = m_frameId;
    schedule.AnimationTime = animationTime;
    schedule.AnimationStep = NanosecondTimeSpan(animationTime.Nanoseconds() - m_lastAnimationTime.Nanoseconds());
    m_lastAnimationTime = animationTime;
    return schedule;
  }

  PresentPlan FreeRunLoop::EndFrame(const NanosecondTickCount workDoneTime) noexcept
  {
    PresentPlan plan;
    if (!m_frameOpen)
    {
      return plan;
    }
    m_frameEnded = true;
    plan.FrameId = m_frameId;
    plan.CpuBusy = NanosecondTimeDuration(workDoneTime - m_startTime);
    return plan;
  }

  NanosecondTimeDuration FreeRunLoop::CpuBusyAt(const NanosecondTickCount now) const noexcept
  {
    return m_frameOpen ? NanosecondTimeDuration(now - m_startTime) : NanosecondTimeDuration();
  }

  void FreeRunLoop::AddPresent(const PresentReport& report) noexcept
  {
    m_lastPresentBlocked = report.Blocked();
    // A present the system did not take says the swap chain is gone: none of the new one's was taken yet
    m_presentTaken = report.Accepted;
    m_wait.AddPresent(report);
    m_gpuWait.AddPresent(report);
  }

  void FreeRunLoop::AddGpuWork(const GpuWorkReport& report, const NanosecondTimeSpan margin) noexcept
  {
    m_frameWork.AddGpuWork(report, margin);
  }

  void FreeRunLoop::ForgetPresents() noexcept
  {
    m_presentTaken = false;
    m_wait.ForgetPresents();
    m_gpuWait.ForgetPresents();
  }

  void FreeRunLoop::Reset() noexcept
  {
    m_frameWork.Clear();
    m_hasFrame = false;
    m_frameOpen = false;
    m_frameEnded = false;
    m_presentTaken = false;
    m_wait.Reset();
    m_gpuWait.Reset();
  }

  PacerHandover FreeRunLoop::GiveOver() const noexcept
  {
    PacerHandover handover;
    handover.HasFrame = m_hasFrame;
    handover.FrameId = m_frameId;
    handover.StartTime = m_startTime;
    // Nothing was said of when the next frame starts: it may start at once
    handover.NextFrameStartTime = m_startTime;
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

  void FreeRunLoop::TakeOver(const PacerHandover& handover) noexcept
  {
    m_frameWork = handover.FrameWork;
    m_wait = handover.Wait;
    m_gpuWait = handover.GpuWait;
    m_hasFrame = handover.HasFrame;
    m_frameId = handover.FrameId;
    m_startTime = handover.StartTime;
    m_frameOpen = false;
    m_frameEnded = false;
    m_animationTime = handover.AnimationTime;
    m_lastAnimationTime = handover.LastAnimationTime;
    m_refreshesBehindClock = handover.RefreshesBehindClock;
    m_pausePending = handover.PausePending;
    m_pauseHeldByWait = handover.PauseHeldByWait;
    m_pauseHasFirstFrame = handover.HasPauseFirstFrame;
    m_pauseFirstFrameTime = handover.PauseFirstFrameTime;
    m_presentTaken = handover.PresentTaken;
  }
}
