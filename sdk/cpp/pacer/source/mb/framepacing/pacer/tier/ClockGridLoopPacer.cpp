// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The pacers on a timer (sdk/doc/pacer-design.md "A grid on the clock"): frame starts on one grid of refresh
// periods on the clock, a swap interval from the rule, an animation time that advances by the swap interval and by a loss that
// repeats. With a wait for a present the loop is held until the display took an earlier frame and the grid follows the ends of
// the waits that held it; without one there is a pause after start-up, a whole period after a late present, and the system's
// own waits.
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/pacer/placement/DisplayPlacementUtil.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <mb/framepacing/pacer/tier/ClockGridLoopPacer.hpp>
#include <algorithm>

namespace MB::FramePacing::Pacer
{
  namespace
  {
    //! A wait held the loop when it took this share of a refresh period or more: one in this many
    constexpr int64_t HeldDivisor = 8;
    //! Where the system is to pace the loop, the loop is held to this share of a period before a frame is due: one in this
    //! many, twice what counts as held
    constexpr int64_t HoldLeadDivisor = 4;
    //! A wait for a present that held the loop moves the grid this share of the way towards its end: one in this many
    constexpr int64_t FollowDivisor = 4;

    //! A time a wait held the loop for, added to what the waits before it did: never more than the longest refresh period
    NanosecondTimeSpan AddHeld(const NanosecondTimeSpan held, const NanosecondTimeDuration blocked) noexcept
    {
      const int64_t longest = RefreshPeriod::MaxPeriod.Nanoseconds();
      return NanosecondTimeSpan(std::min(held.Nanoseconds() + std::min(blocked.Nanoseconds(), longest), longest));
    }
  }

  ClockGridLoopPacer::ClockGridLoopPacer(const PacerSettings& settings, const bool waitsForPresent)
    : m_rule(settings)
    , m_waitsForPresent(waitsForPresent)
  {
  }

  bool ClockGridLoopPacer::StartsAgainAt(const NanosecondTickCount time) const noexcept
  {
    if (!m_hasGrid)
    {
      return true;
    }
    // A pause the pacer did not ask for, or a clock that went back: nothing is measured across it
    const NanosecondTimeSpan gap = time - m_startTime;
    const NanosecondTimeSpan reach = std::max(m_rule.Settings().FrameWindowLength(), m_rule.Refresh().TimeFor(int64_t{2} * (m_nextSlot - m_slot)));
    return gap < NanosecondTimeSpan() || gap > reach;
  }

  int64_t ClockGridLoopPacer::Reserve() const noexcept
  {
    // The frames made ahead of the display: with the aim of smoothness, and at one refresh per frame only (at more the display
    // takes a frame before the next one is made, and nothing can wait)
    const PacerSettings& settings = m_rule.Settings();
    return settings.Aim() == PacerAim::Smoothness && m_rule.SwapInterval() == 1 ? int64_t{settings.ReserveFrames()} : 0;
  }

  bool ClockGridLoopPacer::LetsTheSystemPace() const noexcept
  {
    // Where the system holds the loop while its queue is full, the aim of smoothness means that on purpose, at one refresh
    // per frame (at more the display takes a frame before the next one is made, and the queue is never full)
    const PacerSettings& settings = m_rule.Settings();
    // A loop that waits for a present is held by that wait, and not by a queue that is full
    return !m_waitsForPresent && settings.SystemHoldsLoop() && settings.Aim() == PacerAim::Smoothness && m_rule.SwapInterval() == 1;
  }

  bool ClockGridLoopPacer::HeldByTheDisplaysSide() const noexcept
  {
    // The waits since the last frame started in which the display's side held the loop took a share of a refresh period
    return m_displayHeld.Nanoseconds() >= (m_rule.Refresh().ToNanosecondTimeSpan().Nanoseconds() / HeldDivisor);
  }

  bool ClockGridLoopPacer::LetThroughByTheDisplay() const noexcept
  {
    // The display's side let the loop through when its wait held the loop past the time the pacer holds it to, for a share of a
    // refresh period. A wait that was over before that time paced nothing: the timer did (a present that waits a third of a
    // refresh in every frame is such a wait)
    return m_displayHeldPastTimer.Nanoseconds() >= (m_rule.Refresh().ToNanosecondTimeSpan().Nanoseconds() / HeldDivisor);
  }

