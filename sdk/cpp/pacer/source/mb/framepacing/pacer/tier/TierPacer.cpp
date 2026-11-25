// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The one pacer an application talks to (sdk/doc/pacer-design.md, "How a pacer is put together"): the active
// capabilities pick how the frames are placed and what holds the loop, and a change of them is a handover at a frame's start.
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <mb/framepacing/pacer/capability/PacerTierUtil.hpp>
#include <mb/framepacing/pacer/placement/DisplayPlacementUtil.hpp>
#include <mb/framepacing/pacer/placement/PresentTiming.hpp>
#include <mb/framepacing/pacer/tier/PacerHandover.hpp>
#include <mb/framepacing/pacer/tier/TierPacer.hpp>

namespace MB::FramePacing::Pacer
{
  namespace
  {
    //! The tier of the four things a pacer uses: who puts a frame on its refresh (a time on the present, a duration on it, or
    //! the loop), where the refreshes are, what holds the loop, and where frames were shown. A duration on the present is the
    //! second major tier with a wait for a present or display times (else the loop places the frames)
    constexpr PacerTier TierOf(const PresentTiming placement, const bool onVBlanks, const bool waitsForPresent, const bool displayTimes) noexcept
    {
      if (placement == PresentTiming::AtTime)
      {
        if (onVBlanks)
        {
          return waitsForPresent ? PacerTier::AtTimeVBlankWaitForPresent : PacerTier::AtTimeVBlankPeriodOnly;
        }
        return waitsForPresent ? PacerTier::AtTimeTimerWaitForPresent : PacerTier::AtTimeTimerPeriodOnly;
      }
      if (placement == PresentTiming::AfterDuration && (waitsForPresent || displayTimes))
      {
        if (waitsForPresent)
        {
          return onVBlanks ? PacerTier::AfterDurationVBlankWaitForPresent : PacerTier::AfterDurationTimerWaitForPresent;
        }
        return onVBlanks ? PacerTier::AfterDurationVBlankDisplayTimes : PacerTier::AfterDurationTimerDisplayTimes;
      }
      if (onVBlanks)
      {
        return waitsForPresent ? PacerTier::VBlankWaitForPresent : PacerTier::VBlankPeriodOnly;
      }
      return waitsForPresent ? PacerTier::TimerWaitForPresent : PacerTier::TimerPeriodOnly;
    }
  }

  TierPacer::TierPacer(const PacerSettings& settings, const PacerCapabilities& capabilities)
    : m_capabilities(capabilities)
    , m_active(capabilities)
    , m_grid(settings, PacerTierUtil::HasDisplayHold(capabilities))
    , m_vblank(settings, PacerTierUtil::HasDisplayHold(capabilities))
    , m_held(settings)
    , m_part(PartFor(capabilities))
  {
    m_grid.SetPresentTiming(DisplayPlacementUtil::TimingFor(capabilities));
    m_vblank.SetPresentTiming(DisplayPlacementUtil::TimingFor(capabilities));
    m_grid.SetDisplayHoldsUpTo(DisplayPlacementUtil::HoldsUpTo(capabilities));
    m_vblank.SetDisplayHoldsUpTo(DisplayPlacementUtil::HoldsUpTo(capabilities));
    m_grid.SetMinSwapInterval(capabilities.MinPresentSwapInterval());
    m_vblank.SetMinSwapInterval(capabilities.MinPresentSwapInterval());
    m_held.SetPresentSwapIntervals(capabilities.MinPresentSwapInterval(), capabilities.MaxPresentSwapInterval());
    m_grid.SetUsesDisplayTimes(capabilities.Has(PacerCapability::DisplayTimes));
    m_vblank.SetUsesDisplayTimes(capabilities.Has(PacerCapability::DisplayTimes));
    m_grid.SetWaitsForGpuWork(capabilities.Has(PacerCapability::WaitForGpuWork));
    m_vblank.SetWaitsForGpuWork(capabilities.Has(PacerCapability::WaitForGpuWork));
    m_grid.SetWaitEndsWhenTaken(PacerTierUtil::HoldsByFrameCallback(capabilities));
  }

