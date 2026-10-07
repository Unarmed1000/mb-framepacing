// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The pacer of vertical blank times with a wait for a present (sdk/doc/pacer-design.md): VBlankPeriodOnlyPacer's
// frames, each for one vertical blank of the display, and TimerWaitForPresentPacer's wait, which here also says which vertical
// blank a frame was shown at.
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <mb/framepacing/pacer/tier/VBlankWaitForPresentPacer.hpp>
#include <algorithm>
#include "../detail/MarkerValue.hpp"

namespace MB::FramePacing::Pacer
{
  namespace
  {
    //! A reading is off where the readings before it put the vertical blanks when it is more than the period divided by this
    constexpr int64_t JumpDivisor = 8;
    //! The most refreshes an animation step is counted in: far more than a frame window holds
    constexpr int64_t MaxStepRefreshes = int64_t{1} << 20;
    //! A wait held the loop when it took this share of a refresh period or more: one in this many
    constexpr int64_t BlockedDivisor = 8;
    //! The place a frame is to be ready at is moved earlier by this share of a refresh period at a time: one in this many
    constexpr int64_t ReadyPlaceStepDivisor = 8;
    //! The frames shown later than worked out that move it: one by itself is a refresh the display lost
    constexpr uint32_t ShownLaterToMovePlace = 2;
    //! They count together while no more than this many frames pass between them: what a wait says of a frame comes a frame or
    //! two after it
    constexpr uint32_t ShownLaterFramesApart = 8;
  }

  VBlankWaitForPresentPacer::VBlankWaitForPresentPacer(const PacerSettings& settings)
    : m_rule(settings)
  {
  }

  bool VBlankWaitForPresentPacer::StartsAgainAt(const NanosecondTickCount time) const noexcept
  {
    if (!m_hasFrame)
    {
      return true;
    }
    // A pause the pacer did not ask for, or a clock that went back: nothing is measured across it
    const NanosecondTimeSpan gap = time - m_startTime;
    const NanosecondTimeSpan reach = std::max(m_rule.Settings().FrameWindowLength(), m_rule.Refresh().TimeFor(int64_t{2} * m_swapInterval));
    return gap < NanosecondTimeSpan() || gap > reach;
  }

  NanosecondTickCount VBlankWaitForPresentPacer::TimeOfBlank(const int64_t slot) const noexcept
  {
    const int64_t steps = slot - m_anchorSlot;
    return steps >= 0 ? m_anchorTime + m_rule.Refresh().TimeFor(steps) : m_anchorTime - m_rule.Refresh().TimeFor(-steps);
  }

  int64_t VBlankWaitForPresentPacer::BlankAtOrBefore(const NanosecondTickCount time) const noexcept
  {
    const int64_t nanoseconds = (time - m_anchorTime).Nanoseconds();
    return nanoseconds >= 0 ? m_anchorSlot + m_rule.Refresh().FloorRefreshes(NanosecondTimeSpan(nanoseconds))
                            : m_anchorSlot - m_rule.Refresh().RefreshesToFit(NanosecondTimeSpan(-nanoseconds));
  }

  int64_t VBlankWaitForPresentPacer::FirstBlankAfterReadyAt(const NanosecondTickCount readyTime) const noexcept
  {
    // A frame is shown at a vertical blank when it is ready the frame margin before it
    const NanosecondTickCount time = readyTime + m_rule.Settings().FrameMargin();
    const int64_t slot = BlankAtOrBefore(time);
    return TimeOfBlank(slot) < time ? slot + 1 : slot;
  }

  int64_t VBlankWaitForPresentPacer::Reserve() const noexcept
  {
    // The frames that are ready ahead of the display: with the aim of smoothness, and at one refresh per frame only (at more
    // the display takes a frame before the next one is made, and nothing can wait)
    const PacerSettings& settings = m_rule.Settings();
    return settings.Aim() == PacerAim::Smoothness && m_rule.SwapInterval() == 1 ? int64_t{settings.ReserveFrames()} : 0;
  }

  NanosecondTimeSpan VBlankWaitForPresentPacer::ReadyPlace() const noexcept
  {
    // The settings' place, and earlier by what the waits taught: never before the refresh begins
    const int64_t periodNanoseconds = m_rule.Refresh().ToNanosecondTimeSpan().Nanoseconds();
    const int64_t place = (periodNanoseconds * int64_t{m_rule.Settings().ReadyPlacePercent()}) / 100;
    return NanosecondTimeSpan(std::max(place - (int64_t{m_readyPlaceSteps} * (periodNanoseconds / ReadyPlaceStepDivisor)), int64_t{0}));
  }

