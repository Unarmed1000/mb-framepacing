#ifndef MB_FRAMEPACING_PACER_CAPABILITY_PACERRATING_HPP
#define MB_FRAMEPACING_PACER_CAPABILITY_PACERRATING_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <mb/framepacing/pacer/capability/PacerTier.hpp>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built). What a set of capabilities is worth
  //! (PacerTierUtil::Rate): the best tier it reaches, what would raise it, and two facts beside the tier. As made, it is the
  //! rating of a set without capabilities.
  struct PacerRating
  {
    PacerTier Tier{PacerTier::TimerPeriodOnly};
    //! The capabilities any one of which, added to the set, raises its tier; none at the best tier.
    PacerCapability RaisesTier{PacerCapability::PresentAtTime | PacerCapability::WaitForPresent | PacerCapability::VBlankTimes};
    //! The present can hold a frame for two refreshes or more (a time, a minimum duration, or a swap interval of 2 or more).
    //! True at every tier where the display's side places the frame; where the frame loop does, it says that a present's
    //! minimum duration or swap interval holds such a frame, where without it the loop does.
    bool DisplaySideHolds{false};
    //! The application reports when its frames were shown (DisplayTimes), so the animation error can be worked out where the
    //! application runs: the "+" beside a tier's number. It changes no tier.
    bool ReportsDisplayTimes{false};

    constexpr bool operator==(const PacerRating& other) const noexcept = default;
  };
}

#endif