  TierPacer::PacerPart TierPacer::PartFor(const PacerCapabilities& active) noexcept
  {
    // The system's hold where the set is in its major tier; else the display's vertical blanks where they are active, and a
    // grid on the clock where not
    if (PacerTierUtil::Rate(active).Tier == PacerTier::SystemHoldsLoop)
    {
      return PacerPart::Held;
    }
    return active.Has(PacerCapability::VBlankTimes) ? PacerPart::VBlank : PacerPart::Grid;
  }

  void TierPacer::SetCapabilities(const PacerCapabilities& capabilities) noexcept
  {
    m_capabilities = capabilities;
    SetActiveCapabilities(ActiveCapabilities());
  }

  void TierPacer::SetActiveCapabilities(const PacerCapabilities& active) noexcept
  {
    m_pendingActive = active.IntersectedWith(m_capabilities);
    m_hasPendingActive = true;
    if (!IsFrameOpen())
    {
      ApplyPending();
    }
  }

  void TierPacer::SetPacing(const bool isPacing) noexcept
  {
    m_pendingPacing = isPacing;
    m_hasPendingPacing = true;
    if (!IsFrameOpen())
    {
      ApplyPending();
    }
  }

  PacerRating TierPacer::Rating() const noexcept
  {
    return PacerTierUtil::Rate(m_capabilities);
  }

  PacerRating TierPacer::ActiveRating() const noexcept
  {
    return PacerTierUtil::Rate(ActiveCapabilities());
  }

  PacerTier TierPacer::WorkingTier() const noexcept
  {
    // The system's hold is its tier whether the system holds or the pacer limits the frames
    if (m_part == PacerPart::Held)
    {
      return PacerTier::SystemHoldsLoop;
    }
    // What is really pacing: the vertical blanks once one was read, and the wait while it is made
    const bool onVBlanks = m_part == PacerPart::VBlank && m_vblank.HasVBlankReading();
    const bool waits = PacerTierUtil::HasDisplayHold(m_active) && !PresentWaitsStopped();
    // The display's side places a frame at a time, or after the one before it where the pacer sees where frames were shown
    PresentTiming placement = DisplayPlacementUtil::TimingFor(m_active);
    if (placement != PresentTiming::AtTime)
    {
      placement = DisplayPlacementUtil::HoldsUpTo(m_active) > 0 ? PresentTiming::AfterDuration : PresentTiming::Untimed;
    }
    return TierOf(placement, onVBlanks, waits, m_active.Has(PacerCapability::DisplayTimes));
  }

  bool TierPacer::IsFrameOpen() const noexcept
  {
    if (!m_isPacing)
    {
      return m_free.IsFrameOpen();
    }
    switch (m_part)
    {
    case PacerPart::VBlank:
      return m_vblank.IsFrameOpen();
    case PacerPart::Held:
      return m_held.IsFrameOpen();
    case PacerPart::Grid:
      break;
    }
    return m_grid.IsFrameOpen();
  }

  const SwapIntervalRule& TierPacer::Rule() const noexcept
  {
    switch (m_part)
    {
    case PacerPart::VBlank:
      return m_vblank.Rule();
    case PacerPart::Held:
      return m_held.Rule();
    case PacerPart::Grid:
      break;
    }
    return m_grid.Rule();
  }

  PacerHandover TierPacer::GiveOverFrom(const PacerPart part) const noexcept
  {
    switch (part)
    {
    case PacerPart::VBlank:
      return m_vblank.GiveOver();
    case PacerPart::Held:
      return m_held.GiveOver();
    case PacerPart::Grid:
      break;
    }
    return m_grid.GiveOver();
  }

