#ifndef MB_FRAMEPACING_PACER_RULE_WINDOWSTATE_HPP
#define MB_FRAMEPACING_PACER_RULE_WINDOWSTATE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! What the swap interval rule's window holds: the frames since the last change, of the last WindowTicks. For overlays and logs.
  struct WindowState
  {
    uint32_t Frames{0};
    uint32_t LateFrames{0};
    //! The frames' average work time (FrameEnd::WorkTicks), 0 without frames.
    int64_t AverageWorkTicks{0};
    //! From the oldest frame's display time to the newest's.
    int64_t SpanTicks{0};
    //! The window spans more than WindowTicks: the rule may decide on it.
    bool Full{false};
  };
}

#endif