  NanosecondTimeSpan VBlankWaitForPresentPacer::GpuLead() const noexcept
  {
    // From the present to the GPU done with the frame: its time on the frame, as far as it is reported
    return m_frameWork.GpuTime().Value();
  }

  NanosecondTimeSpan VBlankWaitForPresentPacer::ShortestLead() const noexcept
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

  NanosecondTimeSpan VBlankWaitForPresentPacer::LongestLead() const noexcept
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

  int64_t VBlankWaitForPresentPacer::ShownSlotByPresent() const noexcept
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

  int64_t VBlankWaitForPresentPacer::ShownSlot() const noexcept
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

  int64_t VBlankWaitForPresentPacer::DisplaySlotFor(const NanosecondTickCount startTime, const bool hasPrevious) const noexcept
  {
    // The first vertical blank a frame that starts now can be ready for, if it takes no longer than the shortest of the
    // last frames. One that takes longer and misses it is found out after its present
    const int64_t earliest = FirstBlankAfterReadyAt(startTime + ShortestLead() + GpuLead());
    // The frame before it, its swap interval later, or the first it can make when that is too soon
    return hasPrevious ? std::max(ShownSlot() + int64_t{m_rule.SwapInterval()}, earliest) : earliest;
  }

  NanosecondTickCount VBlankWaitForPresentPacer::StartTimeFor(const int64_t displaySlot) const noexcept
  {
    // Low latency: ready at its place in the refresh before its vertical blank, and started no sooner than that takes
    return TimeOfBlank(displaySlot - 1) + ReadyPlace() - LongestLead() - GpuLead() - m_rule.Settings().FrameMargin();
  }

  NanosecondTickCount VBlankWaitForPresentPacer::PresentTimeFor(const int64_t displaySlot) const noexcept
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

  void VBlankWaitForPresentPacer::AddVBlank(const VBlankReading& reading) noexcept
  {
    if (m_hasReading && reading.ReadTime < m_lastReadTime)
    {
      // Read before the one the pacer has
      return;
    }
    if (m_hasAnchor)
    {
      // The vertical blank it is, by where the pacer has them: the frames keep the blanks they are for
      int64_t slot = BlankAtOrBefore(reading.VBlankTime);
      const int64_t periodNanoseconds = m_rule.Refresh().ToNanosecondTimeSpan().Nanoseconds();
      int64_t offNanoseconds = (reading.VBlankTime - TimeOfBlank(slot)).Nanoseconds();
      if ((offNanoseconds * 2) >= periodNanoseconds)
      {
        ++slot;
        offNanoseconds = (TimeOfBlank(slot) - reading.VBlankTime).Nanoseconds();
      }
      m_vblankJumps += m_hasReading && offNanoseconds > (periodNanoseconds / JumpDivisor) ? 1u : 0u;
      m_anchorSlot = slot;
    }
    m_anchorTime = reading.VBlankTime;
    m_hasAnchor = true;
    m_hasReading = true;
    m_lastReadTime = reading.ReadTime;
  }

  FrameStartPlan VBlankWaitForPresentPacer::PlanFrame(const NanosecondTickCount now) const noexcept
  {
    FrameStartPlan plan;
    // The present to wait for: the one WaitingPresents back from the frame that is about to be made. While the waits run out
    // nothing is waited for: once in a number of frames the plan asks after a present that many frames older, which has had
    // the time a wait would give (asking is not free everywhere)
    const bool stopped = PresentWaitsStopped();
    const uint64_t back = (uint64_t{m_rule.Settings().WaitingPresents()} - 1u) + (stopped ? m_rule.Settings().PresentWaitSwapIntervals() : 0u);
    const bool asks = !stopped || m_framesSinceAsk >= FramesBetweenAsks;
    if (asks && !m_waitReported && m_lastAcceptedId > back && (m_lastAcceptedId - back) >= m_oldestWaitableId &&
        (m_lastAcceptedId - back) != m_waitedForId && (!stopped || (m_lastAcceptedId - back) >= m_runOutFromId))
    {
      plan.WaitForPresentFrameId = m_lastAcceptedId - back;
      // As long as a few of the frame's own swap intervals: a present that is never shown holds the loop no longer
      const int64_t refreshes = int64_t{m_rule.Settings().PresentWaitSwapIntervals()} * m_rule.SwapInterval();
      plan.WaitForPresentTimeout = stopped ? NanosecondTimeDuration() : NanosecondTimeDuration(m_rule.Refresh().TimeFor(refreshes));
    }
    // With the aim of smoothness a frame starts when the wait is over, and its present is held
    if (m_rule.Settings().Aim() == PacerAim::LowLatency && !StartsAgainAt(now))
    {
      const NanosecondTickCount start = StartTimeFor(DisplaySlotFor(now, !m_waitRanOut));
      if (start > now)
      {
        plan.StartTime = start;
      }
    }
    return plan;
  }

