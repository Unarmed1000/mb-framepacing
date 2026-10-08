#ifndef MB_FRAMEPACING_PACER_TIER_CLOCKGRIDLOOPPACER_HPP
#define MB_FRAMEPACING_PACER_TIER_CLOCKGRIDLOOPPACER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/RefreshTime.hpp>
#include <mb/framepacing/pacer/capability/PacerTier.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/frame/FrameStartPlan.hpp>
#include <mb/framepacing/pacer/frame/GpuWaitReport.hpp>
#include <mb/framepacing/pacer/frame/GpuWorkReport.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/frame/PresentWaitReport.hpp>
#include <mb/framepacing/pacer/frame/SystemWaitReport.hpp>
#include <mb/framepacing/pacer/hold/GpuWaitRule.hpp>
#include <mb/framepacing/pacer/hold/PresentWaitRule.hpp>
#include <mb/framepacing/pacer/placement/PresentTiming.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
#include <mb/framepacing/pacer/rule/FrameWorkRule.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalRule.hpp>
#include <mb/framepacing/pacer/tier/PacerHandover.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built). The frame loop places every frame
  //! on a step of a grid of refresh periods on the clock: what the tiers without vertical blank times have in common, with
  //! the wait for a present and the timed present as options. TimerPeriodOnlyPacer is this without the wait and
  //! TimerWaitForPresentPacer with it, and what each of them promises is said there.
  //!
  //! A frame starts at its step of the grid, its swap interval after the one before it. A frame that ran long costs whole
  //! steps, and the loop is then where it was against the display. Where the grid sits in a refresh is not known. With the
  //! aim of smoothness a reserve of frames is made ahead of the display at one refresh per frame; with low latency none is.
  //! A frame of more than one refresh is presented in the period before the step the next frame is due at.
  //!
  //! With a timed present (SetPresentTiming; the simulation only, not measured) the present is given a time
  //! (DisplayPlacementUtil). A time before which the frame is not shown has the display's side show it at the refresh
  //! nearest to the step it is due at: the loop holds no present, and a frame is presented when it is done. A time the
  //! frame before it stays on screen at least is given next to what the loop does without one: it keeps a present that
  //! lands on the wrong side of a refresh from being shown a refresh early. Where the frames start is as without one.
  //!
  //! Without the wait: one pause after start-up with the aim of low latency (StartupPauses), a whole period after a present
  //! that was made late, and the system's own waits (AddSystemWait): where the application says that the system holds the
  //! loop while its queue is full, the aim of smoothness lets the system pace the loop. With the wait: the loop is held
  //! until the present so many back was shown, the grid follows the ends of the waits that held the loop, and a window that
  //! is not shown stops the waits (PresentWaitsStopped).
  //!
  //! Values in, values out: no platform API, no clock read, no wait. Made once (it allocates the rule's frame window); pacing
  //! frames never allocates.
  class ClockGridLoopPacer
  {
    SwapIntervalRule m_rule;
    // What holds the loop: a wait for a present, or nothing but the times the pacer gives
    bool m_waitsForPresent;
    // The time a present is given. With PresentTiming::AtTime the display's side puts a frame on its refresh, else the loop
    // does by when it presents
    PresentTiming m_presentTiming{PresentTiming::Untimed};
    PresentWaitRule m_wait;
    // Without a wait for a present: a wait for the GPU's work on an earlier frame, where the application can make one
    bool m_waitsForGpuWork{false};
    GpuWaitRule m_gpuWait;
    // The grid on the clock: step 0 is at m_origin, the frame that started last is on m_slot and the next is due at m_nextSlot
    NanosecondTickCount m_origin;
    int64_t m_slot{0};
    int64_t m_nextSlot{0};
    bool m_hasGrid{false};
    // The step the frame is due to leave the grid at (the next frame's step, but for a pause), when its present was made, and
    // where in its step a present is made that is on time
    int64_t m_dueSlot{0};
    NanosecondTickCount m_presentTime;
    bool m_hasPresentTime{false};
    NanosecondTimeSpan m_presentPlace;
    // The frame between BeginFrame and the next BeginFrame
    uint64_t m_frameId{0};
    NanosecondTickCount m_startTime;
    uint32_t m_swapInterval{1};
    NanosecondTimeSpan m_work;
    bool m_frameOpen{false};
    bool m_frameEnded{false};
    // The steps of the grid the frame before it took more than it was given, and the steps the frame is behind its own
    int64_t m_lost{0};
    int64_t m_behind{0};
    FrameWorkRule m_frameWork;
    RefreshTime m_animationTime;
    NanosecondTimeSpan m_lastAnimationTime;
    uint64_t m_refreshesBehindClock{0};
    NanosecondTimeDuration m_lastPresentBlocked;
    // The waits since the last frame started in which the display's side held the loop (for an image, in the present) and
    // in which the GPU did (for a frame slot): how long, and the frames each held
    NanosecondTimeSpan m_displayHeld;
    NanosecondTimeSpan m_frameSlotHeld;
    // The part of the display's side's waits that came after the time the pacer holds the loop to before the next frame
    NanosecondTimeSpan m_displayHeldPastTimer;
    uint64_t m_systemHeldFrames{0};
    uint64_t m_frameSlotHeldFrames{0};
    // The pause after start-up: still to be made, whether a wait for a present held a frame since it was asked for, the
    // first frame's start since then, and whether the system took a present since
    bool m_pausePending{true};
    bool m_pauseHeldByWait{false};
    bool m_pauseHasFirstFrame{false};
    NanosecondTickCount m_pauseFirstFrameTime;
    bool m_presentTaken{false};
    uint64_t m_startupPauses{0};
    // Another pacer placed the frames before this one took over: the next frame is the first it places, held to the time
    // the frame before it gave. And the time each frame gives for the one after it
    bool m_takenOver{false};
    NanosecondTickCount m_takeOverStartTime;
    // Where the grid's step for that frame is, when the part before said when the next present would be made
    bool m_hasTakeOverOrigin{false};
    NanosecondTickCount m_takeOverOrigin;
    NanosecondTickCount m_nextFrameStartTime;

  public:
    //! waitsForPresent: the application can wait until a present was shown, and the frame start plan asks for it.
    ClockGridLoopPacer(const PacerSettings& settings, bool waitsForPresent);

    //! The tier this pacer paces as: with the wait or without it, and with a timed present or without one.
    [[nodiscard]] PacerTier Tier() const noexcept
    {
      if (m_presentTiming != PresentTiming::Untimed)
      {
        return m_waitsForPresent ? PacerTier::TimedTimerWaitForPresent : PacerTier::TimedTimerPeriodOnly;
      }
      return m_waitsForPresent ? PacerTier::TimerWaitForPresent : PacerTier::TimerPeriodOnly;
    }

    //! Before a frame takes anything, at now on the application's steady clock: the time of the step the frame is due at, when
    //! that is still to come. It changes nothing, so a frame may be planned again.
    [[nodiscard]] FrameStartPlan PlanFrame(NanosecondTickCount now) const noexcept;

    //! What became of the wait for a present the plan asked for. A wait that held the loop for a share of a refresh period and
    //! ended with the present shown moves the grid towards its end. One that ended without it is counted
    //! (PresentWaitTimeouts), and the frame it held is not judged: the pacer asked for the wait, so the frame is not late, and
    //! the grid goes on from where that frame starts. After PresentWaitRule::WaitsRunOutToStop of them in a row the display is not taking the
    //! window's frames (a window that is covered or minimised): the pacer stops waiting (PresentWaitsStopped) until presents
    //! are shown again. While it is stopped the report is the answer to what the plan asked, and a frame the asking held is
    //! not judged either.
    void AddPresentWait(const PresentWaitReport& report) noexcept;

    //! What became of the wait for the GPU's work the plan asked for. One that ended without the GPU done is counted
    //! (GpuWaitTimeouts). The frame is then planned again.
    void AddGpuWait(const GpuWaitReport& report) noexcept;

    //! The frame starts, at cpuStartTime: the previous frame is judged, the rule decides, and this frame is planned.
    FrameSchedule BeginFrame(NanosecondTickCount cpuStartTime) noexcept;

    //! The frame's CPU work is done, at workDoneTime: how to present it.
    PresentPlan EndFrame(NanosecondTickCount workDoneTime) noexcept;

    //! The frame's CPU busy time so far, at now, for a marker that is drawn while the frame's work is still going on: from the
    //! frame's start to now. Zero: no frame is open.
    [[nodiscard]] NanosecondTimeDuration CpuBusyAt(NanosecondTickCount now) const noexcept;

    //! After the present, before the next frame is planned: when it was called is what the next frame's step is kept away
    //! from when the frame ran long (without the report it is taken as made when EndFrame said). A present the system did not
    //! take says the swap chain is gone: the one made after it gets the pause of a start (ForgetPresents).
    void AddPresent(const PresentReport& report) noexcept;

    //! The GPU's work on an earlier frame, when the application has it: from then on a frame's work is the CPU's and the
    //! GPU's (FrameWorkRule).
    void AddGpuWork(const GpuWorkReport& report) noexcept;

    //! A wait of the application's own before the frame that is about to start (for a frame slot, for an image), after the
    //! wait and before BeginFrame. A wait for an image, like a present that waited (AddPresent), is the display's side
    //! holding the loop while its queue is full. With PacerSettings::SystemHoldsLoop and the aim of smoothness, at one
    //! refresh per frame, a frame whose start that held for a share of a refresh period is one the system let through when
    //! it had room: it is not late and loses no step, and the grid is moved to its start, so the loop is paced by the
    //! display and does not drift. A wait for a frame slot is the GPU's: it excuses nothing (a frame that is late by it is
    //! late by the GPU's work) and is counted. Without the setting the reports are counted and change nothing.
    void AddSystemWait(const SystemWaitReport& report) noexcept;

    //! The presents made so far are gone (a swap chain was made anew, for a window that is resized, say): the pause after
    //! start-up is made once more, counted from the next frame. Nothing else changes: the grid, the frame window and the swap
    //! interval go on.
    void ForgetPresents() noexcept;

    //! The display's refresh period changed (a mode change, the window on another display): the grid starts again on it with an
    //! empty frame window, at the swap interval the application prefers there. The animation time goes on.
    void SetRefreshPeriod(RefreshPeriod period) noexcept;

    //! Other settings on a live pacer: it starts again with them, as with another refresh period. The same settings change
    //! nothing. Allocates when the frame window needs more room than it has, and only then.
    void SetSettings(const PacerSettings& settings);

    //! Start again (after a pause the application knows of): the next frame starts the grid, the frame window is empty, the swap
    //! interval the preferred one, the GPU's work is forgotten, and the pause after start-up is made once more. The animation
    //! time goes on.
    void Reset() noexcept;

    //! True between a frame's start and its end: a change of what paces waits for the end.
    [[nodiscard]] bool IsFrameOpen() const noexcept
    {
      return m_frameOpen && !m_frameEnded;
    }

    //! The swap interval rule, for a pacer that takes over.
    [[nodiscard]] const SwapIntervalRule& Rule() const noexcept
    {
      return m_rule;
    }

    //! What goes on when another pacer takes over: the frames, the animation time, the GPU's work and the presents.
    [[nodiscard]] PacerHandover GiveOver() const noexcept;

    //! Go on from another pacer, which has the same settings: its frames and their ids, its animation time, its swap interval
    //! and the rule's frame window. Where the frames are placed starts with the next frame, which is held to the time the frame
    //! before it gave for it and is not judged against it. Never allocates.
    void TakeOver(const PacerHandover& handover, const SwapIntervalRule& rule) noexcept;

    //! Whether the frame start plan asks for a wait for a present, from the next frame on. A loop that such a wait held has
    //! no frames piled up behind its first presents, so giving the wait up then starts no pause after start-up. Given up
    //! before it held a frame (before the first one, or the first of a new swap chain), the pause is still to be made.
    //! The wait held the loop, not the grid's times: when it is given up the grid starts again at the next frame, which
    //! begins a swap interval after the last one and is not judged, as after a handover.
    void SetWaitsForPresent(bool waitsForPresent) noexcept;

    //! The time the presents are given, from the next frame on: none (the loop presents at the right moment), or the one the
    //! display's side places the frame by.
    void SetPresentTiming(const PresentTiming timing) noexcept
    {
      m_presentTiming = timing;
    }

    //! The time the presents are given.
    [[nodiscard]] PresentTiming Timing() const noexcept
    {
      return m_presentTiming;
    }

    //! How far the animation time is behind the clock, in refreshes, since the pacer was made: the refreshes that were lost and
    //! that it was not moved over, and the pauses after start-up.
    [[nodiscard]] uint64_t RefreshesBehindClock() const noexcept
    {
      return m_refreshesBehindClock;
    }

    //! The pauses after start-up that were made, since the pacer was made.
    [[nodiscard]] uint64_t StartupPauses() const noexcept
    {
      return m_startupPauses;
    }

    //! True while the pacer does not wait for presents, because its waits ran out: the frames are paced on the timer, and
    //! every PresentWaitRule::FramesBetweenAsks frames the plan asks, with no time to wait, whether an older present was shown (one that has
    //! had the time a wait would have given it, and no older than the first whose wait ran out). PresentWaitRule::AsksShownToWait answers in
    //! a row that say shown end it.
    [[nodiscard]] bool PresentWaitsStopped() const noexcept
    {
      return m_wait.Stopped();
    }

    //! The waits for a present that ended without the present being shown, since the pacer was made.
    [[nodiscard]] uint64_t PresentWaitTimeouts() const noexcept
    {
      return m_wait.Timeouts();
    }

    //! Whether the frame start plan asks for a wait for the GPU's work on an earlier frame, from the next frame on. It
    //! does where there is no wait for a present.
    void SetWaitsForGpuWork(const bool waitsForGpuWork) noexcept
    {
      m_waitsForGpuWork = waitsForGpuWork;
    }

    //! The waits for the GPU's work that ended without the GPU done, since the pacer was made.
    [[nodiscard]] uint64_t GpuWaitTimeouts() const noexcept
    {
      return m_gpuWait.Timeouts();
    }

    //! The GPU time a frame is judged with: the newest that was reported, zero without one.
    [[nodiscard]] NanosecondTimeDuration GpuTime() const noexcept
    {
      return m_frameWork.GpuTime();
    }

    //! How long the last present that was reported held the frame loop.
    //! The frames whose start the display's side held (a wait for an image, a present that waited: for an eighth of a refresh
    //! period or more), since the pacer was made.
    [[nodiscard]] uint64_t SystemHeldFrames() const noexcept
    {
      return m_systemHeldFrames;
    }

    //! The frames whose start a wait for a frame slot held as long, since the pacer was made: the GPU was not done with an
    //! earlier frame.
    [[nodiscard]] uint64_t FrameSlotHeldFrames() const noexcept
    {
      return m_frameSlotHeldFrames;
    }

    [[nodiscard]] NanosecondTimeDuration LastPresentBlocked() const noexcept
    {
      return m_lastPresentBlocked;
    }

    [[nodiscard]] FrameWindowState FrameWindow() const noexcept
    {
      return m_rule.FrameWindow();
    }

    //! The swap interval the next frame is paced at.
    [[nodiscard]] uint32_t SwapInterval() const noexcept
    {
      return m_rule.SwapInterval();
    }

    [[nodiscard]] RefreshPeriod Refresh() const noexcept
    {
      return m_rule.Refresh();
    }

    [[nodiscard]] const PacerSettings& Settings() const noexcept
    {
      return m_rule.Settings();
    }

  private:
    [[nodiscard]] bool StartsAgainAt(NanosecondTickCount time) const noexcept;
    [[nodiscard]] int64_t SlotFor(NanosecondTickCount time) const noexcept;
    [[nodiscard]] int64_t SlotAfterPresent() const noexcept;
    [[nodiscard]] int64_t Reserve() const noexcept;
    [[nodiscard]] uint32_t FramesInFlightNow() const noexcept;
    [[nodiscard]] bool LetsTheSystemPace() const noexcept;
    [[nodiscard]] bool HeldByTheDisplaysSide() const noexcept;
    [[nodiscard]] bool LetThroughByTheDisplay() const noexcept;
    [[nodiscard]] NanosecondTimeDuration HeldPastTheTimer(NanosecondTickCount beginTime, NanosecondTickCount endTime) const noexcept;
    [[nodiscard]] NanosecondTickCount StartTimeOf(int64_t slot) const noexcept;
    [[nodiscard]] NanosecondTickCount DueTime(int64_t slot) const noexcept;
    [[nodiscard]] int64_t SmoothSlotFor(NanosecondTickCount time) const noexcept;
    [[nodiscard]] NanosecondTickCount TimeOfSlot(int64_t slot) const noexcept;
    void ArmStartupPause() noexcept;
    [[nodiscard]] uint32_t StartupPauseAt(NanosecondTickCount cpuStartTime) noexcept;
  };
}

#endif
