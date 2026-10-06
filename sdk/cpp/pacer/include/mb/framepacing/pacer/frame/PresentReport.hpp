#ifndef MB_FRAMEPACING_PACER_FRAME_PRESENTREPORT_HPP
#define MB_FRAMEPACING_PACER_FRAME_PRESENTREPORT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeDuration.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: part of a redesign that is not built yet). When a frame's present was
  //! called and when it returned, given right after it. From it the pacer learns whether the present waits on this system, whatever
  //! the capabilities say, and it keeps that wait out of the frame's work.
  struct PresentReport
  {
    //! The frame: its PresentPlan::FrameId.
    uint64_t FrameId{0};
    //! When the present was called, on the application's steady clock
    TickCount64 CallTime;
    //! When it returned, on the same clock
    TickCount64 ReturnTime;

    //! How long the call held the frame loop: zero for a return before the call.
    [[nodiscard]] constexpr TimeDuration Blocked() const noexcept
    {
      return TimeDuration(ReturnTime - CallTime);
    }
  };
}

#endif
