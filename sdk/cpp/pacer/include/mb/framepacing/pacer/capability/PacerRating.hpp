#ifndef MB_FRAMEPACING_PACER_CAPABILITY_PACERRATING_HPP
#define MB_FRAMEPACING_PACER_CAPABILITY_PACERRATING_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <mb/framepacing/pacer/capability/PacerTier.hpp>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built). What a set of capabilities is worth
  //! (PacerTierUtil::Rate): the best tier it reaches, which names its pacer, what would raise it, and whether the display's side
  //! can hold a frame of two refreshes or more. As made, it is the rating of a set without capabilities.
  struct PacerRating
  {
    PacerTier Tier{PacerTier::TimerPeriodOnly};
    //! The capabilities any one of which, added to the set, raises its tier; none at the best tier.
    PacerCapability RaisesTier{PacerCapability::VBlankTimes | PacerCapability::WaitForPresent};
    //! The present can hold a frame for two refreshes or more (a time, a minimum duration, or a swap interval of 2 or more): a
    //! mechanism every pacer uses when it is there, and no tier. Without it the frame loop holds such a frame.
    bool DisplaySideHolds{false};

    constexpr bool operator==(const PacerRating& other) const noexcept = default;
  };
}

#endif
