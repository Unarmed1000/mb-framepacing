#ifndef MB_FRAMEPACING_PACER_CAPABILITY_HOLDTIER_HPP
#define MB_FRAMEPACING_PACER_CAPABILITY_HOLDTIER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: part of a redesign that is not built yet). How a frame is held for
  //! its swap interval: a tier is the capabilities a set needs to reach it (PacerTierUtil::Reaches), 1 the best. The numbers are
  //! the first integration's.
  enum class HoldTier : uint8_t
  {
    //! The display side holds the frame: a present with a time or a minimum duration, or a swap interval of 2 or more
    DisplaySide = 1,
    //! The frame loop holds it and knows where the refreshes are: vertical blank times
    VBlank = 2,
    //! The frame loop holds it on a timer, which is a guess: the baseline, every set reaches it
    Timer = 3,
  };
}

#endif
