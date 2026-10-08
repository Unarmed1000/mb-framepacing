#ifndef MB_FRAMEPACING_PACER_TIER_VBLANKWAITFORPRESENTPACER_HPP
#define MB_FRAMEPACING_PACER_TIER_VBLANKWAITFORPRESENTPACER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
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
#include <mb/framepacing/pacer/frame/VBlankReading.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
#include <mb/framepacing/pacer/tier/TierPacer.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built one tier's pacer at a time; FramePacer is
  //! the pacer the library has until they replace it). The pacer of PacerTier::VBlankWaitForPresent, the best tier: a whole pacer
  //! by itself, for an application that has the baseline (a steady clock, the refresh period of the display its window is on, a
  //! wait until a time, a present that shows every frame in order for at least a refresh), is told when that display's vertical
  //! blanks are (PacerCapability::VBlankTimes) and can wait until a present it made was shown (PacerCapability::WaitForPresent).
  //!
  //! It holds every rule and every time calculation, and the application carries out what it is given. Its calls a frame:
  //!   AddVBlank       a vertical blank's time, whenever the application has one: where the display's refreshes are
  //!   PlanFrame       before the frame takes anything: the present to wait for, and then the time to wait until
  //!   AddPresentWait  after that wait: what became of it. PlanFrame is then called again
  //!   BeginFrame      the frame starts: its swap interval, its animation time and the marker's values
  //!   EndFrame        the CPU's work is done: the time to wait until before the present
  //!   AddPresent      after the present, before the next frame is planned: when it was called and returned, and whether the
  //!                   system took it
  //! and, where the application has it, AddGpuWork: the GPU's work on an earlier frame.
  //!
  //! It is VBlankPeriodOnlyPacer with the wait of TimerWaitForPresentPacer. Every frame is for one vertical blank, the one of
  //! the frame before it and its swap interval later; a frame is shown at a blank when it is ready the frame margin before it;
  //! and the pacer aims a frame to be ready at PacerSettings::ReadyPlacePercent of the refresh before its blank. What the wait
  //! adds:
  //!
  //! The frames that wait can not grow. Before a frame the loop waits until the present PacerSettings::WaitingPresents back was
  //! shown, whatever the pacer had worked out, so no pause after start-up is needed.
  //!
  //! Which vertical blank a frame was shown at becomes a fact. A wait that held the loop ended shortly after the display took
  //! its frame, so the vertical blank at or before the wait's end is the one that frame was shown at, and since the display
  //! takes one frame per refresh the frames made after it are shown that many blanks later at the soonest. Where that is later
  //! than the pacer had it (a frame that waited although it was ready in time, a refresh the display lost by itself), the frame
  //! that was last made is late and the next one is for a blank that much later (ShownLaterByWaits). A wait that returned at
  //! once for the frame last made says that it was shown by then, which the aim of low latency does not count on by itself.
  //!
  //! Where a frame has to be ready is learnt from that. One frame shown later than worked out is a refresh the display lost.
  //! Two within a few frames is a display that takes a frame sooner before a vertical blank than the pacer has it ready (or a GPU whose
  //! time nobody reported), and the place a frame is to be ready at is moved an eighth of a period earlier (ReadyPlaceNow),
  //! down to the start of the refresh. The frames that showed it are late; what is late even then is late by its work.
  //!
  //! PacerAim::Smoothness, the default: a frame starts when the wait is over and its present is held until the time that has
  //! it ready at its place. At one refresh per frame PacerSettings::ReserveFrames frames are ready ahead of the display, and
  //! the wait keeps them to that.
  //!
  //! PacerAim::LowLatency: after the wait a frame's start is held so that it is ready at its place and no sooner, and it is
  //! presented when it is done, though never before the refresh it is to be ready in begins.
  //!
  //! A wait that runs out is the pacer's own doing: the frame it held is not judged. After PresentWaitRule::WaitsRunOutToStop of them in a row
  //! the display is not taking the window's frames (a window that is covered or minimised) and the pacer stops waiting, all as
  //! TimerWaitForPresentPacer does (PresentWaitsStopped).
  //!
  //! The animation time is the time a frame is shown as far as that is known before the frame is made, as in
  //! VBlankPeriodOnlyPacer; what a wait says of a frame after the frames behind it were made is not caught up with
  //! (RefreshesBehindClock).
  //!
  //! Until the first vertical blank reading it takes the first frame's start as a vertical blank. A reading's own refresh
  //! period is not used yet: the period is the settings'.
  //!
  //! Values in, values out: no platform API, no clock read, no wait. Made once (it allocates the rule's frame window); pacing
  //! frames never allocates.
  //!
  //! The class is TierPacer with this tier's capabilities and no others: the rules and the calculations are in the parts it is
  //! put together from.
  class VBlankWaitForPresentPacer
  {
    TierPacer m_pacer;

  public:
    //! The tier this pacer is for.
    static constexpr PacerTier Tier = PacerTier::VBlankWaitForPresent;

    explicit VBlankWaitForPresentPacer(const PacerSettings& settings)
      : m_pacer(settings, PacerCapabilities(PacerCapability::VBlankTimes | PacerCapability::WaitForPresent))
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

    //! The vertical blanks that frames were shown later than worked out, by what the waits said.
    [[nodiscard]] uint64_t ShownLaterByWaits() const noexcept
    {
      return m_pacer.ShownLaterByWaits();
    }

    //! Where in a refresh a frame is to be ready now: the settings' place, and earlier by what the waits taught.
    [[nodiscard]] NanosecondTimeSpan ReadyPlaceNow() const noexcept
    {
      return m_pacer.ReadyPlaceNow();
    }

    //! The times the place a frame is to be ready at was tried one step later again, since the pacer was made.
    [[nodiscard]] uint64_t ReadyPlaceTries() const noexcept
    {
      return m_pacer.ReadyPlaceTries();
    }

    //! The tries that were taken back, because a frame was shown later in the frames after one.
    [[nodiscard]] uint64_t ReadyPlaceTriesTakenBack() const noexcept
    {
      return m_pacer.ReadyPlaceTriesTakenBack();
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