  void VBlankWaitForPresentPacer::AddPresentWait(const PresentWaitReport& report) noexcept
  {
    m_waitedForId = report.FrameId;
    m_waitReported = true;
    const bool heldTheLoop = report.Blocked().Nanoseconds() >= (m_rule.Refresh().ToNanosecondTimeSpan().Nanoseconds() / BlockedDivisor);
    if (PresentWaitsStopped())
    {
      // The answer to what the plan asked. Answers in a row that say shown end the stop: one by itself can be a frame of a
      // window that is still covered, shown in passing. An ask that held the loop is the pacer's doing as a wait is
      m_framesSinceAsk = 0;
      m_presentWaitTimeouts += report.Shown ? 0u : 1u;
      m_shownAsks = report.Shown ? m_shownAsks + 1u : 0u;
      m_waitRanOut = m_waitRanOut || heldTheLoop;
      if (m_shownAsks >= AsksShownToWait)
      {
        m_waitsRunOut = 0;
      }
      return;
    }
    if (!report.Shown)
    {
      ++m_presentWaitTimeouts;
      m_runOutFromId = m_waitsRunOut == 0 ? report.FrameId : m_runOutFromId;
      m_waitsRunOut = std::min(m_waitsRunOut + 1u, WaitsRunOutToStop);
      m_framesSinceAsk = 0;
      m_shownAsks = 0;
      // The frame that starts after a wait that held the loop until it ran out starts late because the pacer asked for the wait
      m_waitRanOut = m_waitRanOut || heldTheLoop;
      return;
    }
    m_waitsRunOut = 0;
    // What the wait says of where frames were shown: nothing before there is a frame, across a pause, or of a frame that is not
    // one of the pacer's
    if (!m_hasFrame || StartsAgainAt(report.EndTime) || report.FrameId == 0 || report.FrameId > m_frameId)
    {
      return;
    }
    const int64_t blank = BlankAtOrBefore(report.EndTime);
    const auto newer = static_cast<int64_t>(m_frameId - report.FrameId);
    if (heldTheLoop)
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

  FrameSchedule VBlankWaitForPresentPacer::BeginFrame(const NanosecondTickCount cpuStartTime) noexcept
  {
    const RefreshPeriod period = m_rule.Refresh();
    const bool isLowLatency = m_rule.Settings().Aim() == PacerAim::LowLatency;
    SwapIntervalChange change = SwapIntervalChange::Unchanged;
    const bool isFirstFrame = m_frameId == 0;
    if (!m_hasAnchor)
    {
      // No vertical blank reading yet: the frame's start is taken as one
      m_anchorTime = cpuStartTime;
      m_anchorSlot = 0;
      m_hasAnchor = true;
    }

    // A frame that the pacer's own wait held until it ran out is not judged: the display is not taking the window's frames, so
    // when the frame before it was shown, or whether, is not known, and this start is late by the pacer's doing
    const bool startsAgain = StartsAgainAt(cpuStartTime);
    const bool hasPrevious = !startsAgain && !m_waitRanOut;
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
      m_framesSinceShownLater = shownLater > 0 ? 0u : std::min(m_framesSinceShownLater + 1u, ShownLaterFramesApart);
      m_shownLaterCount = shownLater > 0 ? m_shownLaterCount + 1u : (m_framesSinceShownLater >= ShownLaterFramesApart ? 0u : m_shownLaterCount);
      if (m_shownLaterCount >= ShownLaterToMovePlace && ReadyPlace() > NanosecondTimeSpan())
      {
        ++m_readyPlaceSteps;
        m_shownLaterCount = 0;
      }
      const int64_t lost = previousShown - m_displaySlot;
      const NanosecondTimeSpan cpuWork = m_frameEnded ? m_work : MarkerValue::Duration(cpuStartTime - m_startTime).ToNanosecondTimeSpan();
      const NanosecondTimeSpan work = m_frameEnded ? m_frameWork.WorkOf(cpuWork, m_rule.Settings().MaxFramesInFlight()) : cpuWork;
      const bool late = lost > 0 || m_startedLate || (m_frameEnded && work > period.TimeFor(m_swapInterval));
      // How long before the time it was given this frame began (low latency: it is given none with the aim of smoothness)
      const NanosecondTimeSpan startAhead =
        isLowLatency ? StartTimeFor(previousShown + int64_t{m_rule.SwapInterval()}) - cpuStartTime : NanosecondTimeSpan();
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
      }
    }

