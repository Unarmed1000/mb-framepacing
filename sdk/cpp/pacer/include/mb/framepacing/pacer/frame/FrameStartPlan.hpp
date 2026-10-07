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
  //! wait for before a frame takes anything, in this order: a present, then a time. Either may be absent. The application carries
  //! it out and computes nothing: it waits for what it is given and then starts the frame.
  struct FrameStartPlan
  {
    //! The present to wait for until it was shown, by the frame id it was presented with; 0: none. Only with
    //! PacerCapability::WaitForPresent active.
    uint64_t WaitForPresentFrameId{0};
    //! The longest that wait may take: a present of a window that is not shown may never be shown. Zero without a present to wait
    //! for.
    NanosecondTimeDuration WaitForPresentTimeout;
    //! The time to wait until after that, on the application's steady clock; NanosecondTickCount(): none, the frame starts at once.
    NanosecondTickCount StartTime;

    //! True when there is a present to wait for.
    [[nodiscard]] constexpr bool WaitsForPresent() const noexcept
    {
      return WaitForPresentFrameId != 0;
    }

    //! True when there is a time to wait until.
    [[nodiscard]] constexpr bool WaitsForStartTime() const noexcept
    {
      return StartTime != NanosecondTickCount();
    }
  };
}

#endif