  void TierPacer::TakeOverInto(const PacerPart part, const PacerHandover& handover, const SwapIntervalRule& rule, const bool forgetsVBlanks) noexcept
  {
    switch (part)
    {
    case PacerPart::VBlank:
      // The vertical blanks are read anew when they are taken up: a reading from before says nothing of now
      if (forgetsVBlanks)
      {
        m_vblank.ForgetVBlanks();
      }
      m_vblank.TakeOver(handover, rule);
      return;
    case PacerPart::Held:
      m_held.TakeOver(handover, rule);
      return;
    case PacerPart::Grid:
      break;
    }
    m_grid.TakeOver(handover, rule);
  }

  void TierPacer::Apply(const PacerCapabilities& active) noexcept
  {
    // Who puts a frame on its refresh, from the next present on: known to the part that takes the frames over
    const PresentTiming timing = DisplayPlacementUtil::TimingFor(active);
    m_grid.SetPresentTiming(timing);
    m_vblank.SetPresentTiming(timing);
    m_grid.SetDisplayHoldsUpTo(DisplayPlacementUtil::HoldsUpTo(active));
    m_vblank.SetDisplayHoldsUpTo(DisplayPlacementUtil::HoldsUpTo(active));
    // The shortest swap interval the present takes: no part's rule runs faster
    m_grid.SetMinSwapInterval(active.MinPresentSwapInterval());
    m_vblank.SetMinSwapInterval(active.MinPresentSwapInterval());
    m_held.SetPresentSwapIntervals(active.MinPresentSwapInterval(), active.MaxPresentSwapInterval());
    m_grid.SetUsesDisplayTimes(active.Has(PacerCapability::DisplayTimes));
    m_vblank.SetUsesDisplayTimes(active.Has(PacerCapability::DisplayTimes));
    const PacerPart part = PartFor(active);
    if (part != m_part)
    {
      // Another part places the frames from the next one on, with the rule of the part before. The vertical blanks are
      // forgotten when they are given up (while the application is not pacing, the frames are the free run's: the part only
      // takes the rule now, and the frames when pacing starts again)
      TakeOverInto(part, m_isPacing ? GiveOverFrom(m_part) : m_free.GiveOver(), Rule(), true);
      if (m_part == PacerPart::VBlank)
      {
        m_vblank.ForgetVBlanks();
      }
      m_part = part;
    }
    const bool waits = PacerTierUtil::HasDisplayHold(active);
    m_grid.SetWaitsForPresent(waits);
    m_vblank.SetWaitsForPresent(waits);
    // Vertical blank times keep a frame callback from holding the loop: only the grid can have that wait
    m_grid.SetWaitEndsWhenTaken(PacerTierUtil::HoldsByFrameCallback(active));
    // The wait for the GPU's work holds the loop where no wait for a present does: the part knows which of the two it has
    m_grid.SetWaitsForGpuWork(active.Has(PacerCapability::WaitForGpuWork));
    m_vblank.SetWaitsForGpuWork(active.Has(PacerCapability::WaitForGpuWork));
    m_active = active;
  }

  void TierPacer::ApplyPacing(const bool isPacing) noexcept
  {
    if (isPacing == m_isPacing)
    {
      return;
    }
    if (!isPacing)
    {
      // The frames go on unplaced, with what the part that paced them hands over
      m_free.TakeOver(GiveOverFrom(m_part));
    }
    else
    {
      // The part that would pace has the rule already, and places the frames from its first one on. The vertical blanks
      // were read all along
      TakeOverInto(m_part, m_free.GiveOver(), Rule(), false);
    }
    m_isPacing = isPacing;
  }

  void TierPacer::ApplyPending() noexcept
  {
    if (m_hasPendingActive)
    {
      m_hasPendingActive = false;
      Apply(m_pendingActive);
    }
    if (m_hasPendingPacing)
    {
      m_hasPendingPacing = false;
      ApplyPacing(m_pendingPacing);
    }
  }

  void TierPacer::AddVBlank(const VBlankReading& reading) noexcept
  {
    if (m_part == PacerPart::VBlank)
    {
      m_vblank.AddVBlank(reading);
    }
  }

