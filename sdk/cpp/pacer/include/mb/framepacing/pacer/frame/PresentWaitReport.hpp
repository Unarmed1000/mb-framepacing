#ifndef MB_FRAMEPACING_PACER_FRAME_PRESENTWAITREPORT_HPP
#define MB_FRAMEPACING_PACER_FRAME_PRESENTWAITREPORT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built). What became of a wait for a present that
  //! a FrameStartPlan asked for: when the wait began, when it ended, and whether it ended because the present was shown or because
  //! the time ran out. Given before the frame begins. A wait that really held the loop ended when the display took a frame, which
  //! is the one thing a pacer without display times learns about where the display's refreshes are.
  struct PresentWaitReport
  {
    //! The frame whose present was waited for: FrameStartPlan::WaitForPresentFrameId.
    uint64_t FrameId{0};
    //! When the wait began, on the application's steady clock
    NanosecondTickCount BeginTime;
    //! When it ended, on the same clock
    NanosecondTickCount EndTime;
    //! true: the present was shown. false: the wait ended without it (the timeout, or the system gave the wait up)
    bool Shown{true};

    //! How long the wait held the frame loop: zero for an end before the begin.
    [[nodiscard]] constexpr NanosecondTimeDuration Blocked() const noexcept
    {
      return NanosecondTimeDuration(EndTime - BeginTime);
    }
  };
}

#endif
