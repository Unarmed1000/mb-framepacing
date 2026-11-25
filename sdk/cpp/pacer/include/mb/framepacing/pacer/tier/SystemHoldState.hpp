#ifndef MB_FRAMEPACING_PACER_TIER_SYSTEMHOLDSTATE_HPP
#define MB_FRAMEPACING_PACER_TIER_SYSTEMHOLDSTATE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md, decision 24). Where the system holds the loop (the third major tier,
  //! SystemHeldLoopPacer): whether it does, as the frames' starts say. The tier stays the same in every state: what changes is
  //! whether the pacer has to limit the frame time itself.
  enum class SystemHoldState : uint8_t
  {
    //! The system holds the loop to the display: the pacer gives no time to wait for
    Holding = 0,
    //! The frames started faster than the display can show them: the system does not hold the loop (a window that is covered,
    //! a hold that lasts a share of a refresh), and the pacer limits the frame time to the swap interval until it holds again
    NotHolding = 1,
    //! The system holds the loop, but the swap interval is longer than the present can hold a frame for (a present without a
    //! swap interval, or above its longest): the pacer holds the rest
    Limited = 2,
  };
}

#endif
