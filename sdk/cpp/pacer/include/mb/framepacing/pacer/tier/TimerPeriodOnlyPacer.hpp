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
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
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
  //!   AddPresent  after the present, before the next frame is planned: when it was called and returned
  //!
  //! It can not see the display. What it has is a count: the clock says how many refresh periods have passed, and it knows how
  //! many it gave its frames. So it keeps the frame starts on one grid of refresh periods on the clock. Step 0 is the first frame's
  //! start, and a frame is due at a whole number of steps from it, whatever the frames before it did:
  //!   - a frame that starts less than half a period late keeps its step and starts at once: that frame is late, no frame after
  //!     it is;
  //!   - a frame that would start later than that takes the step nearest to where the loop is, and waits for it when it is still
  //!     to come: the frame before it ran long, the steps in between are lost, and the loop is back where it was against the
  //!     display, whatever that place is;
  //!   - a frame held for more than one refresh is presented by the loop on a timer, in the period before the step the next frame
  //!     is due at, a margin into it (PacerSettings::FrameMargin). A guess: the grid's place against the display's refreshes is
  //!     not known.
  //! The swap interval rule (SwapIntervalRule) decides each frame's swap interval from how the frames did: a frame is late when
  //! it took more steps of the grid than its swap interval, or when its work was longer than its swap interval's time.
  //!
  //! The animation time advances by a frame's swap interval and by nothing else. It is not moved to catch up with the clock after
  //! refreshes were lost: that would be a second step that does not match the display's, and RefreshesBehindClock() says how many
  //! refreshes that is.
  //!
  //! What it does not do: take a frame away that waits to be shown although its frame was ready in time. It does not learn of
  //! one. At two refreshes per frame or more such a frame is gone by itself; at one it stays.
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
    // The frame between BeginFrame and the next BeginFrame
    uint64_t m_frameId{0};
    TickCount64 m_startTime;
    uint32_t m_swapInterval{1};
    TimeSpan m_work;
    bool m_frameOpen{false};
    bool m_frameEnded{false};
    RefreshTime m_animationTime;
    TimeSpan m_lastAnimationTime;
    uint64_t m_refreshesBehindClock{0};
    TimeDuration m_lastPresentBlocked;

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

    //! After the present, before the next frame is planned.
    void AddPresent(const PresentReport& report) noexcept;

    //! The display's refresh period changed (a mode change, the window on another display): the grid starts again on it with an
    //! empty frame window, at the swap interval the application prefers there. The animation time goes on.
    void SetRefreshPeriod(RefreshPeriod period) noexcept;

    //! Other settings on a live pacer: it starts again with them, as with another refresh period. The same settings change
    //! nothing. Allocates when the frame window needs more room than it has, and only then.
    void SetSettings(const PacerSettings& settings);

    //! Start again (after a pause the application knows of): the next frame starts the grid, the frame window is empty, the swap
    //! interval the preferred one. The animation time goes on.
    void Reset() noexcept;

    //! The refreshes that were lost and that the animation time was not moved over: how far it is behind the clock, in refreshes,
    //! since the pacer was made.
    [[nodiscard]] uint64_t RefreshesBehindClock() const noexcept
    {
      return m_refreshesBehindClock;
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
    [[nodiscard]] TickCount64 TimeOfSlot(int64_t slot) const noexcept;
  };
}

#endif
