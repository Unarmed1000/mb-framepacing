// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The pacers of vertical blank times (sdk/doc/pacer-design.md): every frame is for one vertical blank of the
// display, made early and its present held (smoothness) or its start held so that it is ready just in time (low latency).
// With a wait for a present the loop is held until the display took an earlier frame, and the wait says which vertical blank
// a frame was shown at; without one there is a pause after start-up.
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/frame/SystemWaitKind.hpp>
#include <mb/framepacing/pacer/placement/DisplayPlacementUtil.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <mb/framepacing/pacer/tier/VBlankLoopPacer.hpp>
#include <algorithm>

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
    //! A place that was tried one step later holds when no frame was shown later in this many frames after the try
    constexpr uint32_t PlaceTrialFrames = 16;
    //! The stretch without a frame shown later before a try is the frame window's length, and twice as long after each try
    //! that was taken back: doubled no more often than this
    constexpr uint32_t MaxPlaceTryDoublings = 10;
    //! The display's side held the loop when its waits took this share of a refresh period or more: one in this many
    constexpr int64_t HeldDivisor = 8;
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
    // The frames' vertical blanks are the display's without the ones a start was held over
    return m_timeline.TimeOfBlank(slot + m_heldSlots, m_rule.Refresh());
  }

  int64_t VBlankLoopPacer::BlankAtOrBefore(const NanosecondTickCount time) const noexcept
  {
    return m_timeline.BlankAtOrBefore(time, m_rule.Refresh()) - m_heldSlots;
  }

  int64_t VBlankLoopPacer::FirstBlankAfterReadyAt(const NanosecondTickCount readyTime) const noexcept
  {
    // A frame is shown at a vertical blank when it is ready the frame margin before it
    const NanosecondTickCount time = readyTime + m_rule.Settings().FrameMargin();
    const int64_t slot = BlankAtOrBefore(time);
    return TimeOfBlank(slot) < time ? slot + 1 : slot;
  }

  uint32_t VBlankLoopPacer::FramesInFlightNow() const noexcept
  {
    // The frames in flight a frame's work is judged by: what the pacer's own wait for the GPU's work makes them, where
    // that wait is made, and else what the application says it lets be in flight
    const PacerSettings& settings = m_rule.Settings();
    return (!m_waitsForPresent && m_waitsForGpuWork) ? GpuWaitRule::FramesInFlight(settings) : settings.MaxFramesInFlight();
  }

  void VBlankLoopPacer::AddDisplayHeld(const NanosecondTimeDuration held) noexcept
  {
    // Added up over the waits before one frame: never more than the longest refresh period a time
    const int64_t longest = RefreshPeriod::MaxPeriod.Nanoseconds();
    m_displayHeld = NanosecondTimeSpan(std::min(m_displayHeld.Nanoseconds() + std::min(held.Nanoseconds(), longest), longest * 64));
  }

  NanosecondTimeDuration VBlankLoopPacer::WithoutGpuWork(const NanosecondTimeDuration blocked) const noexcept
  {
    // A wait for the GPU's work on a frame, or for a frame slot, holds the loop while the GPU works and also while a frame
    // that is done, or not begun, waits for the display's side. What of it was the GPU's is not in the wait: it is taken
    // to be no more than the GPU's time on a frame as it was last reported, and the frame margin. Without a reported GPU
    // time the whole wait is the GPU's: a loop the GPU limits is late, and is not to be read as held by the display
    if (!m_frameWork.HasGpuTime())
    {
      return {};
    }
    const int64_t gpuNanoseconds = m_frameWork.GpuTime().Nanoseconds() + m_rule.Settings().FrameMargin().Nanoseconds();
    return NanosecondTimeDuration::FromNanoseconds(std::max(blocked.Nanoseconds() - gpuNanoseconds, int64_t{0}));
  }

  void VBlankLoopPacer::AddGpuWait(const GpuWaitReport& report) noexcept
  {
    m_gpuWait.AddGpuWait(report, m_rule.Refresh());
    AddDisplayHeld(WithoutGpuWork(report.Blocked()));
  }

  void VBlankLoopPacer::AddSystemWait(const SystemWaitReport& report) noexcept
  {
    AddDisplayHeld(report.Kind == SystemWaitKind::FrameSlot ? WithoutGpuWork(report.Blocked()) : report.Blocked());
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
      // begins. Ready at the vertical blank itself is too soon as well: a display may take a frame for the blank it is
      // ready at, so it is the frame margin into the refresh
      return TimeOfBlank(displaySlot - 1) + m_rule.Settings().FrameMargin() - GpuLead();
    }
    // Smoothness: presented so that it is ready at its place, the reserve's refreshes before the refresh before its vertical blank
    return TimeOfBlank(displaySlot - 1 - Reserve()) + ReadyPlace() - GpuLead();
  }

  bool VBlankLoopPacer::TriesTheReadyPlaceAt(const NanosecondTickCount time) const noexcept
  {
    // A step to take back, a display that shows the window's frames, and long enough without a frame shown later: the frame
    // window's length, doubled for each try that was taken back
    if (m_readyPlaceSteps == 0 || m_framesSinceDisturbed < ShownLaterFramesApart || m_shownLaterCount != 0)
    {
      return false;
    }
    const int64_t quiet = m_rule.Settings().FrameWindowLength().Nanoseconds() << m_placeTryDoublings;
    return (time - m_placeQuietSince).Nanoseconds() >= quiet;
  }

  void VBlankLoopPacer::ForgetReadyPlace() noexcept
  {
    m_readyPlaceSteps = 0;
    m_shownLaterCount = 0;
    m_placeOnTrial = false;
    m_placeTryDoublings = 0;
    m_hasPlaceQuietSince = false;
  }

  void VBlankLoopPacer::ArmStartupPause() noexcept
  {
    m_pausePending = true;
    m_pauseHeldByWait = false;
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
    if (!m_waitsForPresent && m_waitsForGpuWork)
    {
      // Without a wait for a present: the wait for the GPU's work on an earlier frame, where the application can make it
      plan = m_gpuWait.Plan(m_rule.Settings(), m_rule.Refresh(), m_rule.SwapInterval());
    }
    if (m_takenOver)
    {
      // The first frame after another pacer placed the frames: it starts when the frame before it said the next one would
      if (m_takeOverStartTime > now)
      {
        plan.StartTime = m_takeOverStartTime;
      }
      return plan;
    }
    if (StartsAgainAt(now))
    {
      return plan;
    }
    if (m_rule.Settings().Aim() == PacerAim::LowLatency)
    {
      const NanosecondTickCount start = StartTimeFor(DisplaySlotFor(now, !m_wait.WaitRanOut()));
      if (start > now)
      {
        plan.StartTime = start;
      }
    }
    else if (m_presentTiming == PresentTiming::AtTime && m_nextFrameStartTime > now)
    {
      // Smoothness with a time on the present: the frame before this one was presented when it was done, and the display's side
      // holds it. What would have held its present holds this frame's start: the loop makes its frames as far ahead of the
      // display as without one, whatever else holds it (a wait for a present does, later, while the window is shown)
      plan.StartTime = m_nextFrameStartTime;
    }
    // Smoothness without such a time: a frame starts at once (when the wait is over), and its present is held
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
    if (!m_hasFrame || m_takenOver || StartsAgainAt(report.EndTime) || report.FrameId == 0 || report.FrameId > m_frameId)
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
    // Nor is a frame that another pacer placed: it was for no vertical blank of this one
    const bool hasPrevious = !startsAgain && !m_wait.WaitRanOut() && !m_takenOver;
    // Where a frame has to be ready is learnt from a display that shows the window's frames. Around a wait that ran out and
    // while the waits are stopped it does not: a frame shown later then is counted and teaches nothing, and a place that was
    // moved in the frames just before is moved back
    if (m_wait.Disturbed())
    {
      // A try can not be judged then either: the place is where it was before the try, which is not counted as taken back
      m_readyPlaceSteps += m_placeOnTrial ? 1u : 0u;
      m_placeOnTrial = false;
      m_hasPlaceQuietSince = false;
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
    if (!m_hasPlaceQuietSince)
    {
      m_placeQuietSince = cpuStartTime;
      m_hasPlaceQuietSince = true;
    }
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
      if (m_placeOnTrial && teaches)
      {
        // The place was one step later on trial, and a frame was shown later: back at once, and the next try comes after
        // twice as long
        ++m_readyPlaceSteps;
        m_placeOnTrial = false;
        ++m_readyPlaceTriesTakenBack;
        m_placeTryDoublings = std::min(m_placeTryDoublings + 1u, MaxPlaceTryDoublings);
        m_shownLaterCount = 0;
        m_framesSinceShownLater = ShownLaterFramesApart;
        m_placeQuietSince = cpuStartTime;
      }
      else
      {
        m_framesSinceShownLater = teaches ? 0u : std::min(m_framesSinceShownLater + 1u, ShownLaterFramesApart);
        m_shownLaterCount = teaches ? m_shownLaterCount + 1u : (m_framesSinceShownLater >= ShownLaterFramesApart ? 0u : m_shownLaterCount);
        if (m_shownLaterCount >= ShownLaterToMovePlace && ReadyPlace() > NanosecondTimeSpan())
        {
          ++m_readyPlaceSteps;
          m_shownLaterCount = 0;
          m_framesSincePlaceStep = 0;
        }
        if (teaches)
        {
          m_placeQuietSince = cpuStartTime;
        }
        if (m_placeOnTrial)
        {
          // The try holds once enough frames after it were shown where they were worked out to be: the next one, if there
          // is a step left to take back, comes after a frame window's length again
          ++m_placeTrialFrames;
          if (m_placeTrialFrames >= PlaceTrialFrames)
          {
            m_placeOnTrial = false;
            m_placeTryDoublings = 0;
            m_placeQuietSince = cpuStartTime;
          }
        }
        else if (TriesTheReadyPlaceAt(cpuStartTime))
        {
          // The place goes back: what moved it earlier may have passed (a swap chain's first frames, a display that was
          // busy with something else), so it is tried one step later
          --m_readyPlaceSteps;
          m_placeOnTrial = true;
          m_placeTrialFrames = 0;
          ++m_readyPlaceTries;
        }
      }
      const int64_t lost = previousShown - m_displaySlot;
      const NanosecondTimeSpan cpuWork = m_frameEnded ? m_work : NanosecondTimeDuration(cpuStartTime - m_startTime).Value();
      const NanosecondTimeSpan work = m_frameEnded ? m_frameWork.WorkOf(cpuWork, FramesInFlightNow()) : cpuWork;
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
    // After another pacer placed the frames, those it made ahead of the display are still on their way and are shown first:
    // this frame is for the vertical blank after theirs, and none is made back to back with it to have them again. And it
    // is for no vertical blank sooner than its swap interval after the one the last frame was ready for, by its present
    int64_t displaySlot = DisplaySlotFor(cpuStartTime, hasPrevious) + (m_takenOver ? Reserve() : 0);
    if (m_takenOver && m_hasTakeOverPresent)
    {
      displaySlot = std::max(displaySlot, FirstBlankAfterReadyAt(m_takeOverPresentTime + GpuLead()) + int64_t{swapInterval});
    }
    m_hasTakeOverPresent = false;
    const int64_t dueSlot = previousShown + m_pauseSlots + int64_t{swapInterval};
    if (hasPrevious && displaySlot > dueSlot && m_displayHeld.Nanoseconds() >= (period.ToNanosecondTimeSpan().Nanoseconds() / HeldDivisor))
    {
      // The display's side held the loop before this frame: the frames before it were still on their way, and are shown one
      // after the other while it was held. The vertical blanks it was held over are no refreshes that were lost: the
      // frame is for the blank its swap interval after the last one, as if they had not been, the animation time does
      // not step over them, and it is that many refreshes further behind the clock. What the start was late by beyond
      // the display's hold is late as any start is
      const int64_t held = std::min(displaySlot - dueSlot, period.RefreshesToFit(m_displayHeld));
      m_heldSlots += held;
      displaySlot -= held;
      m_refreshesBehindClock += static_cast<uint64_t>(held);
      m_displayHeldRefreshes += static_cast<uint64_t>(held);
    }
    m_displayHeld = NanosecondTimeSpan();
    m_startedLate = hasPrevious && displaySlot > dueSlot;
    if (m_takenOver)
    {
      // Another pacer placed the frames up to this one: the frame window's times go on on the vertical blanks, the frame
      // before this one a swap interval before this frame's blank and the newest of the frame window one more before that
      const int64_t blankNanoseconds = (TimeOfBlank(displaySlot) - NanosecondTickCount()).Nanoseconds();
      m_rule.RebaseNewest(NanosecondTimeSpan(blankNanoseconds - period.TimeFor(int64_t{2} * swapInterval).Nanoseconds()));
    }

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
    m_gpuWait.BeginFrame();
    m_pauseHeldByWait = m_pauseHeldByWait || m_waitsForPresent;
    m_takenOver = false;
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
    // with the aim of smoothness, as it starts when the wait after that is over. With a time on the present that is the
    // time the present would have been made at, which the next frame's start is held to
    schedule.NextFrameStartTime = isLowLatency ? StartTimeFor(displaySlot + m_pauseSlots + int64_t{swapInterval}) : PresentTimeFor(displaySlot);
    schedule.TargetFrameTime = NanosecondTimeDuration(period.TimeFor(swapInterval));
    schedule.PreferredFrameTime = NanosecondTimeDuration(period.TimeFor(m_rule.PreferredSwapInterval()));
    schedule.Change = change;
    m_lastAnimationTime = animationTime;
    m_nextFrameStartTime = schedule.NextFrameStartTime;
    return schedule;
  }

  PresentPlan VBlankLoopPacer::EndFrame(const NanosecondTickCount workDoneTime) noexcept
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
    // shows it at the vertical blank it is for, and the frame is presented when it is done
    DisplayPlacementUtil::Place(plan, m_presentTiming, TimeOfBlank(m_displaySlot), m_swapInterval, m_rule.Refresh());
    if (m_presentTiming != PresentTiming::AtTime)
    {
      // Smoothness: made early, and the present waits for the time that has the frame ready at its place. Low latency:
      // presented at once, unless it is done before its refresh begins
      const NanosecondTickCount presentTime = PresentTimeFor(m_displaySlot);
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

  NanosecondTimeDuration VBlankLoopPacer::CpuBusyAt(const NanosecondTickCount now) const noexcept
  {
    return m_frameOpen ? NanosecondTimeDuration(now - m_startTime) : NanosecondTimeDuration();
  }

  void VBlankLoopPacer::AddPresent(const PresentReport& report) noexcept
  {
    m_lastPresentBlocked = report.Blocked();
    // A present that waited for the display held the loop before the next frame
    AddDisplayHeld(report.Blocked());
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
    m_gpuWait.AddPresent(report);
  }

  void VBlankLoopPacer::AddGpuWork(const GpuWorkReport& report) noexcept
  {
    m_frameWork.AddGpuWork(report, m_rule.Settings().FrameMargin());
  }

  void VBlankLoopPacer::ForgetPresents() noexcept
  {
    ArmStartupPause();
    m_wait.ForgetPresents();
    m_gpuWait.ForgetPresents();
  }

  PacerHandover VBlankLoopPacer::GiveOver() const noexcept
  {
    PacerHandover handover;
    handover.HasFrame = m_hasFrame;
    handover.FrameId = m_frameId;
    handover.StartTime = m_startTime;
    handover.NextFrameStartTime = m_takenOver ? m_takeOverStartTime : m_nextFrameStartTime;
    if (m_waitsForPresent && !m_takenOver)
    {
      // A wait for a present held this loop, not the time the frames were given: with the wait a frame starts when the
      // display took an earlier one, a swap interval after the frame before it, and that can be later than its time (with
      // the aim of smoothness it is, by the frames that are not made ahead while the wait holds). A part that takes over
      // without the wait would start its first frames back to back to catch up with a time that has passed
      handover.NextFrameStartTime = std::max(handover.NextFrameStartTime, m_startTime + m_rule.Refresh().TimeFor(int64_t{m_swapInterval}));
    }
    if (m_hasFrame && m_hasPresentTime && !m_takenOver)
    {
      handover.HasPresentTime = true;
      handover.LastPresentTime = m_presentTime;
      // A swap interval after this one's, or where the presents are held to their place, the next frame's place
      const bool holdsPresents = m_presentTiming != PresentTiming::AtTime && m_rule.Settings().Aim() == PacerAim::Smoothness;
      handover.NextPresentTime = holdsPresents ? PresentTimeFor(m_displaySlot + m_pauseSlots + int64_t{m_rule.SwapInterval()})
                                               : m_presentTime + m_rule.Refresh().TimeFor(int64_t{m_swapInterval});
    }
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

  void VBlankLoopPacer::TakeOver(const PacerHandover& handover, const SwapIntervalRule& rule) noexcept
  {
    m_rule.TakeOver(rule);
    m_frameWork = handover.FrameWork;
    m_wait = handover.Wait;
    m_gpuWait = handover.GpuWait;
    // The pause after start-up is the swap chain's, whichever part places the frames: made once, or still to be made
    m_pausePending = handover.PausePending;
    m_pauseHeldByWait = handover.PauseHeldByWait;
    m_pauseHasFirstFrame = handover.HasPauseFirstFrame;
    m_pauseFirstFrameTime = handover.PauseFirstFrameTime;
    m_presentTaken = handover.PresentTaken;
    m_frameId = handover.FrameId;
    m_animationTime = handover.AnimationTime;
    m_lastAnimationTime = handover.LastAnimationTime;
    m_refreshesBehindClock = handover.RefreshesBehindClock;
    // The next frame is the first that is for a vertical blank. Until then the frame before it stands in for one, so that a
    // pause before the next frame is still seen
    m_hasFrame = handover.HasFrame;
    m_takenOver = handover.HasFrame;
    m_takeOverStartTime = handover.NextFrameStartTime;
    m_displayHeld = NanosecondTimeSpan();
    m_hasTakeOverPresent = handover.HasFrame && handover.HasPresentTime;
    m_takeOverPresentTime = handover.LastPresentTime;
    m_startTime = handover.StartTime;
    m_swapInterval = m_rule.SwapInterval();
    m_pauseSlots = 0;
    m_displaySlot = 0;
    m_startedLate = false;
    m_work = NanosecondTimeSpan();
    m_frameOpen = false;
    m_frameEnded = false;
    m_hasPresentTime = false;
    m_hasShownFloor = false;
    m_hasShownCeiling = false;
    m_leadCount = 0;
    ForgetReadyPlace();
  }

  void VBlankLoopPacer::SetRefreshPeriod(const RefreshPeriod period) noexcept
  {
    if (period != m_rule.Refresh())
    {
      m_rule.SetRefreshPeriod(period);
      m_hasFrame = false;
      m_timeline.Clear();
      ForgetReadyPlace();
    }
  }

  void VBlankLoopPacer::SetSettings(const PacerSettings& settings)
  {
    if (settings != m_rule.Settings())
    {
      const bool samePeriod = settings.Refresh() == m_rule.Refresh();
      m_rule.SetSettings(settings);
      m_hasFrame = false;
      ForgetReadyPlace();
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
    ForgetReadyPlace();
    m_framesSinceDisturbed = ShownLaterFramesApart;
    m_framesSincePlaceStep = ShownLaterFramesApart;
    m_pauseSlots = 0;
    ArmStartupPause();
    m_wait.Reset();
    m_gpuWait.Reset();
  }
}
