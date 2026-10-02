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
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalRule.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL: checked against its own simulation only, never against a real swap chain (sdk/doc/pacer.md).
  //!
  //! Paces a frame loop with nothing but a steady clock and a Present that waits for vsync: the baseline that works on any platform.
  //! Every frame: BeginFrame with the time the frame starts, hold the frame for the schedule's swap interval and render it for the
  //! schedule's animation time, EndFrame when presenting. The pacer measures on the CPU's clock when frames start, counts in whole
  //! refreshes on the display's (PacerRefreshClock), and adapts the swap interval to how the frames do (SwapIntervalRule), from the frame
  //! rate the application prefers down.
  //!
  //! Values in, values out: the pacer calls no platform API and never reads a clock. Made once (it allocates the rule's window); nothing
  //! after that allocates.
  class FramePacer
  {
    SwapIntervalRule m_rule;
    PacerRefreshClock m_clock;
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
    //! needed, as the rule should count it; zero takes the CPU busy time. An application the GPU limits must put the GPU's time in
    //! (the last frame's that was measured will do): without it the rule speeds up again after every frame window without a late
    //! frame. Returns the CPU busy time (presentTime - the frame's start) for the marker, zero (unknown) when it does not fit it.
    TimeSpan32 EndFrame(TickCount64 presentTime, TimeSpan work = {}) noexcept;

    //! The display's refresh period changed (a mode change, another monitor): the pacer starts again on it, with an empty window, at the
    //! swap interval the application prefers there. The animation time goes on. The period the pacer has already changes nothing.
    void SetRefreshPeriod(RefreshPeriod period) noexcept;

    //! Start again (after a pause the application knows of): the next frame is planned as the first, the window is empty, the swap
    //! interval the preferred one. The animation time goes on.
    void Reset() noexcept;

    [[nodiscard]] FrameWindowState FrameWindow() const noexcept
    {
      return m_rule.FrameWindow();
    }

    //! The swap interval the next frame is paced at.
    [[nodiscard]] uint32_t SwapInterval() const noexcept
    {
      return m_rule.SwapInterval();
    }

    //! The refresh period the pacer paces at now (PacerSettings::Refresh until the period changes).
    [[nodiscard]] RefreshPeriod Refresh() const noexcept
    {
      return m_rule.Refresh();
    }

    //! The settings the pacer was made with.
    [[nodiscard]] const PacerSettings& Settings() const noexcept
    {
      return m_rule.Settings();
    }
  };
}

#endif
