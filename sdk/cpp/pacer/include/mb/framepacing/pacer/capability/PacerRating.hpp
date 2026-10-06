#ifndef MB_FRAMEPACING_PACER_CAPABILITY_PACERRATING_HPP
#define MB_FRAMEPACING_PACER_CAPABILITY_PACERRATING_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/pacer/capability/HoldTier.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <mb/framepacing/pacer/capability/QueueTier.hpp>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: part of a redesign that is not built yet). What a set of capabilities
  //! is worth (PacerTierUtil::Rate): the best tier it reaches for each of the two questions, and what would raise each. The two
  //! tiers are not one number, as neither orders the other.
  struct PacerRating
  {
    HoldTier Hold{HoldTier::Timer};
    QueueTier Queue{QueueTier::PeriodOnly};
    //! The capabilities any one of which, added to the set, raises its hold tier; none at the best tier. PresentSwapInterval is
    //! one of them only with a longest swap interval of 2 or more.
    PacerCapability RaisesHold{PacerCapability::NoCapabilities};
    //! The capabilities any one of which, added to the set, raises its queue tier; none at the best tier.
    PacerCapability RaisesQueue{PacerCapability::NoCapabilities};

    constexpr bool operator==(const PacerRating& other) const noexcept = default;
  };
}

#endif