  FrameStartPlan TierPacer::PlanFrame(const NanosecondTickCount now) const noexcept
  {
    if (!m_isPacing)
    {
      // Not pacing: nothing to wait for
      return {};
    }
    switch (m_part)
    {
    case PacerPart::VBlank:
      return m_vblank.PlanFrame(now);
    case PacerPart::Held:
      return m_held.PlanFrame(now);
    case PacerPart::Grid:
      break;
    }
    return m_grid.PlanFrame(now);
  }

  void TierPacer::AddPresentWait(const PresentWaitReport& report) noexcept
  {
    // A wait the plan did not ask for is not taken: the wait is not active, the system's hold asks for none, or the application
    // is not pacing
    if (!m_isPacing || !PacerTierUtil::HasDisplayHold(m_active) || m_part == PacerPart::Held)
    {
      return;
    }
    if (m_part == PacerPart::VBlank)
    {
      m_vblank.AddPresentWait(report);
    }
    else
    {
      m_grid.AddPresentWait(report);
    }
  }

  void TierPacer::AddGpuWait(const GpuWaitReport& report) noexcept
  {
    // A wait the plan did not ask for is not taken
    if (!m_isPacing || !m_active.Has(PacerCapability::WaitForGpuWork) || PacerTierUtil::HasDisplayHold(m_active) || m_part == PacerPart::Held)
    {
      return;
    }
    if (m_part == PacerPart::VBlank)
    {
      m_vblank.AddGpuWait(report);
    }
    else
    {
      m_grid.AddGpuWait(report);
    }
  }

  FrameSchedule TierPacer::BeginFrame(const NanosecondTickCount cpuStartTime) noexcept
  {
    // A change that waited for a frame that was never ended
    ApplyPending();
    FrameSchedule schedule;
    if (!m_isPacing)
    {
      // What an application wants does not depend on whether it paces: the marker's preferred frame time is given
      schedule = m_free.BeginFrame(cpuStartTime, Refresh(), Settings().FrameWindowLength());
      schedule.PreferredFrameTime = NanosecondTimeDuration(Refresh().TimeFor(Rule().PreferredSwapInterval()));
    }
    else
    {
      switch (m_part)
      {
      case PacerPart::VBlank:
        schedule = m_vblank.BeginFrame(cpuStartTime);
        break;
      case PacerPart::Held:
        schedule = m_held.BeginFrame(cpuStartTime);
        break;
      case PacerPart::Grid:
        schedule = m_grid.BeginFrame(cpuStartTime);
        break;
      }
    }
    // The display's period is known whether the frame is paced or not
    schedule.RefreshPeriod = NanosecondTimeDuration(Refresh().ToNanosecondTimeSpan());
    // Kept for the display report that comes for it later, with the period the frame's marker says was intended, as the tools
    // read it: the target frame time (its swap interval) while pacing, else the preferred frame time
    const uint32_t intendedRefreshes = m_isPacing ? schedule.SwapInterval : Rule().PreferredSwapInterval();
    m_display.AddFrame(schedule.FrameId, schedule.AnimationStep, intendedRefreshes, cpuStartTime);
    return schedule;
  }

  PresentPlan TierPacer::EndFrame(const NanosecondTickCount workDoneTime) noexcept
  {
    PresentPlan plan;
    if (!m_isPacing)
    {
      plan = m_free.EndFrame(workDoneTime);
    }
    else
    {
      switch (m_part)
      {
      case PacerPart::VBlank:
        plan = m_vblank.EndFrame(workDoneTime);
        break;
      case PacerPart::Held:
        plan = m_held.EndFrame(workDoneTime);
        break;
      case PacerPart::Grid:
        plan = m_grid.EndFrame(workDoneTime);
        break;
      }
      // The present's own swap interval, where it takes one: given next to what the loop does, as a minimum duration is
      plan.SwapInterval = DisplayPlacementUtil::PresentSwapIntervalFor(m_active, SwapInterval());
    }
    // The frame has ended: a change of the active set takes effect now, for the frame after it
    ApplyPending();
    return plan;
  }