  NanosecondTimeDuration ClockGridLoopPacer::HeldPastTheTimer(const NanosecondTickCount beginTime, const NanosecondTickCount endTime) const noexcept
  {
    // Before there is a grid the pacer holds the loop to no time, and the whole wait counts
    const bool hasGrid = m_hasGrid && !m_takenOver;
    const int64_t from = hasGrid ? std::max(beginTime.Nanoseconds(), StartTimeOf(m_nextSlot).Nanoseconds()) : beginTime.Nanoseconds();
    return NanosecondTimeDuration::FromNanoseconds(std::max(endTime.Nanoseconds() - from, int64_t{0}));
  }

  NanosecondTickCount ClockGridLoopPacer::StartTimeOf(const int64_t slot) const noexcept
  {
    // The time the loop is held to before a frame. Where the system is to pace the loop it is a share of a period before the
    // frame is due, so that the loop is there first and the system's wait, not the timer, says when the frame starts; the
    // timer still keeps the loop from running away where the system does not hold it after all
    const NanosecondTickCount due = DueTime(slot);
    return LetsTheSystemPace() ? due - NanosecondTimeSpan(m_rule.Refresh().ToNanosecondTimeSpan().Nanoseconds() / HoldLeadDivisor) : due;
  }

  NanosecondTickCount ClockGridLoopPacer::DueTime(const int64_t slot) const noexcept
  {
    // A frame starts the reserve's refreshes before its step of the grid
    return TimeOfSlot(slot - Reserve());
  }

  int64_t ClockGridLoopPacer::SmoothSlotFor(const NanosecondTickCount time) const noexcept
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

  int64_t ClockGridLoopPacer::SlotFor(const NanosecondTickCount time) const noexcept
  {
    if (m_rule.Settings().Aim() == PacerAim::Smoothness)
    {
      return SmoothSlotFor(time);
    }
    // The step nearest to the time, never one before the step the frame is due at (early is waited for, not taken), and never
    // one that comes too soon after a present that was made late
    return std::max({m_nextSlot, m_rule.Refresh().NearestRefreshes(time - m_origin), SlotAfterPresent()});
  }