    const uint32_t swapInterval = m_rule.SwapInterval();
    // The vertical blank this frame is for. A frame that starts too late for the one its swap interval gives is for the first
    // it can make: known now, so it is in the frame
    const int64_t displaySlot = DisplaySlotFor(cpuStartTime, hasPrevious);
    m_startedLate = hasPrevious && displaySlot > previousShown + int64_t{swapInterval};

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
    m_waitRanOut = false;
    m_waitReported = false;
    m_framesSinceAsk = PresentWaitsStopped() ? std::min(m_framesSinceAsk + 1u, FramesBetweenAsks) : 0u;
    ++m_frameId;
    m_frameWork.AddFrameStart(m_frameId, cpuStartTime);

    FrameSchedule schedule;
    schedule.FrameId = m_frameId;
    schedule.SwapInterval = swapInterval;
    schedule.AnimationTime = animationTime;
    schedule.AnimationStep = NanosecondTimeSpan(animationTime.Nanoseconds() - m_lastAnimationTime.Nanoseconds());
    schedule.IntendedDisplayTime = TimeOfBlank(displaySlot);
    // When the frame after this one starts: at its time with the aim of low latency, and when this frame's present is made
    // with the aim of smoothness, as it starts when the wait after that is over
    schedule.NextFrameStartTime = isLowLatency ? StartTimeFor(displaySlot + int64_t{swapInterval}) : PresentTimeFor(displaySlot);
    schedule.TargetFrameTime = MarkerValue::FrameTime(period.TimeFor(swapInterval));
    schedule.PreferredFrameTime = MarkerValue::FrameTime(period.TimeFor(m_rule.PreferredSwapInterval()));
    schedule.Change = change;
    m_lastAnimationTime = animationTime;
    return schedule;
  }

  PresentPlan VBlankWaitForPresentPacer::EndFrame(const NanosecondTickCount workDoneTime) noexcept
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

  NanosecondTimeSpan32 VBlankWaitForPresentPacer::CpuBusyAt(const NanosecondTickCount now) const noexcept
  {
    return m_frameOpen ? MarkerValue::Duration(now - m_startTime) : NanosecondTimeSpan32();
  }

  void VBlankWaitForPresentPacer::AddPresent(const PresentReport& report) noexcept
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

  void VBlankWaitForPresentPacer::AddGpuWork(const GpuWorkReport& report) noexcept
  {
    m_frameWork.AddGpuWork(report, m_rule.Settings().FrameMargin());
  }

  void VBlankWaitForPresentPacer::ForgetPresents() noexcept
  {
    m_oldestWaitableId = m_lastAcceptedId + 1u;
    // And with them the waits that ran out: a new swap chain's presents are waited for again
    m_waitsRunOut = 0;
    m_shownAsks = 0;
  }

  void VBlankWaitForPresentPacer::SetRefreshPeriod(const RefreshPeriod period) noexcept
  {
    if (period != m_rule.Refresh())
    {
      m_rule.SetRefreshPeriod(period);
      m_hasFrame = false;
      m_hasAnchor = false;
      m_hasReading = false;
      m_readyPlaceSteps = 0;
    }
  }

  void VBlankWaitForPresentPacer::SetSettings(const PacerSettings& settings)
  {
    if (settings != m_rule.Settings())
    {
      const bool samePeriod = settings.Refresh() == m_rule.Refresh();
      m_rule.SetSettings(settings);
      m_hasFrame = false;
      m_readyPlaceSteps = 0;
      m_hasAnchor = m_hasAnchor && samePeriod;
      m_hasReading = m_hasReading && samePeriod;
    }
  }

  void VBlankWaitForPresentPacer::Reset() noexcept
  {
    m_rule.Reset(m_rule.PreferredSwapInterval());
    m_frameWork.Clear();
    m_hasFrame = false;
    m_frameOpen = false;
    m_frameEnded = false;
    m_hasPresentTime = false;
    m_hasShownFloor = false;
    m_hasShownCeiling = false;
    m_waitRanOut = false;
    m_leadCount = 0;
    m_readyPlaceSteps = 0;
    m_shownLaterCount = 0;
    ForgetPresents();
  }
}
