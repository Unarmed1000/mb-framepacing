#ifndef MB_FRAMEPACING_PACER_TIER_TIERPACER_HPP
#define MB_FRAMEPACING_PACER_TIER_TIERPACER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/capability/PacerCapabilities.hpp>
#include <mb/framepacing/pacer/capability/PacerRating.hpp>
#include <mb/framepacing/pacer/capability/PacerTier.hpp>
#include <mb/framepacing/pacer/display/DisplayErrorCounter.hpp>
#include <mb/framepacing/pacer/display/DisplayErrorState.hpp>
#include <mb/framepacing/pacer/display/DisplayReport.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/frame/FrameStartPlan.hpp>
#include <mb/framepacing/pacer/frame/GpuWaitReport.hpp>
#include <mb/framepacing/pacer/frame/GpuWorkReport.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/frame/PresentWaitReport.hpp>
#include <mb/framepacing/pacer/frame/SystemWaitReport.hpp>
#include <mb/framepacing/pacer/frame/VBlankReading.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
#include <mb/framepacing/pacer/tier/ClockGridLoopPacer.hpp>
#include <mb/framepacing/pacer/tier/FreeRunLoop.hpp>
#include <mb/framepacing/pacer/tier/VBlankLoopPacer.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer.md; sdk/doc/pacer-design.md has how it came to be). The one pacer an
  //! application talks to: it is told what the application can do (its capabilities)
  //! and which of that is active now, and paces every frame the way the active set's tier says. The application controls it by
  //! the active set and by nothing else: leaving a capability out is how a mechanism is switched off.
  //!
  //! Its calls a frame, each values in and values out:
  //!   AddVBlank       a vertical blank's time, whenever the application has one (with PacerCapability::VBlankTimes active)
  //!   PlanFrame       before the frame takes anything: a present to wait for (with WaitForPresent active, or a frame callback
  //!                   that holds the loop: that present's callback), then a time
  //!   AddPresentWait  after that wait: what became of it. PlanFrame is then called again
  //!   AddGpuWait      the same for the wait for the GPU's work on an earlier frame, which the plan asks for where
  //!                   WaitForGpuWork is active and nothing else holds the loop
  //!   BeginFrame      the frame starts: its swap interval, its animation time and the marker's values
  //!   EndFrame        the CPU's work is done: the time to wait until before the present, or the time to give the present
  //!   AddPresent      after the present, before the next frame is planned
  //! and, where the application has them, AddGpuWork (the GPU's work on an earlier frame), AddSystemWait (a wait it made
  //! by itself) and AddDisplayReport (when an earlier frame was shown: statistics, DisplayErrors, and in the second major tier
  //! with the aim of low latency the display's slip).
  //!
  //! Which tier paces: where the refreshes are comes from vertical blank times when they are active and from a grid on the clock
  //! when not, the loop is held by a wait for a present when that is active, and where the present takes a time before which
  //! the frame is not shown it is given PresentPlan::NotBeforeTime, with which the display's side puts a frame on its refresh
  //! and no present is held by the loop (PacerMajorTier::DisplayPlacesAtTime). A present that takes a time the frame before it
  //! stays is given PresentPlan::MinimumDuration, and one that takes a swap interval PresentPlan::SwapInterval. Where the pacer
  //! sees where frames were shown (a wait for a present, or display times) the display's side then places a frame after the one
  //! before it (PacerMajorTier::DisplayPlacesAfterDuration, DisplayPlacementUtil::HoldsUpTo): the loop holds no present either,
  //! with the aim of smoothness a frame shown late moves the ones after it (the display stays smooth, and they wait a refresh
  //! longer), and with the aim of low latency and display times the loop starts its frames later by the refreshes the display
  //! slipped (DisplaySlipRefreshes; with a wait for a present the wait keeps the frames that wait few). Without a way to see
  //! where frames were shown the loop places the frames and the values are given next to what it does. The eight tiers where
  //! the display's side places the frame are built against the simulation's display only: no system has been measured with
  //! them.
  //!
  //! On the grid on the clock a frame callback (PacerCapability::FrameCallback) holds the loop as a wait for a present does, where
  //! the set has no such wait (PacerTierUtil::HoldsByFrameCallback, the "c" beside the tier): the plan's wait is then that
  //! present's callback, and it stops and starts again as that wait does.
  //!
  //! A set that says the display's side skips a frame that is overdue (PacerCapability::PresentSkipsOverdue) is rated with it
  //! (PacerRating::SkipsOverdue) and paced as without it: every present is taken as shown, which one that was skipped is not.
  //!
  //! A change of the active set takes effect when the frame that is open has ended (at once when none is). The frames and
  //! their ids, the animation time, the swap interval with the rule's frame window, the GPU's work and the presents that can be
  //! waited for go on. Where the frames are placed starts anew with the first frame after the change, which begins when the
  //! frame before it said the next one would.
  //!
  //! An application that stops pacing for a while (a menu, a benchmark, a display whose refresh rate is variable) says so
  //! (SetPacing) and goes on with every call: a frame is then given nothing to wait for and nothing for its present, its
  //! animation step is the time since the frame before it started, and what it reports is taken, so that pacing starts
  //! again with the vertical blanks, the presents and the GPU's work known (FreeRunLoop).
  //!
  //! Values in, values out: no platform API, no clock read, no wait. Made once (it allocates the rule's frame windows); pacing
  //! frames, changing the active set and switching pacing off and on never allocate.
  class TierPacer
  {
    PacerCapabilities m_capabilities;
    PacerCapabilities m_active;
    PacerCapabilities m_pendingActive;
    bool m_hasPendingActive{false};
    // The two ways a frame is placed, of which one paces: on a grid on the clock, or on the display's vertical blanks
    ClockGridLoopPacer m_grid;
    VBlankLoopPacer m_vblank;
    bool m_onVBlanks;
    // The frames while the application is not pacing: neither of the two places them, and the one that would keeps the
    // swap interval rule
    FreeRunLoop m_free;
    bool m_isPacing{true};
    bool m_pendingPacing{true};
    bool m_hasPendingPacing{false};
    // What the display reports say of the frames: statistics, whichever part paces
    DisplayErrorCounter m_display;

  public:
    //! The settings, and what the application can do: all of it is active.
    TierPacer(const PacerSettings& settings, const PacerCapabilities& capabilities);

    //! What the application can do changed (a swap chain made anew, a window on another display). What is active and no
    //! longer there goes out of the active set.
    void SetCapabilities(const PacerCapabilities& capabilities) noexcept;

    //! What is active now: what of the capabilities the pacer is to use. A capability the application does not have is left
    //! out. Takes effect when the open frame has ended.
    void SetActiveCapabilities(const PacerCapabilities& active) noexcept;

    //! What the application can do, as it was last given.
    [[nodiscard]] const PacerCapabilities& Capabilities() const noexcept
    {
      return m_capabilities;
    }

    //! What is active: the set the frames are paced by, or will be when the open frame has ended.
    [[nodiscard]] const PacerCapabilities& ActiveCapabilities() const noexcept
    {
      return m_hasPendingActive ? m_pendingActive : m_active;
    }

    //! Whether the frames are paced: they are unless this was switched off. Off, every call is made as before: a frame's
    //! plans are empty (no wait, no start time, no time to wait until before the present and none for it, a swap interval
    //! of one), its schedule has no intended display time and no target frame time, and its animation step is the time
    //! since the frame before it started (one refresh across a pause). The reports are taken as before, but for the waits:
    //! no plan asked for one, and a wait of the application's own (AddSystemWait) holds nothing the pacer placed. The swap
    //! interval rule gets no frame. On again, the first frame is placed anew as after a
    //! change of the active set, the frame ids and the animation time go on, and the pause after start-up is made once
    //! more where it is made at all. Takes effect when the open frame has ended.
    void SetPacing(bool isPacing) noexcept;

    //! True when the frames are paced, or will be when the open frame has ended.
    [[nodiscard]] bool IsPacing() const noexcept
    {
      return m_hasPendingPacing ? m_pendingPacing : m_isPacing;
    }

    //! The rating of what the application can do: the capability tier.
    [[nodiscard]] PacerRating Rating() const noexcept;

    //! The rating of what is active: the active tier.
    [[nodiscard]] PacerRating ActiveRating() const noexcept;

    //! The tier that is pacing this frame. Below the active tier while something a capability promised is missing (no
    //! vertical blank reading so far, waits for a present that stopped because none is shown: with a duration on the present
    //! and nothing else to see where frames were shown, the loop places the frames). While the application is not pacing
    //! (IsPacing) it is the tier that would pace.
    [[nodiscard]] PacerTier WorkingTier() const noexcept;

    //! A vertical blank of the display the window is on. Taken with PacerCapability::VBlankTimes active.
    void AddVBlank(const VBlankReading& reading) noexcept;

    //! Before a frame takes anything, at now on the application's steady clock: the present to wait for and the longest that
    //! may take, and after it the time to wait until. Either may be absent. It changes nothing, so a frame may be planned again.
    [[nodiscard]] FrameStartPlan PlanFrame(NanosecondTickCount now) const noexcept;

    //! What became of the wait for a present the plan asked for. The frame is then planned again.
    void AddPresentWait(const PresentWaitReport& report) noexcept;

    //! What became of the wait for the GPU's work the plan asked for (PacerCapability::WaitForGpuWork active and no wait for
    //! a present). The frame is then planned again.
    void AddGpuWait(const GpuWaitReport& report) noexcept;

    //! The frame starts, at cpuStartTime: the previous frame is judged, the rule decides, and this frame is planned.
    FrameSchedule BeginFrame(NanosecondTickCount cpuStartTime) noexcept;

    //! The frame's CPU work is done, at workDoneTime: the time to wait until before the present or the time to give it, and
    //! the marker's CPU busy time.
    PresentPlan EndFrame(NanosecondTickCount workDoneTime) noexcept;

    //! The frame's CPU busy time so far, for a marker that is drawn while the frame's work is still going on.
    [[nodiscard]] NanosecondTimeDuration CpuBusyAt(NanosecondTickCount now) const noexcept;

    //! After the present, before the next frame is planned: when it was called and returned, and whether the system took it.
    void AddPresent(const PresentReport& report) noexcept;

    //! The GPU's work on an earlier frame, where the application has it.
    void AddGpuWork(const GpuWorkReport& report) noexcept;

    //! A wait the application made by itself before the frame starts (for an image, for a frame slot). On the grid on the
    //! clock the system may be what paces the loop (PacerSettings::SystemHoldsLoop). On vertical blanks a start that the
    //! display's side held is not stepped over by the animation time (DisplayHeldRefreshes).
    void AddSystemWait(const SystemWaitReport& report) noexcept;

    //! What the platform says of a frame that was presented earlier: when it was shown, or that it never was. Taken with
    //! PacerCapability::DisplayTimes active, oldest first. Statistics (DisplayErrors), and in the second major tier with the
    //! aim of low latency and no wait for a present the display's slip: the loop starts its frames later by the refreshes a
    //! frame shown late moved the ones after it (DisplaySlipRefreshes). Nothing else is paced by it, and never the animation
    //! time or the swap interval.
    void AddDisplayReport(const DisplayReport& report) noexcept;

    //! What the display reports said of the frames so far: the animation error where the application runs (the "+" beside
    //! a tier), and how long a frame took from its start to its display. A pause the pacer did not ask for shows in it as
    //! one frame shown late.
    [[nodiscard]] DisplayErrorState DisplayErrors() const noexcept
    {
      return m_display.State();
    }

    //! The swap chain was made anew: the presents made so far are never shown.
    void ForgetPresents() noexcept;

    //! The display's refresh period changed: the frames start again, the animation time goes on.
    void SetRefreshPeriod(RefreshPeriod period) noexcept;

    //! Other settings: the frames start again, the animation time goes on. It may allocate.
    void SetSettings(const PacerSettings& settings);

    //! Starts again as made, with the settings and the capability sets it has; the animation time goes on.
    void Reset() noexcept;

    //! True when a vertical blank reading was taken.
    [[nodiscard]] bool HasVBlankReading() const noexcept
    {
      return m_vblank.HasVBlankReading();
    }

    //! The readings that were off where the readings before them put the vertical blanks.
    [[nodiscard]] uint64_t VBlankJumps() const noexcept
    {
      return m_vblank.VBlankJumps();
    }

    //! The refreshes the animation time is behind the clock: what was lost and not caught up with.
    [[nodiscard]] uint64_t RefreshesBehindClock() const noexcept
    {
      return m_onVBlanks ? m_vblank.RefreshesBehindClock() : m_grid.RefreshesBehindClock();
    }

    //! The pauses after start-up that were made, since the pacer was made.
    [[nodiscard]] uint64_t StartupPauses() const noexcept
    {
      return m_grid.StartupPauses() + m_vblank.StartupPauses();
    }

    //! The vertical blanks that frames were shown later than worked out, by what the waits said.
    [[nodiscard]] uint64_t ShownLaterByWaits() const noexcept
    {
      return m_vblank.ShownLaterByWaits();
    }

    //! Where in a refresh a frame is to be ready now, on the display's vertical blanks.
    [[nodiscard]] NanosecondTimeSpan ReadyPlaceNow() const noexcept
    {
      return m_vblank.ReadyPlaceNow();
    }

    //! The times the place a frame is to be ready at was tried one step later again, since the pacer was made.
    [[nodiscard]] uint64_t ReadyPlaceTries() const noexcept
    {
      return m_vblank.ReadyPlaceTries();
    }

    //! The tries that were taken back, because a frame was shown later in the frames after one.
    [[nodiscard]] uint64_t ReadyPlaceTriesTakenBack() const noexcept
    {
      return m_vblank.ReadyPlaceTriesTakenBack();
    }

    //! True while no wait for a present is made because the waits ran out (PresentWaitRule).
    [[nodiscard]] bool PresentWaitsStopped() const noexcept
    {
      return m_onVBlanks ? m_vblank.PresentWaitsStopped() : m_grid.PresentWaitsStopped();
    }

    //! The waits for a present that ended without the present being shown, since the pacer was made.
    [[nodiscard]] uint64_t PresentWaitTimeouts() const noexcept
    {
      return m_onVBlanks ? m_vblank.PresentWaitTimeouts() : m_grid.PresentWaitTimeouts();
    }

    //! The waits for the GPU's work that ended without the GPU done with the frame, since the pacer was made.
    [[nodiscard]] uint64_t GpuWaitTimeouts() const noexcept
    {
      return m_onVBlanks ? m_vblank.GpuWaitTimeouts() : m_grid.GpuWaitTimeouts();
    }

    //! The refreshes frame starts were late by while the display's side held the loop, on vertical blanks: not stepped over
    //! by the animation time, and counted in RefreshesBehindClock.
    [[nodiscard]] uint64_t DisplayHeldRefreshes() const noexcept
    {
      return m_vblank.DisplayHeldRefreshes();
    }

    //! The refreshes the loop started its frames later by for the display's slip (AddDisplayReport), by both parts that place
    //! frames: counted in RefreshesBehindClock.
    [[nodiscard]] uint64_t DisplaySlipRefreshes() const noexcept
    {
      return m_grid.DisplaySlipRefreshes() + m_vblank.DisplaySlipRefreshes();
    }

    //! The frames whose start the display's side held for a share of a refresh period, on the grid on the clock.
    [[nodiscard]] uint64_t SystemHeldFrames() const noexcept
    {
      return m_grid.SystemHeldFrames();
    }

    //! The frames whose start a wait for a frame slot held for a share of a refresh period, on the grid on the clock.
    [[nodiscard]] uint64_t FrameSlotHeldFrames() const noexcept
    {
      return m_grid.FrameSlotHeldFrames();
    }

    //! The GPU time a frame is judged with: the newest that was reported, zero without one.
    [[nodiscard]] NanosecondTimeDuration GpuTime() const noexcept
    {
      return !m_isPacing ? m_free.GpuTime() : (m_onVBlanks ? m_vblank.GpuTime() : m_grid.GpuTime());
    }

    //! How long the last present held the frame loop.
    [[nodiscard]] NanosecondTimeDuration LastPresentBlocked() const noexcept
    {
      return !m_isPacing ? m_free.LastPresentBlocked() : (m_onVBlanks ? m_vblank.LastPresentBlocked() : m_grid.LastPresentBlocked());
    }

    //! What the rule's frame window holds.
    [[nodiscard]] FrameWindowState FrameWindow() const noexcept
    {
      return m_onVBlanks ? m_vblank.FrameWindow() : m_grid.FrameWindow();
    }

    //! The swap interval of the frames now.
    [[nodiscard]] uint32_t SwapInterval() const noexcept
    {
      return m_onVBlanks ? m_vblank.SwapInterval() : m_grid.SwapInterval();
    }

    //! The refresh period the pacer is on.
    [[nodiscard]] RefreshPeriod Refresh() const noexcept
    {
      return m_onVBlanks ? m_vblank.Refresh() : m_grid.Refresh();
    }

    //! The settings the pacer has.
    [[nodiscard]] const PacerSettings& Settings() const noexcept
    {
      return m_onVBlanks ? m_vblank.Settings() : m_grid.Settings();
    }

  private:
    [[nodiscard]] bool IsFrameOpen() const noexcept;
    void Apply(const PacerCapabilities& active) noexcept;
    void ApplyPacing(bool isPacing) noexcept;
    void ApplyPending() noexcept;
    [[nodiscard]] const SwapIntervalRule& Rule() const noexcept;
  };
}

#endif
