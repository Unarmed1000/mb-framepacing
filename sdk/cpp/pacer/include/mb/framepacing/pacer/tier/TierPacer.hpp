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
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/frame/FrameStartPlan.hpp>
#include <mb/framepacing/pacer/frame/GpuWorkReport.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/frame/PresentWaitReport.hpp>
#include <mb/framepacing/pacer/frame/SystemWaitReport.hpp>
#include <mb/framepacing/pacer/frame/VBlankReading.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
#include <mb/framepacing/pacer/tier/ClockGridLoopPacer.hpp>
#include <mb/framepacing/pacer/tier/VBlankLoopPacer.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built; FramePacer is the pacer the library has
  //! until this replaces it). The one pacer an application talks to: it is told what the application can do (its capabilities)
  //! and which of that is active now, and paces every frame the way the active set's tier says. The application controls it by
  //! the active set and by nothing else: leaving a capability out is how a mechanism is switched off.
  //!
  //! Its calls a frame, each values in and values out:
  //!   AddVBlank       a vertical blank's time, whenever the application has one (with PacerCapability::VBlankTimes active)
  //!   PlanFrame       before the frame takes anything: a present to wait for (with WaitForPresent active), then a time
  //!   AddPresentWait  after that wait: what became of it. PlanFrame is then called again
  //!   BeginFrame      the frame starts: its swap interval, its animation time and the marker's values
  //!   EndFrame        the CPU's work is done: the time to wait until before the present
  //!   AddPresent      after the present, before the next frame is planned
  //! and, where the application has them, AddGpuWork (the GPU's work on an earlier frame) and AddSystemWait (a wait it made
  //! by itself).
  //!
  //! Which tier paces: where the refreshes are comes from vertical blank times when they are active and from a grid on the clock
  //! when not, and the loop is held by a wait for a present when that is active. Those are tiers 5 to 8 of PacerTier. A timed
  //! present is rated (tiers 1 to 4) and not used yet: a set that has one is paced as the same set without it, and
  //! WorkingTier says which tier that is.
  //!
  //! A change of the active set takes effect when the frame that is open has ended (at once when none is). The frames and
  //! their ids, the animation time, the swap interval with the rule's frame window, the GPU's work and the presents that can be
  //! waited for go on. Where the frames are placed starts anew with the first frame after the change, which begins when the
  //! frame before it said the next one would.
  //!
  //! Values in, values out: no platform API, no clock read, no wait. Made once (it allocates the rule's frame windows); pacing
  //! frames and changing the active set never allocate.
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

    //! The rating of what the application can do: the capability tier.
    [[nodiscard]] PacerRating Rating() const noexcept;

    //! The rating of what is active: the active tier.
    [[nodiscard]] PacerRating ActiveRating() const noexcept;

    //! The tier that is pacing this frame. Below the active tier while something a capability promised is missing: a timed
    //! present (not used yet), no vertical blank reading so far, waits for a present that stopped because none is shown.
    [[nodiscard]] PacerTier WorkingTier() const noexcept;

    //! A vertical blank of the display the window is on. Taken with PacerCapability::VBlankTimes active.
    void AddVBlank(const VBlankReading& reading) noexcept;

    //! Before a frame takes anything, at now on the application's steady clock: the present to wait for and the longest that
    //! may take, and after it the time to wait until. Either may be absent. It changes nothing, so a frame may be planned again.
    [[nodiscard]] FrameStartPlan PlanFrame(NanosecondTickCount now) const noexcept;

    //! What became of the wait for a present the plan asked for. The frame is then planned again.
    void AddPresentWait(const PresentWaitReport& report) noexcept;

    //! The frame starts, at cpuStartTime: the previous frame is judged, the rule decides, and this frame is planned.
    FrameSchedule BeginFrame(NanosecondTickCount cpuStartTime) noexcept;

    //! The frame's CPU work is done, at workDoneTime: the time to wait until before the present, and the marker's CPU busy time.
    PresentPlan EndFrame(NanosecondTickCount workDoneTime) noexcept;

    //! The frame's CPU busy time so far, for a marker that is drawn while the frame's work is still going on.
    [[nodiscard]] NanosecondTimeDuration CpuBusyAt(NanosecondTickCount now) const noexcept;

    //! After the present, before the next frame is planned: when it was called and returned, and whether the system took it.
    void AddPresent(const PresentReport& report) noexcept;

    //! The GPU's work on an earlier frame, where the application has it.
    void AddGpuWork(const GpuWorkReport& report) noexcept;

    //! A wait the application made by itself before the frame starts (for an image, for a frame slot). Used on the grid on the
    //! clock, where the system may be what paces the loop.
    void AddSystemWait(const SystemWaitReport& report) noexcept;

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
      return m_onVBlanks ? m_vblank.GpuTime() : m_grid.GpuTime();
    }

    //! How long the last present held the frame loop.
    [[nodiscard]] NanosecondTimeDuration LastPresentBlocked() const noexcept
    {
      return m_onVBlanks ? m_vblank.LastPresentBlocked() : m_grid.LastPresentBlocked();
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
    void ApplyPending() noexcept;
  };
}

#endif
