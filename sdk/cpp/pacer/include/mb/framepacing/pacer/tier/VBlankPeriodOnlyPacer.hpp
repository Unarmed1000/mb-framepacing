#ifndef MB_FRAMEPACING_PACER_TIER_VBLANKPERIODONLYPACER_HPP
#define MB_FRAMEPACING_PACER_TIER_VBLANKPERIODONLYPACER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/capability/PacerTier.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/frame/FrameStartPlan.hpp>
#include <mb/framepacing/pacer/frame/GpuWorkReport.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/frame/VBlankReading.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
#include <mb/framepacing/pacer/tier/VBlankLoopPacer.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built one tier's pacer at a time; FramePacer is
  //! the pacer the library has until they replace it). The pacer of PacerTier::VBlankPeriodOnly: a whole pacer by
  //! itself, for an application that has the baseline (a steady clock, the refresh period of the display its window is on, a wait
  //! until a time, a present that shows every frame in order for at least a refresh) and is told when that display's vertical
  //! blanks are (PacerCapability::VBlankTimes).
  //!
  //! It holds every rule and every time calculation, and the application carries out what it is given. Its calls a frame:
  //!   AddVBlank   a vertical blank's time, whenever the application has one: where the display's refreshes are
  //!   PlanFrame   before the frame takes anything: the time to wait until
  //!   BeginFrame  the frame starts: its swap interval, its animation time and the marker's values
  //!   EndFrame    the CPU's work is done: the time to wait until before the present
  //!   AddPresent  after the present, before the next frame is planned: when it was called and returned, and whether the
  //!               system took it
  //! and, where the application has it, AddGpuWork: the GPU's work on an earlier frame.
  //!
  //! It knows where the refreshes are, so it does not guess. Every frame is for one vertical blank, the one it is to be shown at,
  //! and that blank follows from the frame before it: its swap interval later. A frame is shown at a blank when it is ready the
  //! frame margin before it (PacerSettings::FrameMargin); ready is presented, and with GPU work reports the GPU done with it. The
  //! pacer aims a frame to be ready at PacerSettings::ReadyPlacePercent of the refresh before its blank. The grid follows every
  //! reading, so a refresh period that is a little off the display's does not add up, which it does for a pacer on a timer.
  //!
  //! When a frame is made is its aim (PacerSettings::Aim), and it has both.
  //!
  //! PacerAim::Smoothness, the default: a frame starts at once and is made early, and its present is held until the time that
  //! has it ready at its place. Work that runs longer than usual uses up the time the frame would have waited. At one refresh per
  //! frame it also keeps a reserve: PacerSettings::WaitingPresents less one frames are ready that many refreshes before they are
  //! shown and wait, so a frame may be that many refreshes late and is still shown at its blank, and the frames after it are made
  //! back to back until the reserve is there again.
  //!
  //! PacerAim::LowLatency: a frame's start is held so that it is ready at its place and no sooner, from how long the frames
  //! before it took (the longest of the last eight but no longer than its swap interval's time, and the GPU time where it is
  //! reported), and it is presented when it is done, though never before the refresh it is to be ready in begins. The frame
  //! shown is as new as it can be, and a frame that takes longer than the ones before it can miss its blank. For the frames
  //! that pile up behind the first presents of a new swap chain it pauses once, as TimerPeriodOnlyPacer does.
  //!
  //! What it knows before a frame is in the frame: a frame that starts too late for its blank is for the first blank it can be
  //! ready for, and its animation step is the refreshes from the frame before it to that blank, so its animation time is the
  //! time it is shown. What it learns only after a frame (its own work ran long and it missed its blank) is not caught up with:
  //! the frame after it is its swap interval later and its animation step is its swap interval, and RefreshesBehindClock() counts
  //! those refreshes.
  //!
  //! What it does not do: learn of a frame that waits to be shown although it was ready in time. It sees the display's refreshes
  //! and not what the display shows.
  //!
  //! Until the first vertical blank reading it takes the first frame's start as a vertical blank: a pacer on a timer, by the
  //! same rules. A reading's own refresh period is not used yet: the period is the settings'.
  //!
  //! Values in, values out: no platform API, no clock read, no wait. Made once (it allocates the rule's frame window); pacing
  //! frames never allocates.
  //!
  //! The class is VBlankLoopPacer without the wait for a present: the rules and the calculations are there.
  class VBlankPeriodOnlyPacer
  {
    VBlankLoopPacer m_pacer;

  public:
    //! The tier this pacer is for.
    static constexpr PacerTier Tier = PacerTier::VBlankPeriodOnly;

    explicit VBlankPeriodOnlyPacer(const PacerSettings& settings)
      : m_pacer(settings, false)
    {
    }

    //! A vertical blank of the display the window is on, whenever the application has one.
    void AddVBlank(const VBlankReading& reading) noexcept
    {
      m_pacer.AddVBlank(reading);
    }

    //! Before a frame takes anything: what to wait for before it starts.
    [[nodiscard]] FrameStartPlan PlanFrame(const NanosecondTickCount now) const noexcept
    {
      return m_pacer.PlanFrame(now);
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

    //! The display's refresh period changed: the frames start again, the animation time goes on.
    void SetRefreshPeriod(const RefreshPeriod period) noexcept
    {
      m_pacer.SetRefreshPeriod(period);
    }

    //! Other settings: the frames start again, the animation time goes on. It may allocate.
    void SetSettings(const PacerSettings& settings)
    {
      m_pacer.SetSettings(settings);
    }

    //! Starts again as made, with the settings it has; the animation time goes on.
    void Reset() noexcept
    {
      m_pacer.Reset();
    }

    //! True when a vertical blank reading was taken.
    [[nodiscard]] bool HasVBlankReading() const noexcept
    {
      return m_pacer.HasVBlankReading();
    }

    //! The readings that were off where the readings before them put the vertical blanks.
    [[nodiscard]] uint64_t VBlankJumps() const noexcept
    {
      return m_pacer.VBlankJumps();
    }

    //! The refreshes the animation time is behind the clock: what was lost and not caught up with.
    [[nodiscard]] uint64_t RefreshesBehindClock() const noexcept
    {
      return m_pacer.RefreshesBehindClock();
    }

    //! The pauses after start-up that were made, since the pacer was made.
    [[nodiscard]] uint64_t StartupPauses() const noexcept
    {
      return m_pacer.StartupPauses();
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
