#ifndef MB_FRAMEPACING_PACER_CAPABILITY_PACERRATING_HPP
#define MB_FRAMEPACING_PACER_CAPABILITY_PACERRATING_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <mb/framepacing/pacer/capability/PacerTier.hpp>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built). What a set of capabilities is worth
  //! (PacerTierUtil::Rate): the best tier it reaches, what would raise it, and four facts beside the tier, three of them the marks
  //! written after its number. As made, it is the rating of a set without capabilities.
  struct PacerRating
  {
    PacerTier Tier{PacerTier::TimerPeriodOnly};
    //! The capabilities any one of which, added to the set, raises its tier; none at the best tier.
    PacerCapability RaisesTier{PacerCapability::PresentAtTime | PacerCapability::WaitForPresent | PacerCapability::FrameCallback |
                               PacerCapability::VBlankTimes};
    //! The present can hold a frame for two refreshes or more (a time, a minimum duration, or a swap interval of 2 or more).
    //! True at every tier where the display's side places the frame; where the frame loop does (a duration without a wait for a
    //! present or display times), it says that a present's minimum duration or swap interval holds such a frame, where without
    //! it the loop does.
    bool DisplaySideHolds{false};
    //! The application reports when its frames were shown (DisplayTimes), so the animation error can be worked out where the
    //! application runs: the "+" after a tier's number (PacerTierText::DisplayTimesMark). It is what reaches the second major
    //! tier's sub tiers without a wait for a present, and changes no other tier.
    bool ReportsDisplayTimes{false};
    //! The display's side shows the newer of two frames that are both due and never the older (PresentSkipsOverdue): the "s"
    //! after a tier's number (PacerTierText::SkipsOverdueMark). It describes the system (a display may skip whether asked or
    //! not) and is a rating only, on every tier: no pacer relies on it yet, so it changes no tier.
    bool SkipsOverdue{false};
    //! What holds the loop at this tier is a frame callback, not a wait for a present (PacerTierUtil::HoldsByFrameCallback): the
    //! "c" after a tier's number (PacerTierText::FrameCallbackMark). The tier is the one a wait for a present gives; the mark says
    //! that the display's side took a frame where that wait would say it was shown.
    bool HoldsByFrameCallback{false};

    constexpr bool operator==(const PacerRating& other) const noexcept = default;
  };
}

#endif
