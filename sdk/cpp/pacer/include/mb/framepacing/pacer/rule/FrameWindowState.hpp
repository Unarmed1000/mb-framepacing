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
  };
}

#endif
