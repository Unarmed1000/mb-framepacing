#ifndef MB_FRAMEPACING_PACER_FRAME_SYSTEMWAITREPORT_HPP
#define MB_FRAMEPACING_PACER_FRAME_SYSTEMWAITREPORT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/pacer/frame/SystemWaitKind.hpp>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built). A wait of the application's own before a
  //! frame started, one the pacer did not ask for: which it was, when it began and when it ended. Given after the wait and before
  //! the frame begins, for every such wait, also one that returned at once. With it a pacer can tell a frame loop that the system
  //! held from one that is late. (How long a present held the loop is in its PresentReport.)
  struct SystemWaitReport
  {
    SystemWaitKind Kind{SystemWaitKind::Acquire};
    //! When the wait began, on the application's steady clock
    NanosecondTickCount BeginTime;
    //! When it ended, on the same clock
    NanosecondTickCount EndTime;

    //! How long the wait held the frame loop: zero for an end before the begin.
    [[nodiscard]] constexpr NanosecondTimeDuration Blocked() const noexcept
    {
      return NanosecondTimeDuration(EndTime - BeginTime);
    }
  };
}

#endif