  int64_t ClockGridLoopPacer::SlotAfterPresent() const noexcept
  {
    // A present made before the step the next frame is due at is where it always is: nothing to keep away from. With a wait
    // for a present it is the wait that keeps two presents apart
    if (m_waitsForPresent || !m_hasPresentTime || m_presentTime <= TimeOfSlot(m_dueSlot))
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

  NanosecondTickCount ClockGridLoopPacer::TimeOfSlot(const int64_t slot) const noexcept
  {
    return m_origin + m_rule.Refresh().TimeFor(slot);
  }

  void ClockGridLoopPacer::ArmStartupPause() noexcept
  {
    m_pausePending = true;
    m_pauseHasFirstFrame = false;
    m_presentTaken = false;
  }

  uint32_t ClockGridLoopPacer::StartupPauseAt(const NanosecondTickCount cpuStartTime) noexcept
  {
    // The pause takes the frames that wait away: it belongs to the aim of low latency, and to a loop that no wait for a
    // present holds
    if (m_waitsForPresent || !m_pausePending || m_rule.Settings().Aim() != PacerAim::LowLatency)
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

  FrameStartPlan ClockGridLoopPacer::PlanFrame(const NanosecondTickCount now) const noexcept
  {
    // The wait for a present, where the loop has one, and after it the time the frame is due at
    FrameStartPlan plan = m_waitsForPresent ? m_wait.Plan(m_rule.Settings(), m_rule.Refresh(), m_rule.SwapInterval()) : FrameStartPlan();
    if (m_takenOver)
    {
      // The first frame after another pacer placed the frames: it starts when the frame before it said the next one would
      if (m_takeOverStartTime > now)
      {
        plan.StartTime = m_takeOverStartTime;
      }
      return plan;
    }
    // A present that waited for the display past the time the loop is held to has let the loop through: the frame starts now
    if (!StartsAgainAt(now) && !(LetsTheSystemPace() && LetThroughByTheDisplay()))
    {
      const NanosecondTickCount start = StartTimeOf(SlotFor(now));
      if (start > now)
      {
        plan.StartTime = start;
      }
    }
    return plan;
  }

  void ClockGridLoopPacer::AddSystemWait(const SystemWaitReport& report) noexcept
  {
    // Added up over the waits before one frame: the display's side holds the loop with a wait for an image, the GPU with a
    // wait for a frame slot
    if (report.Kind == SystemWaitKind::FrameSlot)
    {
      m_frameSlotHeld = AddHeld(m_frameSlotHeld, report.Blocked());
    }
    else
    {
      m_displayHeld = AddHeld(m_displayHeld, report.Blocked());
      m_displayHeldPastTimer = AddHeld(m_displayHeldPastTimer, HeldPastTheTimer(report.BeginTime, report.EndTime));
    }
  }

  void ClockGridLoopPacer::AddPresentWait(const PresentWaitReport& report) noexcept
  {
    const RefreshPeriod period = m_rule.Refresh();
    // A wait that ran out, or an answer while the waits are stopped, says nothing of where the display is
    if (!m_wait.AddPresentWait(report, period))
    {
      return;
    }
    // A wait that returned at once says nothing: the present was shown some time before. One that held the loop ended when the
    // display took a frame: the grid's step nearest to its end is moved a quarter of the way towards it
    if (!m_hasGrid || m_takenOver || StartsAgainAt(report.EndTime) || !PresentWaitRule::HeldTheLoop(report, period))
    {
      return;
    }
    const int64_t nearest = period.NearestRefreshes(report.EndTime - m_origin);
    const int64_t offNanoseconds = (report.EndTime - TimeOfSlot(nearest)).Nanoseconds();
    m_origin = m_origin + NanosecondTimeSpan(offNanoseconds / FollowDivisor);
  }

  FrameSchedule ClockGridLoopPacer::BeginFrame(const NanosecondTickCount cpuStartTime) noexcept
  {
    const RefreshPeriod period = m_rule.Refresh();
    SwapIntervalChange change = SwapIntervalChange::Unchanged;
    const bool isFirstFrame = m_frameId == 0;
    // The steps of the grid the previous frame took more than it was given, and the frame before it
    int64_t lost = 0;
    int64_t lostBefore = 0;
    // The display's side held the loop before this frame, or the GPU did: a wait took a share of a refresh period
    const int64_t heldFor = period.ToNanosecondTimeSpan().Nanoseconds() / HeldDivisor;
    const bool heldBySystem = HeldByTheDisplaysSide();
    m_systemHeldFrames += heldBySystem ? 1u : 0u;
    m_frameSlotHeldFrames += m_frameSlotHeld.Nanoseconds() >= heldFor ? 1u : 0u;
    if (StartsAgainAt(cpuStartTime))
    {
      // The grid starts at this frame: nothing was measured, the frame window starts empty and the swap interval stays
      m_rule.Clear();
      m_origin = cpuStartTime;
      m_slot = 0;
      m_behind = 0;
      m_hasGrid = true;
    }
    else if (m_takenOver || m_wait.WaitRanOut())
    {
      // The pacer's own wait held this frame's start until it ran out: the display is not taking the window's frames, so when
      // the previous frame was shown, or whether, is not known, and this start is late by the pacer's doing. Or another
      // pacer placed the frame before this one, and there is no step of this grid it was due at. Nothing is judged and the
      // frame window stays as it is; the grid goes on from this frame, and the frame window's times with it: the frame
      // before this one would have left at step 0, and the newest frame of the frame window a swap interval before that
      m_origin = cpuStartTime;
      m_slot = 0;
      m_behind = 0;
      m_rule.RebaseNewest(NanosecondTimeSpan(-period.TimeFor(m_swapInterval).Nanoseconds()));
    }
    else
    {
      // The previous frame: the steps of the grid from the one it was due to leave at to this start, and its work against its
      // swap interval's time. Without an EndFrame its work is not known, and the time to this start says nothing about it
      const bool letThrough = LetsTheSystemPace() && LetThroughByTheDisplay();
      if (letThrough)
      {
        // The system let the frame through when its queue had room, which is when the display took a frame: the grid is moved
        // so that this start is where the frame was due, and the frame before it loses no step by it
        m_origin = m_origin + (cpuStartTime - DueTime(m_nextSlot));
      }
      const int64_t slot = letThrough ? m_nextSlot : SlotFor(cpuStartTime);
      lost = slot - m_nextSlot;
      lostBefore = m_lost;
      // Where in its step a present is made when it is on time
      if (m_hasPresentTime && m_presentTime <= TimeOfSlot(m_dueSlot))
      {
        m_presentPlace = m_presentTime - TimeOfSlot(period.FloorRefreshes(m_presentTime - m_origin));
      }
      const NanosecondTimeSpan cpuWork = m_frameEnded ? m_work : NanosecondTimeDuration(cpuStartTime - m_startTime).Value();
      const NanosecondTimeSpan work = m_frameEnded ? m_frameWork.WorkOf(cpuWork, m_rule.Settings().MaxFramesInFlight()) : cpuWork;
      const bool late = lost > 0 || (m_frameEnded && work > period.TimeFor(m_swapInterval));
      change = m_rule.AddFrame(period.TimeFor(slot), work, late, StartTimeOf(m_nextSlot) - cpuStartTime);
      // The steps the frame is behind the one it takes: within the reserve, and made up for by the frames after it
      m_behind = std::max(period.NearestRefreshes(cpuStartTime - DueTime(slot)), int64_t{0});
      m_slot = slot;
    }
    // A loss that repeats: the frame before this one took refreshes more than it was given, and so did the one before that. A
    // swap interval the rule just changed is its answer to the losses before it
    const int64_t repeated = change == SwapIntervalChange::Unchanged ? std::min(lost, lostBefore) : 0;
    const auto repeatedLoss = static_cast<uint32_t>(std::min(repeated, int64_t{PacerSettings::MaxSwapInterval}));
    m_lost = change == SwapIntervalChange::Unchanged ? lost : 0;

    m_displayHeld = NanosecondTimeSpan();
    m_displayHeldPastTimer = NanosecondTimeSpan();
    m_frameSlotHeld = NanosecondTimeSpan();
    m_wait.BeginFrame();
    m_takenOver = false;
    m_swapInterval = m_rule.SwapInterval();
    m_startTime = cpuStartTime;
    m_work = NanosecondTimeSpan();
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
    const NanosecondTimeSpan animationTime = m_animationTime.ToNanosecondTimeSpan();
    // How far the animation time is behind the clock: what was lost and what is paused for, less what the step is longer by
    m_refreshesBehindClock += (static_cast<uint64_t>(lost) - repeatedLoss) + pause;

    FrameSchedule schedule;
    schedule.FrameId = m_frameId;
    schedule.SwapInterval = m_swapInterval;
    schedule.AnimationTime = animationTime;
    schedule.AnimationStep = NanosecondTimeSpan(animationTime.Nanoseconds() - m_lastAnimationTime.Nanoseconds());
    // The step this frame is due to leave the grid at is where it is expected to reach the screen: without a display to ask, it
    // is the pacer's aim for the frame. The next frame starts there, or a pause later
    schedule.NextFrameStartTime = StartTimeOf(m_nextSlot);
    schedule.IntendedDisplayTime = TimeOfSlot(m_dueSlot);
    schedule.TargetFrameTime = NanosecondTimeDuration(period.TimeFor(m_swapInterval));
    schedule.PreferredFrameTime = NanosecondTimeDuration(period.TimeFor(m_rule.PreferredSwapInterval()));
    schedule.Change = change;
    m_lastAnimationTime = animationTime;
    m_nextFrameStartTime = schedule.NextFrameStartTime;
    return schedule;
  }

  PresentPlan ClockGridLoopPacer::EndFrame(const NanosecondTickCount workDoneTime) noexcept
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
    // The time the present is given, where it takes one. With a time before which the frame is not shown the display's side
    // shows it at the refresh nearest to the step it is due at, however many refreshes that is after the frame before it,
    // and the frame is presented when it is done
    DisplayPlacementUtil::Place(plan, m_presentTiming, TimeOfSlot(m_dueSlot), m_swapInterval, m_rule.Refresh());
    if (m_presentTiming != PresentTiming::AtTime && m_swapInterval > 1)
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

  NanosecondTimeDuration ClockGridLoopPacer::CpuBusyAt(const NanosecondTickCount now) const noexcept
  {
    return m_frameOpen ? NanosecondTimeDuration(now - m_startTime) : NanosecondTimeDuration();
  }

  void ClockGridLoopPacer::AddPresent(const PresentReport& report) noexcept
  {
    m_lastPresentBlocked = report.Blocked();
    // A present that waited for the display held the loop before the next frame
    m_displayHeld = AddHeld(m_displayHeld, report.Blocked());
    m_displayHeldPastTimer = AddHeld(m_displayHeldPastTimer, HeldPastTheTimer(report.CallTime, report.ReturnTime));
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
    m_wait.AddPresent(report);
  }

  void ClockGridLoopPacer::AddGpuWork(const GpuWorkReport& report) noexcept
  {
    m_frameWork.AddGpuWork(report, m_rule.Settings().FrameMargin());
  }

  void ClockGridLoopPacer::ForgetPresents() noexcept
  {
    ArmStartupPause();
    m_wait.ForgetPresents();
  }

  PacerHandover ClockGridLoopPacer::GiveOver() const noexcept
  {
    PacerHandover handover;
    handover.HasFrame = m_hasGrid && m_frameId != 0;
    handover.FrameId = m_frameId;
    handover.StartTime = m_startTime;
    handover.NextFrameStartTime = m_takenOver ? m_takeOverStartTime : m_nextFrameStartTime;
    handover.AnimationTime = m_animationTime;
    handover.LastAnimationTime = m_lastAnimationTime;
    handover.RefreshesBehindClock = m_refreshesBehindClock;
    handover.FrameWork = m_frameWork;
    handover.Wait = m_wait;
    return handover;
  }

  void ClockGridLoopPacer::TakeOver(const PacerHandover& handover, const SwapIntervalRule& rule) noexcept
  {
    m_rule.TakeOver(rule);
    m_frameWork = handover.FrameWork;
    m_wait = handover.Wait;
    m_frameId = handover.FrameId;
    m_animationTime = handover.AnimationTime;
    m_lastAnimationTime = handover.LastAnimationTime;
    m_refreshesBehindClock = handover.RefreshesBehindClock;
    // The grid starts at the next frame. Until then the frame before it stands in for a frame of this grid, so that a pause
    // before the next frame is still seen
    m_hasGrid = handover.HasFrame;
    m_takenOver = handover.HasFrame;
    m_takeOverStartTime = handover.NextFrameStartTime;
    m_startTime = handover.StartTime;
    m_swapInterval = m_rule.SwapInterval();
    m_slot = 0;
    m_dueSlot = int64_t{m_swapInterval};
    m_nextSlot = m_dueSlot;
    m_lost = 0;
    m_behind = 0;
    m_work = NanosecondTimeSpan();
    m_frameOpen = false;
    m_frameEnded = false;
    m_hasPresentTime = false;
    m_displayHeld = NanosecondTimeSpan();
    m_displayHeldPastTimer = NanosecondTimeSpan();
    m_frameSlotHeld = NanosecondTimeSpan();
  }

  void ClockGridLoopPacer::SetRefreshPeriod(const RefreshPeriod period) noexcept
  {
    if (period != m_rule.Refresh())
    {
      m_rule.SetRefreshPeriod(period);
      m_hasGrid = false;
    }
  }

  void ClockGridLoopPacer::SetSettings(const PacerSettings& settings)
  {
    if (settings != m_rule.Settings())
    {
      m_rule.SetSettings(settings);
      m_hasGrid = false;
    }
  }

  void ClockGridLoopPacer::Reset() noexcept
  {
    m_rule.Reset(m_rule.PreferredSwapInterval());
    m_frameWork.Clear();
    m_hasGrid = false;
    m_frameOpen = false;
    m_frameEnded = false;
    m_hasPresentTime = false;
    ArmStartupPause();
    m_wait.Reset();
  }
}
