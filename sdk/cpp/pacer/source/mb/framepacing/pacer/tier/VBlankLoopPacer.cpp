// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The pacers of vertical blank times (sdk/doc/pacer-design.md): every frame is for one vertical blank of the
// display, made early and its present held (smoothness) or its start held so that it is ready just in time (low latency).
// With a wait for a present the loop is held until the display took an earlier frame, and the wait says which vertical blank
// a frame was shown at; without one there is a pause after start-up.
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <mb/framepacing/pacer/tier/VBlankLoopPacer.hpp>
#include <algorithm>
#include "../detail/MarkerValue.hpp"

namespace MB::FramePacing::Pacer
{
  namespace
  {
    //! The most refreshes an animation step is counted in: far more than a frame window holds
    constexpr int64_t MaxStepRefreshes = int64_t{1} << 20;
    //! The place a frame is to be ready at is moved earlier by this share of a refresh period at a time: one in this many
    constexpr int64_t ReadyPlaceStepDivisor = 8;
    //! The frames shown later than worked out that move it: one by itself is a refresh the display lost
    constexpr uint32_t ShownLaterToMovePlace = 2;
    //! They count together while no more than this many frames pass between them: what a wait says of a frame comes a frame or
    //! two after it
    constexpr uint32_t ShownLaterFramesApart = 8;
  }

  VBlankLoopPacer::VBlankLoopPacer(const PacerSettings& settings, const bool waitsForPresent)
    : m_rule(settings)
    , m_waitsForPresent(waitsForPresent)
  {
  }

  bool VBlankLoopPacer::StartsAgainAt(const NanosecondTickCount time) const noexcept
  {
    if (!m_hasFrame)
    {
      return true;
    }
    // A pause the pacer did not ask for, or a clock that went back: nothing is measured across it
    const NanosecondTimeSpan gap = time - m_startTime;
    const NanosecondTimeSpan reach =
      std::max(m_rule.Settings().FrameWindowLength(), m_rule.Refresh().TimeFor(int64_t{2} * (int64_t{m_swapInterval} + m_pauseSlots)));
    return gap < NanosecondTimeSpan() || gap > reach;
  }

  NanosecondTickCount VBlankLoopPacer::TimeOfBlank(const int64_t slot) const noexcept
  {
    return m_timeline.TimeOfBlank(slot, m_rule.Refresh());
  }

  int64_t VBlankLoopPacer::BlankAtOrBefore(const NanosecondTickCount time) const noexcept
  {
    return m_timeline.BlankAtOrBefore(time, m_rule.Refresh());
  }

  int64_t VBlankLoopPacer::FirstBlankAfterReadyAt(const NanosecondTickCount readyTime) const noexcept
  {
    // A frame is shown at a vertical blank when it is ready the frame margin before it
    const NanosecondTickCount time = readyTime + m_rule.Settings().FrameMargin();
    const int64_t slot = BlankAtOrBefore(time);
    return TimeOfBlank(slot) < time ? slot + 1 : slot;
  }

  int64_t VBlankLoopPacer::Reserve() const noexcept
  {
    // The frames that are ready ahead of the display: with the aim of smoothness, and at one refresh per frame only (at more
    // the display takes a frame before the next one is made, and nothing can wait)
    const PacerSettings& settings = m_rule.Settings();
    return settings.Aim() == PacerAim::Smoothness && m_rule.SwapInterval() == 1 ? int64_t{settings.ReserveFrames()} : 0;
  }

  NanosecondTimeSpan VBlankLoopPacer::ReadyPlace() const noexcept
  {
    // The settings' place, and earlier by what the waits taught: never before the refresh begins
    const int64_t periodNanoseconds = m_rule.Refresh().ToNanosecondTimeSpan().Nanoseconds();
    const int64_t place = (periodNanoseconds * int64_t{m_rule.Settings().ReadyPlacePercent()}) / 100;
    return NanosecondTimeSpan(std::max(place - (int64_t{m_readyPlaceSteps} * (periodNanoseconds / ReadyPlaceStepDivisor)), int64_t{0}));
  }

  NanosecondTimeSpan VBlankLoopPacer::GpuLead() const noexcept
  {
    // From the present to the GPU done with the frame: its time on the frame, as far as it is reported
    return m_frameWork.GpuTime().Value();
  }

