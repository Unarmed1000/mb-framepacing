#ifndef MB_FRAMEPACING_PACER_FRAMEPACER_HPP
#define MB_FRAMEPACING_PACER_FRAMEPACER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/clock/PacerRefreshClock.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/frame/FramesInFlight.hpp>
#include <mb/framepacing/pacer/frame/PresentFeedback.hpp>
#include <mb/framepacing/pacer/frame/PresentFeedbackState.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalRule.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL: checked against its own simulation and one machine's driver logs; no capture of it on a real swap chain has been
  //! analysed with the tools (sdk/doc/pacer.md).
  //!
  //! Paces a frame loop with nothing but a steady clock and a Present that waits for vsync: the baseline that works on any platform.
  //! Every frame: BeginFrame with the time the frame starts, hold the frame for the schedule's swap interval and render it for the
  //! schedule's animation time, EndFrame when presenting. The pacer measures on the CPU's clock when frames start, counts in whole
  //! refreshes on the display's (PacerRefreshClock), and adapts the swap interval to how the frames do (SwapIntervalRule), from the frame
  //! rate the application prefers down.
  //!
  //! Optional, where the platform has present feedback and the display a fixed refresh rate (PacerSettings::UsePresentFeedback): the
  //! application gives each frame's measured display time back (AddPresentFeedback), a few frames after the frame. The pacer paces
  //! exactly as without it; the display times give statistics (FeedbackState: what the display did, to hold against the pacer's own
  //! count of late frames) and the intended display time as a refresh of the display (FramesInFlight).
  //!
  //! Values in, values out: the pacer calls no platform API and never reads a clock. Made once (it allocates the rule's window); pacing
  //! frames never allocates, and only SetSettings with settings that need a larger window does.
  class FramePacer
  {
    SwapIntervalRule m_rule;
    PacerRefreshClock m_clock;
    FramesInFlight m_inFlight;
    // The frame between BeginFrame and the next BeginFrame
    TickCount64 m_cpuStartTime;
    TimeSpan m_work;
    bool m_frameOpen{false};
    bool m_frameEnded{false};

  public:
    explicit FramePacer(const PacerSettings& settings);

    //! Start a frame, at cpuStartTime on the application's steady clock: the previous frame is measured (how many refreshes after the
    //! frame before it it was shown, so whether it was late), the rule decides, and this frame is planned.
    FrameSchedule BeginFrame(TickCount64 cpuStartTime) noexcept;

    //! The frame's work is done and it is about to be presented, at presentTime on the same clock: call it as the marker is drawn,
    //! before the present and before any wait for the frame's time (a wait inside it would count as work). work: how long the frame
    //! needed, as the rule should count it; zero takes the CPU busy time. A frame whose work is longer than its swap interval's time
    //! is late, whenever the next frame starts (a swap chain with a buffer to spare does not hold the loop to the display). An
    //! application the GPU limits must put the GPU's time in (the last frame's that was measured will do): without it the pacer does
    //! not see that, and the rule speeds up again after every frame window without a late frame. Returns the CPU busy time
    //! (presentTime - the frame's start) for the marker, zero (unknown) when it does not fit it.
    TimeSpan32 EndFrame(TickCount64 presentTime, TimeSpan work = {}) noexcept;

    //! What the platform measured for an earlier frame (its FrameSchedule::FrameId): any number of calls between two BeginFrames,
    //! oldest frame first. Nothing while PacerSettings::UsePresentFeedback is off. It changes no swap interval and no animation
    //! time: it is counted (FeedbackState), and the next BeginFrame's intended display time is counted from it. Feedback that can
    //! not be a refresh of the display is refused. Never allocates.
    void AddPresentFeedback(const PresentFeedback& feedback) noexcept;

    //! The display's refresh period changed (a mode change, another monitor): the pacer starts again on it, with an empty window, at the
    //! swap interval the application prefers there. The animation time goes on. The period the pacer has already changes nothing.
    void SetRefreshPeriod(RefreshPeriod period) noexcept;

    //! Other settings on a live pacer (another target frame rate, the rule switched off, another display): the pacer starts again with
    //! them, with an empty window, at the swap interval the application prefers. The animation time goes on, and a frame that is open
    //! is ended as usual. The same settings change nothing, so this may be called every frame with the application's current ones.
    //! Allocates when the frame window needs more room than it has (a longer window, a faster display or frame rate), and only then.
    void SetSettings(const PacerSettings& settings);

    //! Start again (after a pause the application knows of): the next frame is planned as the first, the window is empty, the swap
    //! interval the preferred one. The animation time goes on.
    void Reset() noexcept;

    [[nodiscard]] FrameWindowState FrameWindow() const noexcept
    {
      return m_rule.FrameWindow();
    }

    //! The statistics of the present feedback given so far: what the display did, and what became of the feedback.
    [[nodiscard]] PresentFeedbackState FeedbackState() const noexcept
    {
      return m_inFlight.State();
    }

    //! The swap interval the next frame is paced at.
    [[nodiscard]] uint32_t SwapInterval() const noexcept
    {
      return m_rule.SwapInterval();
    }

    //! The refresh period the pacer paces at now: its settings'.
    [[nodiscard]] RefreshPeriod Refresh() const noexcept
    {
      return m_rule.Refresh();
    }

    //! The settings the pacer paces with: the ones it was made with, or was given since, with the refresh period it is on.
    [[nodiscard]] const PacerSettings& Settings() const noexcept
    {
      return m_rule.Settings();
    }
  };
}

#endif
