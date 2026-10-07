#ifndef MB_FRAMEPACING_PACER_TIER_TIMERWAITFORPRESENTPACER_HPP
#define MB_FRAMEPACING_PACER_TIER_TIMERWAITFORPRESENTPACER_HPP
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
#include <mb/framepacing/pacer/frame/PresentWaitReport.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalRule.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built one tier's pacer at a time; FramePacer is
  //! the pacer the library has until they replace it). The pacer of HoldTier::Timer with QueueTier::WaitForPresent: a whole pacer by
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
  //!
  //! The frames that wait to be shown are kept few by the display itself: before a frame the application waits until the present
  //! PacerSettings::WaitingPresents back was shown. A wait for a present that was shown already returns at once, so it costs nothing
  //! while the loop is in step with the display, and it holds the loop for exactly what is too many when the display lost a
  //! refresh, at start-up, and after a swap chain was made anew. There is no other rule for those. A present the system did not
  //! take is not waited for, nor is any present before it.
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
  //! The animation time advances by a frame's swap interval and by nothing else, as TimerPeriodOnlyPacer's.
  //!
  //! Values in, values out: no platform API, no clock read, no wait. Made once (it allocates the rule's frame window); pacing
  //! frames never allocates.
  class TimerWaitForPresentPacer
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
    // The presents: the last one the system took, and the first that can still be waited for (0: none yet)
    uint64_t m_lastAcceptedId{0};
    uint64_t m_oldestWaitableId{1};
    uint64_t m_presentWaitTimeouts{0};
    // The present the wait before the next frame was made for: it is not asked for again
    uint64_t m_waitedForId{0};

  public:
    //! The tiers this pacer is for.
    static constexpr HoldTier Hold = HoldTier::Timer;
    static constexpr QueueTier Queue = QueueTier::WaitForPresent;

    explicit TimerWaitForPresentPacer(const PacerSettings& settings);

    //! Before a frame takes anything, at now on the application's steady clock: the present to wait for (the one
    //! PacerSettings::WaitingPresents back, where the system took it and it can still be waited for), and after it the time
    //! of the step the frame is due at, when that is still to come. It changes nothing, so a frame may be planned again, and
    //! after AddPresentWait it is planned again: the present that was waited for is not asked for a second time, and the
    //! time is the one that holds then.
    [[nodiscard]] FrameStartPlan PlanFrame(TickCount64 now) const noexcept;

    //! What became of the wait for a present the plan asked for. A wait that held the loop for a share of a refresh period and
    //! ended with the present shown moves the grid towards its end. One that ended without it is counted
    //! (PresentWaitTimeouts).
    void AddPresentWait(const PresentWaitReport& report) noexcept;

    //! The frame starts, at cpuStartTime: the previous frame is judged, the rule decides, and this frame is planned.
    FrameSchedule BeginFrame(TickCount64 cpuStartTime) noexcept;

    //! The frame's CPU work is done, at workDoneTime: how to present it.
    PresentPlan EndFrame(TickCount64 workDoneTime) noexcept;

    //! The frame's CPU busy time so far, at now, for a marker that is drawn while the frame's work is still going on: from the
    //! frame's start to now. Zero: no frame is open, or it does not fit the marker's field.
    [[nodiscard]] TimeSpan32 CpuBusyAt(TickCount64 now) const noexcept;

    //! After the present, before the next frame is planned. A present the system did not take is not waited for, nor is any
    //! present before it (a swap chain that is made anew starts with nothing to wait for). A frame that is presented again
    //! after that, on the new swap chain, is reported again, and can be waited for.
    void AddPresent(const PresentReport& report) noexcept;

    //! The display's refresh period changed (a mode change, the window on another display): the grid starts again on it with an
    //! empty frame window, at the swap interval the application prefers there. The animation time goes on.
    void SetRefreshPeriod(RefreshPeriod period) noexcept;

    //! Other settings on a live pacer: it starts again with them, as with another refresh period. The same settings change
    //! nothing. Allocates when the frame window needs more room than it has, and only then.
    void SetSettings(const PacerSettings& settings);

    //! The presents made so far can no longer be waited for (a swap chain was made anew, for a window that is resized, say).
    //! Nothing else changes: the grid, the frame window and the swap interval go on.
    void ForgetPresents() noexcept;

    //! Start again (after a pause the application knows of): the next frame starts the grid, the frame window is empty, the
    //! swap interval the preferred one, and no present from before is waited for. The animation time goes on.
    void Reset() noexcept;

    //! The refreshes that were lost and that the animation time was not moved over: how far it is behind the clock, in refreshes,
    //! since the pacer was made.
    [[nodiscard]] uint64_t RefreshesBehindClock() const noexcept
    {
      return m_refreshesBehindClock;
    }

    //! The waits for a present that ended without the present being shown, since the pacer was made.
    [[nodiscard]] uint64_t PresentWaitTimeouts() const noexcept
    {
      return m_presentWaitTimeouts;
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
