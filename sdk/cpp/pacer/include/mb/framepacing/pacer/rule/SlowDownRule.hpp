#ifndef MB_FRAMEPACING_PACER_RULE_SLOWDOWNRULE_HPP
#define MB_FRAMEPACING_PACER_RULE_SLOWDOWNRULE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! When the swap interval rule slows down (sdk/doc/pacer.md). Both speed up the same way.
  enum class SlowDownRule : uint8_t
  {
    //! The default: as FullWindow, and also as soon as the late frames since the last change pass the share of a full window's frames
    //! (SlowDownLatePercent of the frames the window holds at the current rate), without waiting for the window to fill again.
    LateCount = 0,
    //! Swappy's rule: only on a full window (more than WindowTicks of frames) with more than SlowDownLatePercent of them late.
    FullWindow = 1,
  };
}

#endif