  NanosecondTimeSpan VBlankLoopPacer::ShortestLead() const noexcept
  {
    // From a frame's start to the end of its CPU work, at least: the shortest of the last frames, the one that just ended
    // among them, and nothing without one
    const std::size_t count = std::min(m_leadCount, LeadFrames);
    if (count == 0)
    {
      return m_frameEnded ? m_work : NanosecondTimeSpan();
    }
    NanosecondTimeSpan lead = m_frameEnded ? std::min(m_work, m_leads[0]) : m_leads[0];
    for (std::size_t index = 1; index < count; ++index)
    {
      lead = std::min(lead, m_leads[index]);
    }
    return lead;
  }

  NanosecondTimeSpan VBlankLoopPacer::LongestLead() const noexcept
  {
    // The same at most: the longest of the last frames, and no longer than the swap interval's time. A frame that takes longer
    // than that is late whenever it starts, and one such frame is not to make the frames after it start a refresh sooner
    NanosecondTimeSpan lead = m_frameEnded ? m_work : NanosecondTimeSpan();
    for (std::size_t index = 0; index < std::min(m_leadCount, LeadFrames); ++index)
    {
      lead = std::max(lead, m_leads[index]);
    }
    return std::min(lead, m_rule.Refresh().TimeFor(m_rule.SwapInterval()));
  }

  int64_t VBlankLoopPacer::ShownSlotByPresent() const noexcept
  {
    // The vertical blank the frame is for, and a later one once its present says it was not ready for that: the frames before
    // it are shown before it, so it is never an earlier one
    if (!m_hasPresentTime)
    {
      return m_displaySlot;
    }
    const NanosecondTickCount readyTime = m_presentTime + GpuLead();
    int64_t shown = FirstBlankAfterReadyAt(readyTime);
    if (shown > m_displaySlot && m_rule.Settings().Aim() == PacerAim::LowLatency)
    {
      // A frame that missed its vertical blank was ready somewhere in a later refresh. Later in that refresh than a frame is
      // meant to be ready, it is not counted on to have made the next vertical blank, unless a wait says that it did
      const int64_t readySlot = BlankAtOrBefore(readyTime);
      shown = (readyTime - TimeOfBlank(readySlot)) > ReadyPlace() ? std::max(shown, readySlot + 2) : shown;
    }
    return std::max(m_displaySlot, shown);
  }

  int64_t VBlankLoopPacer::ShownSlot() const noexcept
  {
    int64_t shown = ShownSlotByPresent();
    // What the waits say of the frame: it was shown by a vertical blank, and no sooner than one. A fact is taken over what was
    // worked out, both ways, and the frame is never shown before the vertical blank it is for
    if (m_hasShownCeiling)
    {
      shown = std::max(m_displaySlot, std::min(shown, m_shownCeiling));
    }
    if (m_hasShownFloor)
    {
      shown = std::max(shown, m_shownFloor);
    }
    return shown;
  }

  int64_t VBlankLoopPacer::DisplaySlotFor(const NanosecondTickCount startTime, const bool hasPrevious) const noexcept
  {
    // The first vertical blank a frame that starts now can be ready for, if it takes no longer than the shortest of the
    // last frames. One that takes longer and misses it is found out after its present
    const int64_t earliest = FirstBlankAfterReadyAt(startTime + ShortestLead() + GpuLead());
    // The frame before it, its swap interval later (and a pause later, once), or the first it can make when that is too soon
    return hasPrevious ? std::max(ShownSlot() + m_pauseSlots + int64_t{m_rule.SwapInterval()}, earliest) : earliest;
  }

  NanosecondTickCount VBlankLoopPacer::StartTimeFor(const int64_t displaySlot) const noexcept
  {
    // Low latency: ready at its place in the refresh before its vertical blank, and started no sooner than that takes
    return TimeOfBlank(displaySlot - 1) + ReadyPlace() - LongestLead() - GpuLead() - m_rule.Settings().FrameMargin();
  }

  NanosecondTickCount VBlankLoopPacer::PresentTimeFor(const int64_t displaySlot) const noexcept
  {
    if (m_rule.Settings().Aim() == PacerAim::LowLatency)
    {
      // Low latency: presented when it is done, and never so soon that it is ready before the refresh it is to be ready in
      // begins
      return TimeOfBlank(displaySlot - 1) - GpuLead();
    }
    // Smoothness: presented so that it is ready at its place, the reserve's refreshes before the refresh before its vertical blank
    return TimeOfBlank(displaySlot - 1 - Reserve()) + ReadyPlace() - GpuLead();
  }

