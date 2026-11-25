#ifndef MB_FRAMEPACING_PACER_CAPABILITY_PACERMAJORTIER_HPP
#define MB_FRAMEPACING_PACER_CAPABILITY_PACERMAJORTIER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built). The major tiers: who places a frame
  //! on its refresh, by what the present takes, 1 the best. Most have four sub tiers (PacerTier), and a tier is written as the
  //! two: "4.1"; the system's hold has none, and is written "3" (PacerTierUtil::SubTierCountOf).
  enum class PacerMajorTier : uint8_t
  {
    //! The display's side, from a time on the present before which the frame is not shown (PresentAtTime): it places the frame
    //! against a clock, whenever the present is made.
    DisplayPlacesAtTime = 1,
    //! The display's side, from a time the frame before it stays on screen at least (PresentAfterDuration, or a swap interval of
    //! two or more, the same in whole refreshes): it places the frame relative to the one before, so the loop may present early.
    //! After a late frame every later duration counts from it: the display stays smooth and the pacer has to see that.
    DisplayPlacesAfterDuration = 2,
    //! The display's side, by holding the loop: the present or the call for the next image waits while the swap chain's queue
    //! is full (PresentWaits, AcquireWaits), so the display takes a frame every swap interval and the loop runs into the hold.
    //! No sub tiers: a full queue leaves a wait for a present and vertical blank times nothing to do.
    SystemHoldsLoop = 3,
    //! The frame loop: it has to make the present at the right moment.
    LoopPlaces = 4,
  };
}

#endif
