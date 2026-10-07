#ifndef MB_FRAMEPACING_PACER_TIER_TIMERPERIODONLYPACER_HPP
#define MB_FRAMEPACING_PACER_TIER_TIMERPERIODONLYPACER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeDuration.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/RefreshTime.hpp>
#include <mb/framepacing/pacer/capability/HoldTier.hpp>
#include <mb/framepacing/pacer/capability/QueueTier.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/frame/FrameStartPlan.hpp>
#include <mb/framepacing/pacer/frame/GpuWorkReport.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
#include <mb/framepacing/pacer/rule/FrameWorkRule.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalRule.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built one tier's pacer at a time; FramePacer is
  //! the pacer the library has until they replace it). The pacer of the lowest pair of tiers, HoldTier::Timer with
  //! QueueTier::PeriodOnly: a whole pacer by itself, for an application with a steady clock, the refresh period of the display its
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
  //! many it gave its frames. So it keeps the frame starts on one grid of refresh periods on the clock. Step 0 is the first frame's
  //! start, and a frame is due at a whole number of steps from it, whatever the frames before it did:
  //!   - a frame that starts less than half a period late keeps its step and starts at once: that frame is late, no frame after
  //!     it is;
  //!   - a frame that would start later than that takes the step nearest to where the loop is, and waits for it when it is still
  //!     to come: the frame before it ran long, the steps in between are lost, and the loop is back where it was against the
  //!     display, whatever that place is;
  //!   - after a frame whose present was made later than the step the next frame was due at, the next present comes a whole
  //!     period after it: the next step when the late present was made no later in its step than the last present that was
  //!     on time, and else the step after. Two presents less than a period apart can reach the display between the same two
  //!     refreshes, and one of them then waits to be shown for as long as the loop runs. The price is a refresh more after
  //!     most long frames, on a display where the next step would have done;
  //!   - a frame held for more than one refresh is presented by the loop on a timer, in the period before the step the next frame
  //!     is due at, a margin into it (PacerSettings::FrameMargin). A guess: the grid's place against the display's refreshes is
  //!     not known.
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
  //! What it does not do: take a frame away that waits to be shown although its frame was ready in time. It does not learn of
  //! one. At two refreshes per frame or more such a frame is gone by itself; at one it stays. For the frames that pile up
  //! behind the first presents of a new swap chain it pauses once: PacerSettings::StartupPauseDelay after the first frame of
  //! a start, and never before a present was taken, the frame after is due PacerSettings::StartupPauseRefreshes later. The
  //! frame on screen stays there through the pause. A guess, made once per start and once per swap chain made anew, and
  //! not at all when the pacer is at two refreshes per frame or more then.
  //!
  //! Values in, values out: no platform API, no clock read, no wait. Made once (it allocates the rule's frame window); pacing
  //! frames never allocates.
  class TimerPeriodOnlyPacer
  {
    SwapIntervalRule m_rule;
    // The grid on the clock: step 0 is at m_origin, the frame that started last is on m_slot and the next is due at m_nextSlot
    TickCount64 m_origin;
    int64_t m_slot{0};
    int64_t m_nextSlot{0};
    bool m_hasGrid{false};
    // The step the frame is due to leave the grid at (the next frame's step, but for a pause), when its present was made, and
    // where in its step a present is made that is on time
    int64_t m_dueSlot{0};
    TickCount64 m_presentTime;
    bool m_hasPresentTime{false};
    TimeSpan m_presentPlace;
    // The frame between BeginFrame and the next BeginFrame
    uint64_t m_frameId{0};
    TickCount64 m_startTime;
    uint32_t m_swapInterval{1};
    TimeSpan m_work;
    bool m_frameOpen{false};
    bool m_frameEnded{false};
    // The steps of the grid the frame before it took more than it was given
    int64_t m_lost{0};
    FrameWorkRule m_frameWork;
    RefreshTime m_animationTime;
    TimeSpan m_lastAnimationTime;
    uint64_t m_refreshesBehindClock{0};
    TimeDuration m_lastPresentBlocked;
    // The pause after start-up: still to be made, the first frame's start since it was asked for, and whether the system
    // took a present since
    bool m_pausePending{true};
    bool m_pauseHasFirstFrame{false};
    TickCount64 m_pauseFirstFrameTime;
    bool m_presentTaken{false};
    uint64_t m_startupPauses{0};

  public:
    //! The tiers this pacer is for.
    static constexpr HoldTier Hold = HoldTier::Timer;
    static constexpr QueueTier Queue = QueueTier::PeriodOnly;

    explicit TimerPeriodOnlyPacer(const PacerSettings& settings);

    //! Before a frame takes anything, at now on the application's steady clock: the time of the step the frame is due at, when
    //! that is still to come. It changes nothing, so a frame may be planned again.
    [[nodiscard]] FrameStartPlan PlanFrame(TickCount64 now) const noexcept;

    //! The frame starts, at cpuStartTime: the previous frame is judged, the rule decides, and this frame is planned.
    FrameSchedule BeginFrame(TickCount64 cpuStartTime) noexcept;

    //! The frame's CPU work is done, at workDoneTime: how to present it.
    PresentPlan EndFrame(TickCount64 workDoneTime) noexcept;

    //! The frame's CPU busy time so far, at now, for a marker that is drawn while the frame's work is still going on: from the
    //! frame's start to now. Zero: no frame is open, or it does not fit the marker's field.
    [[nodiscard]] TimeSpan32 CpuBusyAt(TickCount64 now) const noexcept;

    //! After the present, before the next frame is planned: when it was called is what the next frame's step is kept away
    //! from when the frame ran long (without the report it is taken as made when EndFrame said). A present the system did not
    //! take says the swap chain is gone: the one made after it gets the pause of a start (ForgetPresents).
    void AddPresent(const PresentReport& report) noexcept;

    //! The GPU's work on an earlier frame, when the application has it: from then on a frame's work is the CPU's and the
    //! GPU's (FrameWorkRule).
    void AddGpuWork(const GpuWorkReport& report) noexcept;

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

    //! The GPU time a frame is judged with: the newest that was reported, zero without one.
    [[nodiscard]] TimeDuration GpuTime() const noexcept
    {
      return m_frameWork.GpuTime();
    }

    //! How long the last present that was reported held the frame loop.
    [[nodiscard]] TimeDuration LastPresentBlocked() const noexcept
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
    [[nodiscard]] bool StartsAgainAt(TickCount64 time) const noexcept;
    [[nodiscard]] int64_t SlotFor(TickCount64 time) const noexcept;
    [[nodiscard]] int64_t SlotAfterPresent() const noexcept;
    [[nodiscard]] TickCount64 TimeOfSlot(int64_t slot) const noexcept;
    void ArmStartupPause() noexcept;
    [[nodiscard]] uint32_t StartupPauseAt(TickCount64 cpuStartTime) noexcept;
  };
}

#endif
