// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The pacer of the lowest pair of tiers (sdk/doc/pacer-design.md "A grid on the clock"): frame starts on one grid of
// refresh periods on the clock, a swap interval from the rule, an animation time that advances by the swap interval and by a loss
// that repeats, and one pause after start-up.
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <mb/framepacing/pacer/tier/TimerPeriodOnlyPacer.hpp>
#include <algorithm>
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

  TimerPeriodOnlyPacer::TimerPeriodOnlyPacer(const PacerSettings& settings)
    : m_rule(settings)
  {
  }

  bool TimerPeriodOnlyPacer::StartsAgainAt(const TickCount64 time) const noexcept
  {
    if (!m_hasGrid)
    {
      return true;
    }
    // A pause the pacer did not ask for, or a clock that went back: nothing is measured across it
    const TimeSpan gap = time - m_startTime;
    const TimeSpan reach = std::max(m_rule.Settings().FrameWindowLength(), m_rule.Refresh().TimeFor(int64_t{2} * (m_nextSlot - m_slot)));
    return gap < TimeSpan() || gap > reach;
  }

  int64_t TimerPeriodOnlyPacer::SlotFor(const TickCount64 time) const noexcept
  {
    // The step nearest to the time, never one before the step the frame is due at (early is waited for, not taken), and never
    // one that comes too soon after a present that was made late
    return std::max({m_nextSlot, m_rule.Refresh().NearestRefreshes(time - m_origin), SlotAfterPresent()});
  }

  int64_t TimerPeriodOnlyPacer::SlotAfterPresent() const noexcept
  {
    // A present made before the step the next frame is due at is where it always is: nothing to keep away from
    if (!m_hasPresentTime || m_presentTime <= TimeOfSlot(m_dueSlot))
    {
      return m_nextSlot;
    }
    // The frame ran long, and its present was made somewhere in a later step. The display takes one frame per refresh, so the
    // next present has to come a whole period after this one, or the two can reach the display between the same two refreshes
    // and one of them waits from then on. A frame is presented at its own place in its step: the next step will do when this
    // present was made no later in its step than the last one that was on time, and else it is the step after
    const int64_t slot = m_rule.Refresh().FloorRefreshes(m_presentTime - m_origin);
    return slot + ((m_presentTime - TimeOfSlot(slot)) <= m_presentPlace ? 1 : 2);
  }

  TickCount64 TimerPeriodOnlyPacer::TimeOfSlot(const int64_t slot) const noexcept
  {
    return m_origin + m_rule.Refresh().TimeFor(slot);
  }

  void TimerPeriodOnlyPacer::ArmStartupPause() noexcept
  {
    m_pausePending = true;
    m_pauseHasFirstFrame = false;
    m_presentTaken = false;
  }

  uint32_t TimerPeriodOnlyPacer::StartupPauseAt(const TickCount64 cpuStartTime) noexcept
  {
    if (!m_pausePending)
    {
      return 0;
    }
    if (!m_pauseHasFirstFrame)
    {
      m_pauseFirstFrameTime = cpuStartTime;
      m_pauseHasFirstFrame = true;
    }
    // Not before a frame is on its way to the screen, and not before the presents that pile up were made
    if (!m_presentTaken || (cpuStartTime - m_pauseFirstFrameTime) < m_rule.Settings().StartupPauseDelay())
    {
      return 0;
    }
    m_pausePending = false;
    // At two refreshes per frame or more the display took the frames that waited by now: nothing to pause for
    if (m_swapInterval > 1)
    {
      return 0;
    }
    const uint32_t refreshes = m_rule.Settings().StartupPauseRefreshes();
    m_startupPauses += refreshes > 0 ? 1u : 0u;
    return refreshes;
  }

  FrameStartPlan TimerPeriodOnlyPacer::PlanFrame(const TickCount64 now) const noexcept
  {
    FrameStartPlan plan;
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

  FrameSchedule TimerPeriodOnlyPacer::BeginFrame(const TickCount64 cpuStartTime) noexcept
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
      // The previous frame: the steps of the grid from the one it was due to leave at to this start, and its work against its
      // swap interval's time. Without an EndFrame its work is not known, and the time to this start says nothing about it
      const int64_t slot = SlotFor(cpuStartTime);
      lost = slot - m_nextSlot;
      lostBefore = m_lost;
      // Where in its step a present is made when it is on time
      if (m_hasPresentTime && m_presentTime <= TimeOfSlot(m_dueSlot))
      {
        m_presentPlace = m_presentTime - TimeOfSlot(period.FloorRefreshes(m_presentTime - m_origin));
      }
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
    m_startTime = cpuStartTime;
    m_work = TimeSpan();
    m_frameOpen = true;
    m_frameEnded = false;
    ++m_frameId;
    m_frameWork.AddFrameStart(m_frameId, cpuStartTime);

    // The one pause after start-up: the frame after this one is due that many refreshes later
    const uint32_t pause = StartupPauseAt(cpuStartTime);
    m_dueSlot = m_slot + int64_t{m_swapInterval};
    m_nextSlot = m_dueSlot + int64_t{pause};
    m_hasPresentTime = false;

    // The animation time: the first frame's is where the pacer starts, every other frame's is its swap interval after the one
    // before it. A refresh that was lost is not caught up with. With a loss that repeats the display shows every frame for that
    // much longer, and the step is longer by it: by the fewer of the two frames' losses
    if (!isFirstFrame)
    {
      m_animationTime.Add(m_swapInterval + repeatedLoss, period);
    }
    const TimeSpan animationTime = m_animationTime.ToTimeSpan();
    // How far the animation time is behind the clock: what was lost and what is paused for, less what the step is longer by
    m_refreshesBehindClock += (static_cast<uint64_t>(lost) - repeatedLoss) + pause;

    FrameSchedule schedule;
    schedule.FrameId = m_frameId;
    schedule.SwapInterval = m_swapInterval;
    schedule.AnimationTime = animationTime;
    schedule.AnimationStep = TimeSpan(animationTime.Ticks() - m_lastAnimationTime.Ticks());
    // The step this frame is due to leave the grid at is where it is expected to reach the screen: without a display to ask, it
    // is the pacer's aim for the frame. The next frame starts there, or a pause later
    schedule.NextFrameStartTime = TimeOfSlot(m_nextSlot);
    schedule.IntendedDisplayTime = TimeOfSlot(m_dueSlot);
    schedule.TargetFrameTime = ToTimeSpan32(period.TimeFor(m_swapInterval));
    schedule.PreferredFrameTime = ToTimeSpan32(period.TimeFor(m_rule.PreferredSwapInterval()));
    schedule.Change = change;
    m_lastAnimationTime = animationTime;
    return schedule;
  }

  PresentPlan TimerPeriodOnlyPacer::EndFrame(const TickCount64 workDoneTime) noexcept
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
    // When the present is made, as far as it is known here: AddPresent says when it was
    m_presentTime = plan.WaitsForPresentTime() ? plan.PresentTime : workDoneTime;
    m_hasPresentTime = true;
    return plan;
  }

  TimeSpan32 TimerPeriodOnlyPacer::CpuBusyAt(const TickCount64 now) const noexcept
  {
    return m_frameOpen ? ToTimeSpan32(now - m_startTime) : TimeSpan32();
  }

  void TimerPeriodOnlyPacer::AddPresent(const PresentReport& report) noexcept
  {
    m_lastPresentBlocked = report.Blocked();
    if (m_frameEnded && report.FrameId == m_frameId)
    {
      m_presentTime = report.CallTime;
    }
    if (report.Accepted)
    {
      m_presentTaken = true;
    }
    else
    {
      // The swap chain the present was made for is gone, and a new one starts as a new window does
      ArmStartupPause();
    }
  }

  void TimerPeriodOnlyPacer::AddGpuWork(const GpuWorkReport& report) noexcept
  {
    m_frameWork.AddGpuWork(report, m_rule.Settings().FrameMargin());
  }

  void TimerPeriodOnlyPacer::ForgetPresents() noexcept
  {
    ArmStartupPause();
  }

  void TimerPeriodOnlyPacer::SetRefreshPeriod(const RefreshPeriod period) noexcept
  {
    if (period != m_rule.Refresh())
    {
      m_rule.SetRefreshPeriod(period);
      m_hasGrid = false;
    }
  }

  void TimerPeriodOnlyPacer::SetSettings(const PacerSettings& settings)
  {
    if (settings != m_rule.Settings())
    {
      m_rule.SetSettings(settings);
      m_hasGrid = false;
    }
  }

  void TimerPeriodOnlyPacer::Reset() noexcept
  {
    m_rule.Reset(m_rule.PreferredSwapInterval());
    m_frameWork.Clear();
    m_hasGrid = false;
    m_frameOpen = false;
    m_frameEnded = false;
    m_hasPresentTime = false;
    ArmStartupPause();
  }
}
