#ifndef MB_FRAMEPACING_PACER_SIMULATION_TIMERPERIODONLYPACER_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_TIMERPERIODONLYPACER_HPP
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
#include <mb/framepacing/pacer/frame/SystemWaitReport.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
#include <mb/framepacing/pacer/tier/TierPacer.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! Test code (the pacer module, sdk/doc/pacer-design.md): TierPacer with its capabilities fixed, as the tier's pacer was
  //! first built and as its tests still use it. The pacer of the lowest tier, PacerTier::TimerPeriodOnly, the
  //! baseline: a whole pacer by itself, for an application with a steady clock, the refresh period of the display its
  //! window is on, a wait until a time, and a present that shows every frame in order for at least a refresh. Every application has
  //! that, so every capability set reaches this pacer.
  //!
  //! It holds every rule and every time calculation, and the application carries out what it is given. Four calls a frame:
  //!   PlanFrame   before the frame takes anything: the time to wait until
  //!   BeginFrame  the frame starts: its swap interval, its animation time and the marker's values
  //!   EndFrame    the CPU's work is done: the time to wait until before the present
  //!   AddPresent  after the present, before the next frame is planned: when it was called and returned, and whether the
  //!               system took it
  //! and, where the application has it, AddGpuWork: the GPU's work on an earlier frame.
  //!
  //! It can not see the display. What it has is a count: the clock says how many refresh periods have passed, and it knows how
  //! many it gave its frames. So it keeps the frames on one grid of refresh periods on the clock. Step 0 is the first frame's
  //! start, and every frame is for a step a whole number of periods from it. A frame held for more than one refresh is presented
  //! by the loop on a timer, in the period before the step the next frame is due at, a margin into it
  //! (PacerSettings::FrameMargin). A guess: the grid's place against the display's refreshes is not known.
  //!
  //! What it does with the frames that wait to be shown is its aim (PacerSettings::Aim), and it has both.
  //!
  //! PacerAim::Smoothness, the default: at one refresh per frame it keeps a reserve. PacerSettings::WaitingPresents less one
  //! frames are made ahead of the display: a frame starts that many periods before its step, so the first ones of a start are
  //! made back to back, and from then on the display has that many frames in hand. Where in a refresh a present lands then
  //! matters less, and a frame that runs long is covered for as many refreshes.
  //!   - A frame that is late within the reserve gives up no step: the frames after it are made back to back until the loop is
  //!     where it was, so the reserve is there again and the animation time stays with the clock.
  //!   - What is beyond the reserve is given up: the display showed a frame again for those refreshes. How late a frame was
  //!     is read from its present (the whole periods it came after the time its swap interval gave it) and from the next
  //!     frame's start. This pacer gives up the steps it is sure of and no more, so after a long frame the frames that wait
  //!     are the reserve or one more.
  //!   - There is no pause after start-up, which would take the reserve away.
  //! At two refreshes per frame or more nothing can wait (the display takes a frame before the next is made), and there is no
  //! reserve.
  //!
  //! PacerAim::LowLatency: the frames that wait are kept as few as this tier can.
  //!   - A frame that starts less than half a period late keeps its step and starts at once: that frame is late, no frame after
  //!     it is.
  //!   - A frame that would start later than that takes the step nearest to where the loop is, and waits for it when it is still
  //!     to come: the frame before it ran long, the steps in between are lost, and the loop is back where it was against the
  //!     display, whatever that place is.
  //!   - After a frame whose present was made later than the step the next frame was due at, the next present comes a whole
  //!     period after it: the next step when the late present was made no later in its step than the last present that was
  //!     on time, and else the step after. Two presents less than a period apart can reach the display between the same two
  //!     refreshes, and one of them then waits to be shown for as long as the loop runs. The price is a refresh more after
  //!     most long frames, on a display where the next step would have done.
  //!   - For the frames that pile up behind the first presents of a new swap chain it pauses once:
  //!     PacerSettings::StartupPauseDelay after the first frame of a start, and never before a present was taken, the frame
  //!     after is due PacerSettings::StartupPauseRefreshes later. The frame on screen stays there through the pause. A guess,
  //!     made once per start and once per swap chain made anew, and not at all when the pacer is at two refreshes per frame or
  //!     more then.
  //!
  //! With either aim it does not learn of a frame that waits although it was ready in time, and the reserve is a count, not
  //! something it sees: a refresh period that is a little off the display's makes the frames that wait one more or one fewer
  //! over time.
  //!
  //! The swap interval rule (SwapIntervalRule) decides each frame's swap interval from how the frames did: a frame is late when
  //! it took more steps of the grid than its swap interval, or when its work was longer than its swap interval's time. A
  //! frame's work is the CPU's, from BeginFrame to EndFrame, and with GPU work reports the GPU's too, put together by how the
  //! two lie in time (FrameWorkRule).
  //!
  //! The animation time advances by a frame's swap interval. It is not moved to catch up with the clock after a refresh was
  //! lost: that would be a second step that does not match the display's. One thing more is added to a step, a loss that
  //! repeats: when the frame before took refreshes more than it was given and the one before that did too, the display shows
  //! every frame for that much longer, and the step is longer by the fewer of the two. RefreshesBehindClock() says how far
  //! the animation time is behind the clock.
  //!
  //! Values in, values out: no platform API, no clock read, no wait. Made once (it allocates the rule's frame window); pacing
  //! frames never allocates.
  //!
  //! The class is TierPacer with this tier's capabilities and no others: the rules and the calculations are in the parts it is
  //! put together from.
  class TimerPeriodOnlyPacer
  {
    TierPacer m_pacer;

  public:
    //! The tier this pacer is for.
    static constexpr PacerTier Tier = PacerTier::TimerPeriodOnly;

    explicit TimerPeriodOnlyPacer(const PacerSettings& settings)
      : m_pacer(settings, PacerCapabilities())
    {
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

    //! A wait the application made by itself before the frame starts: for an image, or for a frame slot.
    void AddSystemWait(const SystemWaitReport& report) noexcept
    {
      m_pacer.AddSystemWait(report);
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

    //! The pauses after start-up that were made, since the pacer was made.
    [[nodiscard]] uint64_t StartupPauses() const noexcept
    {
      return m_pacer.StartupPauses();
    }

    //! The frames whose start the display's side held for a share of a refresh period (a wait for an image, a present that waited).
    [[nodiscard]] uint64_t SystemHeldFrames() const noexcept
    {
      return m_pacer.SystemHeldFrames();
    }

    //! The frames whose start a wait for a frame slot held for a share of a refresh period: the GPU's doing.
    [[nodiscard]] uint64_t FrameSlotHeldFrames() const noexcept
    {
      return m_pacer.FrameSlotHeldFrames();
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
