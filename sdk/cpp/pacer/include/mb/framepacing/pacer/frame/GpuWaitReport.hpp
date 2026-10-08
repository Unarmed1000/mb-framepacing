#ifndef MB_FRAMEPACING_PACER_FRAME_GPUWAITREPORT_HPP
#define MB_FRAMEPACING_PACER_FRAME_GPUWAITREPORT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built). What became of a wait for the GPU's work
  //! on a frame that a FrameStartPlan asked for (a fence, a frame slot): when the wait began, when it ended, and whether it ended
  //! because the GPU was done with that frame or because the time ran out. Given before the frame begins.
  struct GpuWaitReport
  {
    //! The frame whose GPU work was waited for: FrameStartPlan::WaitForGpuWorkFrameId.
    uint64_t FrameId{0};
    //! When the wait began, on the application's steady clock
    NanosecondTickCount BeginTime;
    //! When it ended, on the same clock
    NanosecondTickCount EndTime;
    //! true: the GPU was done with the frame. false: the wait ended without it (the timeout)
    bool Done{true};

    //! How long the wait held the frame loop: zero for an end before the begin.
    [[nodiscard]] constexpr NanosecondTimeDuration Blocked() const noexcept
    {
      return NanosecondTimeDuration(EndTime - BeginTime);
    }
  };
}

#endif
