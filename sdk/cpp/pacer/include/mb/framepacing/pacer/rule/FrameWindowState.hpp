#ifndef MB_FRAMEPACING_PACER_RULE_FRAMEWINDOWSTATE_HPP
#define MB_FRAMEPACING_PACER_RULE_FRAMEWINDOWSTATE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer.md). What the swap interval rule's frame window holds: the frames it decides on,
  //! those since its last change of the last FrameWindowLength. For overlays and logs. (A window of frames in time: nothing to do
  //! with a window on screen.)
  struct FrameWindowState
  {
    uint32_t Frames{0};
    uint32_t LateFrames{0};
    //! The frames' average work time, zero without frames.
    TimeSpan AverageWork;
    //! From the oldest frame's display time to the newest's.
    TimeSpan Span;
    //! The window spans more than FrameWindowLength, or holds all the frames it can: the rule may decide on it.
    bool Full{false};
    //! The frames whose next frame began before the time the pacer gave for it (FrameSchedule::NextFrameStartTime): a check of the
    //! application's frame loop, which the rule does not use. None in a loop that waits for that time. About half of the frames in
    //! a loop that only a present waiting for the display holds, as its frame starts jitter around the refreshes. Nearly all of
    //! them: nothing holds the loop to the display and it runs ahead of it, or the refresh period given is longer than the display's.
    uint32_t EarlyStarts{0};
  };
}

#endif