  void VBlankLoopPacer::ArmStartupPause() noexcept
  {
    m_pausePending = true;
    m_pauseHasFirstFrame = false;
    m_presentTaken = false;
  }

  int64_t VBlankLoopPacer::StartupPauseAt(const NanosecondTickCount cpuStartTime) noexcept
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
    return int64_t{refreshes};
  }

  void VBlankLoopPacer::AddVBlank(const VBlankReading& reading) noexcept
  {
    m_timeline.AddVBlank(reading, m_rule.Refresh());
  }

  FrameStartPlan VBlankLoopPacer::PlanFrame(const NanosecondTickCount now) const noexcept
  {
    // The wait for a present, where the loop has one
    FrameStartPlan plan = m_waitsForPresent ? m_wait.Plan(m_rule.Settings(), m_rule.Refresh(), m_rule.SwapInterval()) : FrameStartPlan();
    // With the aim of smoothness a frame starts at once (when the wait is over), and its present is held
    if (m_rule.Settings().Aim() == PacerAim::LowLatency && !StartsAgainAt(now))
    {
      const NanosecondTickCount start = StartTimeFor(DisplaySlotFor(now, !m_wait.WaitRanOut()));
      if (start > now)
      {
        plan.StartTime = start;
      }
    }
    return plan;
  }

  void VBlankLoopPacer::AddPresentWait(const PresentWaitReport& report) noexcept
  {
    // A wait that ran out, or an answer while the waits are stopped, says nothing of where a frame was shown
    if (!m_wait.AddPresentWait(report, m_rule.Refresh()))
    {
      return;
    }
    // What the wait says of where frames were shown: nothing before there is a frame, across a pause, or of a frame that is not
    // one of the pacer's
    if (!m_hasFrame || StartsAgainAt(report.EndTime) || report.FrameId == 0 || report.FrameId > m_frameId)
    {
      return;
    }
    const int64_t blank = BlankAtOrBefore(report.EndTime);
    const auto newer = static_cast<int64_t>(m_frameId - report.FrameId);
    if (PresentWaitRule::HeldTheLoop(report, m_rule.Refresh()))
    {
      // The wait ended when the display took that frame: it was shown at the vertical blank before the wait's end. The display
      // takes one frame per refresh, so the frame last made is shown as many blanks later as it was made frames later, at the
      // soonest
      const int64_t floor = blank + newer;
      m_shownFloor = m_hasShownFloor ? std::max(m_shownFloor, floor) : floor;
      m_hasShownFloor = true;
    }
    if (newer == 0)
    {
      // The frame last made was shown by the time the wait ended
      m_shownCeiling = blank;
      m_hasShownCeiling = true;
    }
  }

  FrameSchedule VBlankLoopPacer::BeginFrame(const NanosecondTickCount cpuStartTime) noexcept
  {
    const RefreshPeriod period = m_rule.Refresh();
    const bool isLowLatency = m_rule.Settings().Aim() == PacerAim::LowLatency;
    SwapIntervalChange change = SwapIntervalChange::Unchanged;
    const bool isFirstFrame = m_frameId == 0;
    // No vertical blank reading yet: the frame's start is taken as one
    m_timeline.StartAt(cpuStartTime);

    // A frame that the pacer's own wait held until it ran out is not judged: the display is not taking the window's frames, so
    // when the frame before it was shown, or whether, is not known, and this start is late by the pacer's doing
    const bool startsAgain = StartsAgainAt(cpuStartTime);
    const bool hasPrevious = !startsAgain && !m_wait.WaitRanOut();
    // Where a frame has to be ready is learnt from a display that shows the window's frames. Around a wait that ran out and
    // while the waits are stopped it does not: a frame shown later then is counted and teaches nothing, and a place that was
    // moved in the frames just before is moved back
    if (m_wait.Disturbed())
    {
      m_readyPlaceSteps -= (m_framesSincePlaceStep < ShownLaterFramesApart && m_readyPlaceSteps > 0) ? 1u : 0u;
      m_framesSincePlaceStep = ShownLaterFramesApart;
      m_framesSinceDisturbed = 0;
      m_shownLaterCount = 0;
    }
    else
    {
      m_framesSinceDisturbed = std::min(m_framesSinceDisturbed, ShownLaterFramesApart - 1u) + 1u;
    }
    m_framesSincePlaceStep = std::min(m_framesSincePlaceStep, ShownLaterFramesApart - 1u) + 1u;
    int64_t previousShown = 0;
    if (hasPrevious)
    {
      // The previous frame: the vertical blank it was shown at against the one it was for, and its work against its swap
      // interval's time. Without an EndFrame its work is not known, and the time to this start says nothing about it
      previousShown = ShownSlot();
      // A frame that was ready in time by the pacer's reckoning and was shown later. Once is a refresh the display lost. Again
      // within a few frames, the display takes a frame sooner before a vertical blank than the pacer has it ready: the place
      // is moved earlier
      const int64_t shownLater = std::max(previousShown - ShownSlotByPresent(), int64_t{0});
      m_shownLaterByWaits += static_cast<uint64_t>(shownLater);
      const bool teaches = shownLater > 0 && m_framesSinceDisturbed >= ShownLaterFramesApart;
      m_framesSinceShownLater = teaches ? 0u : std::min(m_framesSinceShownLater + 1u, ShownLaterFramesApart);
      m_shownLaterCount = teaches ? m_shownLaterCount + 1u : (m_framesSinceShownLater >= ShownLaterFramesApart ? 0u : m_shownLaterCount);
      if (m_shownLaterCount >= ShownLaterToMovePlace && ReadyPlace() > NanosecondTimeSpan())
      {
        ++m_readyPlaceSteps;
        m_shownLaterCount = 0;
        m_framesSincePlaceStep = 0;
      }
      const int64_t lost = previousShown - m_displaySlot;
      const NanosecondTimeSpan cpuWork = m_frameEnded ? m_work : MarkerValue::Duration(cpuStartTime - m_startTime).ToNanosecondTimeSpan();
      const NanosecondTimeSpan work = m_frameEnded ? m_frameWork.WorkOf(cpuWork, m_rule.Settings().MaxFramesInFlight()) : cpuWork;
      const bool late = lost > 0 || m_startedLate || (m_frameEnded && work > period.TimeFor(m_swapInterval));
      // How long before the time it was given this frame began (low latency: it is given none with the aim of smoothness)
      const NanosecondTimeSpan startAhead =
        isLowLatency ? StartTimeFor(previousShown + m_pauseSlots + int64_t{m_rule.SwapInterval()}) - cpuStartTime : NanosecondTimeSpan();
      change = m_rule.AddFrame(TimeOfBlank(previousShown) - NanosecondTickCount(), work, late, startAhead);
      m_refreshesBehindClock += static_cast<uint64_t>(lost);
      if (m_frameEnded)
      {
        m_leads[m_leadCount % LeadFrames] = m_work;
        ++m_leadCount;
      }
    }
    else
    {
      m_shownLaterCount = 0;
      if (startsAgain)
      {
        // The frames start again here: nothing was measured, the frame window starts empty and the swap interval stays
        m_rule.Clear();
        m_pauseSlots = 0;
      }
    }

    const uint32_t swapInterval = m_rule.SwapInterval();
    // The vertical blank this frame is for. A frame that starts too late for the one its swap interval gives is for the first
    // it can make: known now, so it is in the frame
    const int64_t displaySlot = DisplaySlotFor(cpuStartTime, hasPrevious);
    m_startedLate = hasPrevious && displaySlot > previousShown + m_pauseSlots + int64_t{swapInterval};

    // The animation time: the first frame's is where the pacer starts, every other frame's is the refreshes from the frame
    // before it to the vertical blank it is for. A refresh the frame before it lost after its animation time was fixed is not
    // caught up with: this frame's step counts from where that frame was shown
    if (!isFirstFrame)
    {
      const int64_t refreshes = hasPrevious ? displaySlot - previousShown : int64_t{swapInterval};
      m_animationTime.Add(static_cast<uint32_t>(std::clamp(refreshes, int64_t{1}, MaxStepRefreshes)), period);
    }
    const NanosecondTimeSpan animationTime = m_animationTime.ToNanosecondTimeSpan();

    m_swapInterval = swapInterval;
    m_displaySlot = displaySlot;
    m_startTime = cpuStartTime;
    m_work = NanosecondTimeSpan();
    m_frameOpen = true;
    m_frameEnded = false;
    m_hasPresentTime = false;
    m_hasShownFloor = false;
    m_hasShownCeiling = false;
    m_hasFrame = true;
    m_wait.BeginFrame();
    ++m_frameId;
    m_frameWork.AddFrameStart(m_frameId, cpuStartTime);
    // The one pause after start-up (low latency, no wait for a present): the frame after this one is for a vertical blank
    // that many later
    m_pauseSlots = StartupPauseAt(cpuStartTime);

    FrameSchedule schedule;
    schedule.FrameId = m_frameId;
    schedule.SwapInterval = swapInterval;
    schedule.AnimationTime = animationTime;
    schedule.AnimationStep = NanosecondTimeSpan(animationTime.Nanoseconds() - m_lastAnimationTime.Nanoseconds());
    schedule.IntendedDisplayTime = TimeOfBlank(displaySlot);
    // When the frame after this one starts: at its time with the aim of low latency, and when this frame's present is made
    // with the aim of smoothness, as it starts when the wait after that is over
    schedule.NextFrameStartTime = isLowLatency ? StartTimeFor(displaySlot + m_pauseSlots + int64_t{swapInterval}) : PresentTimeFor(displaySlot);
    schedule.TargetFrameTime = MarkerValue::FrameTime(period.TimeFor(swapInterval));
    schedule.PreferredFrameTime = MarkerValue::FrameTime(period.TimeFor(m_rule.PreferredSwapInterval()));
    schedule.Change = change;
    m_lastAnimationTime = animationTime;
    return schedule;
  }

  PresentPlan VBlankLoopPacer::EndFrame(const NanosecondTickCount workDoneTime) noexcept
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
    // Smoothness: made early, and the present waits for the time that has the frame ready at its place. Low latency: presented
    // at once, unless it is done before its refresh begins
    const NanosecondTickCount presentTime = PresentTimeFor(m_displaySlot);
    if (presentTime > workDoneTime)
    {
      plan.PresentTime = presentTime;
    }
    // When the present is made, as far as it is known here: AddPresent says when it was
    m_presentTime = plan.WaitsForPresentTime() ? plan.PresentTime : workDoneTime;
    m_hasPresentTime = true;
    return plan;
  }

  NanosecondTimeSpan32 VBlankLoopPacer::CpuBusyAt(const NanosecondTickCount now) const noexcept
  {
    return m_frameOpen ? MarkerValue::Duration(now - m_startTime) : NanosecondTimeSpan32();
  }

  void VBlankLoopPacer::AddPresent(const PresentReport& report) noexcept
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
    m_wait.AddPresent(report);
  }

  void VBlankLoopPacer::AddGpuWork(const GpuWorkReport& report) noexcept
  {
    m_frameWork.AddGpuWork(report, m_rule.Settings().FrameMargin());
  }

  void VBlankLoopPacer::ForgetPresents() noexcept
  {
    ArmStartupPause();
    m_wait.ForgetPresents();
  }

  void VBlankLoopPacer::SetRefreshPeriod(const RefreshPeriod period) noexcept
  {
    if (period != m_rule.Refresh())
    {
      m_rule.SetRefreshPeriod(period);
      m_hasFrame = false;
      m_timeline.Clear();
      m_readyPlaceSteps = 0;
    }
  }

  void VBlankLoopPacer::SetSettings(const PacerSettings& settings)
  {
    if (settings != m_rule.Settings())
    {
      const bool samePeriod = settings.Refresh() == m_rule.Refresh();
      m_rule.SetSettings(settings);
      m_hasFrame = false;
      m_readyPlaceSteps = 0;
      if (!samePeriod)
      {
        m_timeline.Clear();
      }
    }
  }

  void VBlankLoopPacer::Reset() noexcept
  {
    m_rule.Reset(m_rule.PreferredSwapInterval());
    m_frameWork.Clear();
    m_hasFrame = false;
    m_frameOpen = false;
    m_frameEnded = false;
    m_hasPresentTime = false;
    m_hasShownFloor = false;
    m_hasShownCeiling = false;
    m_leadCount = 0;
    m_readyPlaceSteps = 0;
    m_shownLaterCount = 0;
    m_framesSinceDisturbed = ShownLaterFramesApart;
    m_framesSincePlaceStep = ShownLaterFramesApart;
    m_pauseSlots = 0;
    ArmStartupPause();
    m_wait.Reset();
  }
}
