#ifndef MB_FRAMEPACING_PACER_TIER_SYSTEMHELDLOOPPACER_HPP
#define MB_FRAMEPACING_PACER_TIER_SYSTEMHELDLOOPPACER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/RefreshTime.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/frame/FrameStartPlan.hpp>
#include <mb/framepacing/pacer/frame/GpuWorkReport.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/frame/SystemWaitReport.hpp>
#include <mb/framepacing/pacer/hold/GpuWaitRule.hpp>
#include <mb/framepacing/pacer/hold/PresentWaitRule.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
#include <mb/framepacing/pacer/rule/FrameWorkRule.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalRule.hpp>
#include <mb/framepacing/pacer/tier/PacerHandover.hpp>
#include <mb/framepacing/pacer/tier/SystemHoldState.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md, decision 24). The third major tier, "3": the system holds the loop.
  //! The present or the call for the next image waits while the swap chain's queue is full (PacerCapability::PresentWaits,
  //! AcquireWaits), so the display takes a frame every swap interval and the loop runs into that hold. While it holds the pacer
  //! gives no time to wait for and no wait (no timer), every frame's animation step is its swap interval, and a long frame
  //! that the frames in the queue cover costs nothing. A refresh the display lost because the queue ran empty is not seen
  //! (there are no vertical blank times or display times in this tier): it is one hitch, and not caught up with. A frame is
  //! late by its work alone, which the swap interval rule answers.
  //!
  //! Whether the system holds the loop is checked against the frames' starts, by rate (SystemHoldState): a budget of display
  //! time, refilled by the time between two frames' starts and capped at what the swap chain can queue, from which each frame
  //! takes its swap interval less a hundredth. Where a frame starts with none left, more frames began than the display can show:
  //! the system is not holding (a window that is covered, a hold that lasts a share of a refresh), and the pacer limits the frame
  //! time itself. The limit is a cadence counted from the frame where the hold was found missing, an eighth of a refresh faster
  //! than the display, so that a system that holds again shows it by holding the frames to the display's rate: frames in a row
  //! that start no sooner than the display can show them (less the slack), though the limit would let them start sooner, say
  //! that it holds the loop again. A hold of a share of a refresh can not do that for so many frames in a row. A frame that
  //! starts more than a frame late starts the cadence again (nothing is caught up with). The same limit, at the swap interval
  //! itself, holds a swap interval longer than the present can hold a frame for (SystemHoldState::Limited). The system's own
  //! waits say nothing more than the rate does, and are taken as reports only.
  //!
  //! The aim of smoothness only: low latency would need a timer, and the queue stays full (PacerTierUtil::HasLowLatencyAim). An
  //! application that asks for low latency is paced as with smoothness.
  //!
  //! Values in, values out: no platform API, no clock read, no wait, and nothing is allocated after it is made.
  class SystemHeldLoopPacer
  {
    SwapIntervalRule m_rule;
    PresentWaitRule m_wait;
    GpuWaitRule m_gpuWait;
    FrameWorkRule m_frameWork;
    // The longest swap interval the present holds a frame for: 1 without a swap interval on the present
    uint32_t m_presentHoldsUpTo{1};
    // The last frame that was begun
    bool m_hasFrame{false};
    uint64_t m_frameId{0};
    NanosecondTickCount m_startTime;
    bool m_frameOpen{false};
    bool m_frameEnded{false};
    NanosecondTimeSpan m_work;
    uint32_t m_swapInterval{1};
    // The first frame after another part placed the frames, or after a pause: not judged
    bool m_startsAgain{true};
    // The refreshes the rule's frame window counts its frames on
    int64_t m_slots{0};
    RefreshTime m_animationTime;
    NanosecondTimeSpan m_lastAnimationTime;
    uint64_t m_refreshesBehindClock{0};
    NanosecondTimeDuration m_lastPresentBlocked;
    // The budget of display time, in nanoseconds; whether the system was found not to hold the loop, and the frames in a row
    // since that started no sooner than the display can show them
    int64_t m_budget{0};
    bool m_notHolding{false};
    uint32_t m_heldInARow{0};
    // The time the next frame is limited to, where it is limited (NotHolding, Limited)
    NanosecondTickCount m_limitTime;
    uint64_t m_holdLosses{0};
    uint64_t m_limitedFrames{0};
    // The pause after start-up, as a part that handed over had it (this part makes none: the queue stays full)
    bool m_pausePending{true};
    bool m_pauseHeldByWait{false};
    bool m_pauseHasFirstFrame{false};
    NanosecondTickCount m_pauseFirstFrameTime;
    bool m_presentTaken{false};

  public:
    //! The share of a frame's swap interval it may start early by, against the refresh period it was told, before it counts
    //! against the system's hold: one in this many.
    static constexpr int64_t SlackDivisor = 100;
    //! The frames in a row that start no sooner than the display can show them, while the frames are limited, that say the
    //! system holds the loop again.
    static constexpr uint32_t HeldFramesToHoldAgain = 8;
    //! While the system does not hold the loop the limit runs this share of a refresh period faster than the display: one in this
    //! many. HeldFramesToHoldAgain frames that each start that much later than the limit lets them, less the slack, take more than
    //! a hold of a share of a refresh can give.
    static constexpr int64_t ProbeDivisor = 8;
    //! The images the queue is taken to have where the application does not know its swap chain's (PacerSettings::SwapChainImages).
    static constexpr uint32_t UnknownSwapChainImages = 4;

    //! Allocates the swap interval rule's frame window.
    explicit SystemHeldLoopPacer(const PacerSettings& settings);

    //! The swap intervals the present takes, from the next frame on (PacerCapabilities::MinPresentSwapInterval and
    //! MaxPresentSwapInterval; 0 for both without PresentSwapInterval, which holds a frame for one refresh): the rule never runs
    //! faster than the shortest, and a swap interval above the longest is held by the pacer's limit.
    void SetPresentSwapIntervals(uint32_t minSwapInterval, uint32_t maxSwapInterval) noexcept;

    //! Before a frame takes anything: nothing while the system holds the loop, else the time the frame is limited to.
    [[nodiscard]] FrameStartPlan PlanFrame(NanosecondTickCount now) const noexcept;

    //! The frame starts, at cpuStartTime: the frame before is judged, the system's hold checked, and the rule decides.
    FrameSchedule BeginFrame(NanosecondTickCount cpuStartTime) noexcept;

    //! The frame's CPU work is done, at workDoneTime: nothing to wait for before the present, and the marker's CPU busy time.
    //! The present's swap interval is the caller's (DisplayPlacementUtil::PresentSwapIntervalFor).
    PresentPlan EndFrame(NanosecondTickCount workDoneTime) noexcept;

    //! The frame's CPU busy time so far, at now. Zero: no frame is open.
    [[nodiscard]] NanosecondTimeDuration CpuBusyAt(NanosecondTickCount now) const noexcept;

    //! After the present: a present that waited held the loop as the system's wait does.
    void AddPresent(const PresentReport& report) noexcept;

    //! The GPU's work on an earlier frame: a frame's work is then the CPU's and the GPU's (FrameWorkRule).
    void AddGpuWork(const GpuWorkReport& report) noexcept;

    //! A wait of the application's own before the frame that is about to start: a report only, as the frames' starts say whether
    //! the system holds the loop (in a window the system's hold was measured as a wait for a frame slot).
    void AddSystemWait(const SystemWaitReport& report) noexcept;

    //! The presents made so far are gone (a swap chain was made anew): the queue fills from empty.
    void ForgetPresents() noexcept;

    //! The display's refresh period changed: the frames start again on it, the animation time goes on.
    void SetRefreshPeriod(RefreshPeriod period) noexcept;

    //! Other settings: the frames start again with them. Allocates when the rule's frame window needs more room.
    void SetSettings(const PacerSettings& settings);

    //! Start again (after a pause the application knows of): the frame window is empty, the GPU's work forgotten, and the
    //! system taken to hold the loop. The animation time goes on.
    void Reset() noexcept;

    //! True between a frame's start and its end.
    [[nodiscard]] bool IsFrameOpen() const noexcept
    {
      return m_frameOpen && !m_frameEnded;
    }

    //! Whether the system holds the loop, as the frames' starts say.
    [[nodiscard]] SystemHoldState Hold() const noexcept;

    //! The times the frames were found to start faster than the display can show them, since the part was made.
    [[nodiscard]] uint64_t HoldLosses() const noexcept
    {
      return m_holdLosses;
    }

    //! The frames the pacer limited, since the part was made.
    [[nodiscard]] uint64_t LimitedFrames() const noexcept
    {
      return m_limitedFrames;
    }

    //! The refreshes the animation time is behind the clock, as the part that handed over had them: this part sees no loss.
    [[nodiscard]] uint64_t RefreshesBehindClock() const noexcept
    {
      return m_refreshesBehindClock;
    }

    //! The waits for a present and for the GPU's work that ran out, as the parts that asked for them counted them.
    [[nodiscard]] uint64_t PresentWaitTimeouts() const noexcept
    {
      return m_wait.Timeouts();
    }

    [[nodiscard]] uint64_t GpuWaitTimeouts() const noexcept
    {
      return m_gpuWait.Timeouts();
    }

    //! The GPU time that was reported last, zero without one.
    [[nodiscard]] NanosecondTimeDuration GpuTime() const noexcept
    {
      return m_frameWork.GpuTime();
    }

    //! How long the last present that was reported held the frame loop.
    [[nodiscard]] NanosecondTimeDuration LastPresentBlocked() const noexcept
    {
      return m_lastPresentBlocked;
    }

    [[nodiscard]] const SwapIntervalRule& Rule() const noexcept
    {
      return m_rule;
    }

    [[nodiscard]] FrameWindowState FrameWindow() const noexcept
    {
      return m_rule.FrameWindow();
    }

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

    //! What goes on when another part takes over: the frames, the animation time, the GPU's work and the presents. Its first
    //! frame may start at once, or at the limit where the frames are limited.
    [[nodiscard]] PacerHandover GiveOver() const noexcept;

    //! Go on from another part, with its swap interval rule (the same object where this part goes on with its own).
    void TakeOver(const PacerHandover& handover, const SwapIntervalRule& rule) noexcept;

  private:
    [[nodiscard]] bool IsLimited() const noexcept;
    [[nodiscard]] int64_t Capacity() const noexcept;
    void StartAgain() noexcept;
  };
}

#endif
