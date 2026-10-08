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
    //! The tier of the three things a pacer uses: who puts a frame on its refresh, where the refreshes are, and what holds
    //! the loop. Never a tier of the display that skips: no pacer is built for it
    constexpr PacerTier TierOf(const bool timedPresent, const bool onVBlanks, const bool waitsForPresent) noexcept
    {
      if (timedPresent)
      {
        if (onVBlanks)
        {
          return waitsForPresent ? PacerTier::TimedVBlankWaitForPresent : PacerTier::TimedVBlankPeriodOnly;
        }
        return waitsForPresent ? PacerTier::TimedTimerWaitForPresent : PacerTier::TimedTimerPeriodOnly;
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
    , m_grid(settings, capabilities.Has(PacerCapability::WaitForPresent))
    , m_vblank(settings, capabilities.Has(PacerCapability::WaitForPresent))
    , m_onVBlanks(capabilities.Has(PacerCapability::VBlankTimes))
  {
    m_grid.SetPresentTiming(DisplayPlacementUtil::TimingFor(capabilities));
    m_vblank.SetPresentTiming(DisplayPlacementUtil::TimingFor(capabilities));
    m_grid.SetWaitsForGpuWork(capabilities.Has(PacerCapability::WaitForGpuWork));
    m_vblank.SetWaitsForGpuWork(capabilities.Has(PacerCapability::WaitForGpuWork));
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
    // What is really pacing: the vertical blanks once one was read, and the wait while it is made
    const bool onVBlanks = m_onVBlanks && m_vblank.HasVBlankReading();
    const bool waits = m_active.Has(PacerCapability::WaitForPresent) && !PresentWaitsStopped();
    // The display's side places a frame with a time before which it is not shown, and with nothing else
    return TierOf(DisplayPlacementUtil::TimingFor(m_active) == PresentTiming::AtTime, onVBlanks, waits);
  }

  bool TierPacer::IsFrameOpen() const noexcept
  {
    return m_onVBlanks ? m_vblank.IsFrameOpen() : m_grid.IsFrameOpen();
  }

  void TierPacer::Apply(const PacerCapabilities& active) noexcept
  {
    // Who puts a frame on its refresh, from the next present on: known to the part that takes the frames over
    const PresentTiming timing = DisplayPlacementUtil::TimingFor(active);
    m_grid.SetPresentTiming(timing);
    m_vblank.SetPresentTiming(timing);
    const bool onVBlanks = active.Has(PacerCapability::VBlankTimes);
    if (onVBlanks != m_onVBlanks)
    {
      // Another part places the frames from the next one on. The vertical blanks are read anew when they are taken up, and
      // forgotten when they are given up: a reading from before says nothing of now
      if (onVBlanks)
      {
        m_vblank.ForgetVBlanks();
        m_vblank.TakeOver(m_grid.GiveOver(), m_grid.Rule());
      }
      else
      {
        m_grid.TakeOver(m_vblank.GiveOver(), m_vblank.Rule());
        m_vblank.ForgetVBlanks();
      }
      m_onVBlanks = onVBlanks;
    }
    const bool waits = active.Has(PacerCapability::WaitForPresent);
    m_grid.SetWaitsForPresent(waits);
    m_vblank.SetWaitsForPresent(waits);
    // The wait for the GPU's work holds the loop where no wait for a present does: the part knows which of the two it has
    m_grid.SetWaitsForGpuWork(active.Has(PacerCapability::WaitForGpuWork));
    m_vblank.SetWaitsForGpuWork(active.Has(PacerCapability::WaitForGpuWork));
    m_active = active;
  }

  void TierPacer::ApplyPending() noexcept
  {
    if (m_hasPendingActive)
    {
      m_hasPendingActive = false;
      Apply(m_pendingActive);
    }
  }

  void TierPacer::AddVBlank(const VBlankReading& reading) noexcept
  {
    if (m_onVBlanks)
    {
      m_vblank.AddVBlank(reading);
    }
  }

  FrameStartPlan TierPacer::PlanFrame(const NanosecondTickCount now) const noexcept
  {
    return m_onVBlanks ? m_vblank.PlanFrame(now) : m_grid.PlanFrame(now);
  }

  void TierPacer::AddPresentWait(const PresentWaitReport& report) noexcept
  {
    // A wait the plan did not ask for is not taken: the wait is not active
    if (!m_active.Has(PacerCapability::WaitForPresent))
    {
      return;
    }
    if (m_onVBlanks)
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
    if (!m_active.Has(PacerCapability::WaitForGpuWork) || m_active.Has(PacerCapability::WaitForPresent))
    {
      return;
    }
    if (m_onVBlanks)
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
    const FrameSchedule schedule = m_onVBlanks ? m_vblank.BeginFrame(cpuStartTime) : m_grid.BeginFrame(cpuStartTime);
    // Kept for the display report that comes for it later
    m_display.AddFrame(schedule.FrameId, schedule.AnimationStep, cpuStartTime);
    return schedule;
  }

  PresentPlan TierPacer::EndFrame(const NanosecondTickCount workDoneTime) noexcept
  {
    PresentPlan plan = m_onVBlanks ? m_vblank.EndFrame(workDoneTime) : m_grid.EndFrame(workDoneTime);
    // The present's own swap interval, where it takes one: given next to what the loop does, as a minimum duration is
    plan.SwapInterval = DisplayPlacementUtil::PresentSwapIntervalFor(m_active, SwapInterval());
    // The frame has ended: a change of the active set takes effect now, for the frame after it
    ApplyPending();
    return plan;
  }

  NanosecondTimeDuration TierPacer::CpuBusyAt(const NanosecondTickCount now) const noexcept
  {
    return m_onVBlanks ? m_vblank.CpuBusyAt(now) : m_grid.CpuBusyAt(now);
  }

  void TierPacer::AddPresent(const PresentReport& report) noexcept
  {
    if (m_onVBlanks)
    {
      m_vblank.AddPresent(report);
    }
    else
    {
      m_grid.AddPresent(report);
    }
  }

  void TierPacer::AddGpuWork(const GpuWorkReport& report) noexcept
  {
    if (m_onVBlanks)
    {
      m_vblank.AddGpuWork(report);
    }
    else
    {
      m_grid.AddGpuWork(report);
    }
  }

  void TierPacer::AddSystemWait(const SystemWaitReport& report) noexcept
  {
    if (m_onVBlanks)
    {
      m_vblank.AddSystemWait(report);
    }
    else
    {
      m_grid.AddSystemWait(report);
    }
  }

  void TierPacer::AddDisplayReport(const DisplayReport& report) noexcept
  {
    if (m_active.Has(PacerCapability::DisplayTimes))
    {
      m_display.AddDisplayReport(report, Refresh());
    }
  }

  void TierPacer::ForgetPresents() noexcept
  {
    if (m_onVBlanks)
    {
      m_vblank.ForgetPresents();
    }
    else
    {
      m_grid.ForgetPresents();
    }
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
  }

  void TierPacer::SetSettings(const PacerSettings& settings)
  {
    if (settings != Settings())
    {
      m_display.Restart();
    }
    m_grid.SetSettings(settings);
    m_vblank.SetSettings(settings);
  }

  void TierPacer::Reset() noexcept
  {
    m_grid.Reset();
    m_vblank.Reset();
    m_display.Restart();
  }
}
