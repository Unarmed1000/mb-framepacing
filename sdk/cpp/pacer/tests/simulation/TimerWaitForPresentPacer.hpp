#ifndef MB_FRAMEPACING_PACER_SIMULATION_TIMERWAITFORPRESENTPACER_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_TIMERWAITFORPRESENTPACER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/capability/PacerCapabilities.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <mb/framepacing/pacer/capability/PacerTier.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/frame/FrameStartPlan.hpp>
#include <mb/framepacing/pacer/frame/GpuWorkReport.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/frame/PresentWaitReport.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
#include <mb/framepacing/pacer/tier/TierPacer.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! Test code (the pacer module, sdk/doc/pacer-design.md): TierPacer with its capabilities fixed, as the tier's pacer was
  //! first built and as its tests still use it. The pacer of PacerTier::TimerWaitForPresent: a whole pacer by
  //! itself, for an application that has the baseline (a steady clock, the refresh period of the display its window is on, a wait
  //! until a time, a present that shows every frame in order for at least a refresh) and can wait until a present it names was
  //! shown (PacerCapability::WaitForPresent).
  //!
  //! It holds every rule and every time calculation, and the application carries out what it is given. Its calls a frame:
  //!   PlanFrame       before the frame takes anything: a present to wait for, then a time to wait until
  //!   AddPresentWait  what became of that wait, and then PlanFrame again: the wait may have taken long, and what it told
  //!                   about the display may have moved the time to wait until
  //!   BeginFrame      the frame starts: its swap interval, its animation time and the marker's values
  //!   EndFrame        the CPU's work is done: the time to wait until before the present
  //!   AddPresent      after the present, before the next frame is planned: when it was called, when it returned, and whether the
  //!                   system took it
  //! and, where the application has it, AddGpuWork: the GPU's work on an earlier frame.
  //!
  //! The frames that wait to be shown are kept few by the display itself: before a frame the application waits until the present
  //! PacerSettings::WaitingPresents back was shown. A wait for a present that was shown already returns at once, so it costs nothing
  //! while the loop is in step with the display, and it holds the loop for exactly what is too many when the display lost a
  //! refresh, at start-up, and after a swap chain was made anew. There is no other rule for those. A present the system did not
  //! take is not waited for, nor is any present before it. A wait may take as long as PacerSettings::PresentWaitSwapIntervals of
  //! the frame's own swap intervals: some presents are never shown.
  //!
  //! It has both aims (PacerSettings::Aim). PacerAim::Smoothness, the default: at one refresh per frame
  //! PacerSettings::WaitingPresents less one frames are made ahead of the display as a reserve, a frame that is late within it
  //! gives up no step, and what is beyond it is given up, all as TimerPeriodOnlyPacer does. Here the wait is what keeps the
  //! reserve to what may wait: the frames that are made up for can not make it more. PacerAim::LowLatency: no frame is made
  //! ahead, and a frame that would start half a period late or more takes the step nearest to where the loop is.
  //!
  //! The frame starts are on a grid of refresh periods on the clock, as TimerPeriodOnlyPacer's, and here the grid follows the
  //! display: a wait that really held the loop ended when the display took a frame, so the grid is moved towards its end, a
  //! quarter of the way each time. One return is not exact (it was measured 0.06 to 2.4 ms after the display took the frame on one
  //! system), which is why it is not taken whole. A wait that held the loop past the step its frame was due at shows as steps of
  //! the grid that were lost: the display was behind, and that frame is late.
  //!
  //! A frame held for more than one refresh is presented by the loop on a timer, in the period before the step the next frame is
  //! due at, PacerSettings::FrameMargin into it: still a guess, on a grid that is now close to the display's.
  //!
  //! The animation time advances by a frame's swap interval and by a loss that repeats, and the swap interval rule takes a
  //! frame's work as the CPU's and, with GPU work reports, the GPU's: both as TimerPeriodOnlyPacer's.
  //!
  //! Values in, values out: no platform API, no clock read, no wait. Made once (it allocates the rule's frame window); pacing
  //! frames never allocates.
  //!
  //! The class is TierPacer with this tier's capabilities and no others: the rules and the calculations are in the parts it is
  //! put together from.
  class TimerWaitForPresentPacer
  {
    TierPacer m_pacer;

  public:
    //! The tier this pacer is for.
    static constexpr PacerTier Tier = PacerTier::TimerWaitForPresent;

    explicit TimerWaitForPresentPacer(const PacerSettings& settings)
      : m_pacer(settings, PacerCapabilities(PacerCapability::WaitForPresent))
    {
    }

    //! Before a frame takes anything: what to wait for before it starts.
    [[nodiscard]] FrameStartPlan PlanFrame(const NanosecondTickCount now) const noexcept
    {
      return m_pacer.PlanFrame(now);
    }

    //! After the wait the plan asked for: what became of it. The frame is then planned again.
    void AddPresentWait(const PresentWaitReport& report) noexcept
    {
      m_pacer.AddPresentWait(report);
    }

    //! The frame starts: the previous frame is judged, the rule decides, and this frame is planned.
    FrameSchedule BeginFrame(const NanosecondTickCount cpuStartTime) noexcept
    {
      return m_pacer.BeginFrame(cpuStartTime);
    }

    //! The frame's CPU work is done: how it is to be presented.
    PresentPlan EndFrame(const NanosecondTickCount workDoneTime) noexcept
    {
      return m_pacer.EndFrame(workDoneTime);
    }

    //! The CPU busy time of the open frame up to now, for a marker drawn before the frame's end.
    [[nodiscard]] NanosecondTimeDuration CpuBusyAt(const NanosecondTickCount now) const noexcept
    {
      return m_pacer.CpuBusyAt(now);
    }

    //! After the present, before the next frame is planned: when it was called and returned, and whether the system took it.
    void AddPresent(const PresentReport& report) noexcept
    {
      m_pacer.AddPresent(report);
    }

    //! The GPU's work on an earlier frame, where the application has it.
    void AddGpuWork(const GpuWorkReport& report) noexcept
    {
      m_pacer.AddGpuWork(report);
    }

    //! The swap chain was made anew: the presents made so far are never shown.
    void ForgetPresents() noexcept
    {
      m_pacer.ForgetPresents();
    }

    //! The display's refresh period changed: the grid starts again, the animation time goes on.
    void SetRefreshPeriod(const RefreshPeriod period) noexcept
    {
      m_pacer.SetRefreshPeriod(period);
    }

    //! Other settings: the grid starts again, the animation time goes on. It may allocate.
    void SetSettings(const PacerSettings& settings)
    {
      m_pacer.SetSettings(settings);
    }

    //! Starts again as made, with the settings it has; the animation time goes on.
    void Reset() noexcept
    {
      m_pacer.Reset();
    }

    //! The refreshes the animation time is behind the clock: what was lost and not caught up with.
    [[nodiscard]] uint64_t RefreshesBehindClock() const noexcept
    {
      return m_pacer.RefreshesBehindClock();
    }

    //! True while no wait for a present is made because the waits ran out (PresentWaitRule).
    [[nodiscard]] bool PresentWaitsStopped() const noexcept
    {
      return m_pacer.PresentWaitsStopped();
    }

    //! The waits for a present that ended without the present being shown, since the pacer was made.
    [[nodiscard]] uint64_t PresentWaitTimeouts() const noexcept
    {
      return m_pacer.PresentWaitTimeouts();
    }

    //! The GPU time a frame is judged with: the newest that was reported, zero without one.
    [[nodiscard]] NanosecondTimeDuration GpuTime() const noexcept
    {
      return m_pacer.GpuTime();
    }

    //! How long the last present held the frame loop.
    [[nodiscard]] NanosecondTimeDuration LastPresentBlocked() const noexcept
    {
      return m_pacer.LastPresentBlocked();
    }

    //! What the rule's frame window holds.
    [[nodiscard]] FrameWindowState FrameWindow() const noexcept
    {
      return m_pacer.FrameWindow();
    }

    //! The swap interval of the frames now.
    [[nodiscard]] uint32_t SwapInterval() const noexcept
    {
      return m_pacer.SwapInterval();
    }

    //! The refresh period the pacer is on.
    [[nodiscard]] RefreshPeriod Refresh() const noexcept
    {
      return m_pacer.Refresh();
    }

    //! The settings the pacer has.
    [[nodiscard]] const PacerSettings& Settings() const noexcept
    {
      return m_pacer.Settings();
    }
  };
}

#endif
