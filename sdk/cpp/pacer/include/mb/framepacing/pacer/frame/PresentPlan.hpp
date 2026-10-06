#ifndef MB_FRAMEPACING_PACER_FRAME_PRESENTPLAN_HPP
#define MB_FRAMEPACING_PACER_FRAME_PRESENTPLAN_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeDuration.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: part of a redesign that is not built yet). How the application is to
  //! present a frame whose CPU work is done: a time to wait until first (absent when the present goes at once), and the values to
  //! give the present, of which the one the active capabilities have is set. The application carries it out and computes nothing.
  struct PresentPlan
  {
    //! The frame: the id to present it with where the present takes one, and to give back with its reports.
    uint64_t FrameId{0};
    //! The time to wait until before the present, on the application's steady clock; TickCount64(): none, present at once.
    TickCount64 PresentTime;
    //! The swap interval for a present that takes one (PacerCapability::PresentSwapInterval active); 1 otherwise.
    uint32_t SwapInterval{1};
    //! The time before which the frame is not to be shown, for a present that takes one (PacerCapability::PresentAtTime
    //! active); TickCount64(): none.
    TickCount64 NotBeforeTime;
    //! The time the frame before this one is to stay on screen at least, for a present that takes one
    //! (PacerCapability::PresentAfterDuration active); zero: none.
    TimeDuration MinimumDuration;
    //! The marker's CPU busy time: from the frame's start to the end of its CPU work, waits the plans asked for left out. Zero:
    //! not known, or it does not fit the marker's field.
    TimeSpan32 CpuBusy;

    //! True when there is a time to wait until before the present.
    [[nodiscard]] constexpr bool WaitsForPresentTime() const noexcept
    {
      return PresentTime != TickCount64();
    }
  };
}

#endif