  NanosecondTimeDuration TierPacer::CpuBusyAt(const NanosecondTickCount now) const noexcept
  {
    if (!m_isPacing)
    {
      return m_free.CpuBusyAt(now);
    }
    switch (m_part)
    {
    case PacerPart::VBlank:
      return m_vblank.CpuBusyAt(now);
    case PacerPart::Held:
      return m_held.CpuBusyAt(now);
    case PacerPart::Grid:
      break;
    }
    return m_grid.CpuBusyAt(now);
  }

  void TierPacer::AddPresent(const PresentReport& report) noexcept
  {
    if (!m_isPacing)
    {
      m_free.AddPresent(report);
      return;
    }
    switch (m_part)
    {
    case PacerPart::VBlank:
      m_vblank.AddPresent(report);
      return;
    case PacerPart::Held:
      m_held.AddPresent(report);
      return;
    case PacerPart::Grid:
      break;
    }
    m_grid.AddPresent(report);
  }

  void TierPacer::AddGpuWork(const GpuWorkReport& report) noexcept
  {
    if (!m_isPacing)
    {
      m_free.AddGpuWork(report, Settings().FrameMargin());
      return;
    }
    switch (m_part)
    {
    case PacerPart::VBlank:
      m_vblank.AddGpuWork(report);
      return;
    case PacerPart::Held:
      m_held.AddGpuWork(report);
      return;
    case PacerPart::Grid:
      break;
    }
    m_grid.AddGpuWork(report);
  }

  void TierPacer::AddSystemWait(const SystemWaitReport& report) noexcept
  {
    if (!m_isPacing)
    {
      // A loop that is not paced is held by nothing of the pacer's: there is nothing to tell the wait apart from
      return;
    }
    switch (m_part)
    {
    case PacerPart::VBlank:
      m_vblank.AddSystemWait(report);
      return;
    case PacerPart::Held:
      m_held.AddSystemWait(report);
      return;
    case PacerPart::Grid:
      break;
    }
    m_grid.AddSystemWait(report);
  }

  void TierPacer::AddDisplayReport(const DisplayReport& report) noexcept
  {
    if (!m_active.Has(PacerCapability::DisplayTimes))
    {
      return;
    }
    m_display.AddDisplayReport(report, Refresh());
    // The part that places the frames follows the display's slip where it does (the second major tier, low latency); the
    // system's hold paces nothing by display times
    if (!m_isPacing)
    {
      return;
    }
    if (m_part == PacerPart::VBlank)
    {
      m_vblank.AddDisplayReport(report);
    }
    else if (m_part == PacerPart::Grid)
    {
      m_grid.AddDisplayReport(report);
    }
  }

  void TierPacer::ForgetPresents() noexcept
  {
    if (!m_isPacing)
    {
      m_free.ForgetPresents();
      return;
    }
    switch (m_part)
    {
    case PacerPart::VBlank:
      m_vblank.ForgetPresents();
      return;
    case PacerPart::Held:
      m_held.ForgetPresents();
      return;
    case PacerPart::Grid:
      break;
    }
    m_grid.ForgetPresents();
  }

  void TierPacer::SetRefreshPeriod(const RefreshPeriod period) noexcept
  {
    if (period != Refresh())
    {
      // The frames start again: nothing is judged across it
      m_display.Restart();
    }
    m_grid.SetRefreshPeriod(period);
    m_vblank.SetRefreshPeriod(period);
    m_held.SetRefreshPeriod(period);
  }

  void TierPacer::SetSettings(const PacerSettings& settings)
  {
    if (settings != Settings())
    {
      m_display.Restart();
    }
    m_grid.SetSettings(settings);
    m_vblank.SetSettings(settings);
    m_held.SetSettings(settings);
  }

  void TierPacer::Reset() noexcept
  {
    m_grid.Reset();
    m_vblank.Reset();
    m_held.Reset();
    m_free.Reset();
    m_display.Restart();
  }
}
