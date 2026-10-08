#ifndef MB_FRAMEPACING_PACER_FRAME_FRAMESTARTPLAN_HPP
#define MB_FRAMEPACING_PACER_FRAME_FRAMESTARTPLAN_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: part of a redesign that is not built yet). What the application is to
  //! wait for before a frame takes anything, in this order: a present or the GPU's work on a frame (never both), then a time.
  //! Each may be absent. The application carries it out and computes nothing: it waits for what it is given, reports a wait
  //! for a present or for the GPU's work, asks for the plan again after one, and then starts the frame.
  struct FrameStartPlan
  {
    //! The present to wait for until it was shown, by the frame id it was presented with; 0: none. Only with
    //! PacerCapability::WaitForPresent active.
    uint64_t WaitForPresentFrameId{0};
    //! The longest that wait may take: a present of a window that is not shown may never be shown. Zero with a present to wait
    //! for: do not wait, only ask whether the present was shown, and report that (a pacer asks so while its waits run out).
    NanosecondTimeDuration WaitForPresentTimeout;
    //! The frame whose GPU work to wait for until the GPU is done with it (a fence, a frame slot), by its frame id; 0: none.
    //! Only with PacerCapability::WaitForGpuWork active and no wait for a present. It is the application's one wait for a
    //! frame slot: no such wait of its own is made next to it.
    uint64_t WaitForGpuWorkFrameId{0};
    //! The longest that wait may take.
    NanosecondTimeDuration WaitForGpuWorkTimeout;
    //! The time to wait until after that, on the application's steady clock; NanosecondTickCount(): none, the frame starts at once.
    NanosecondTickCount StartTime;

    //! True when there is a present to wait for.
    [[nodiscard]] constexpr bool WaitsForPresent() const noexcept
    {
      return WaitForPresentFrameId != 0;
    }

    //! True when there is a frame whose GPU work to wait for.
    [[nodiscard]] constexpr bool WaitsForGpuWork() const noexcept
    {
      return WaitForGpuWorkFrameId != 0;
    }

    //! True when there is a time to wait until.
    [[nodiscard]] constexpr bool WaitsForStartTime() const noexcept
    {
      return StartTime != NanosecondTickCount();
    }
  };
}

#endif
