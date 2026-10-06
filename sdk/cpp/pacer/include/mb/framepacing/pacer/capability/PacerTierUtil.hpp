#ifndef MB_FRAMEPACING_PACER_CAPABILITY_PACERTIERUTIL_HPP
#define MB_FRAMEPACING_PACER_CAPABILITY_PACERTIERUTIL_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/pacer/capability/HoldTier.hpp>
#include <mb/framepacing/pacer/capability/PacerCapabilities.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <mb/framepacing/pacer/capability/PacerRating.hpp>
#include <mb/framepacing/pacer/capability/QueueTier.hpp>

//! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: part of a redesign that is not built yet). The tiers as what they are:
//! sets of capabilities. A set reaches a tier when it holds all of one of the tier's sets, and its rating is the best tier it
//! reaches. Functions of a set alone: they change nothing and need no pacer, so an application can rate any set it considers.
namespace MB::FramePacing::Pacer::PacerTierUtil
{
  //! The shortest "longest swap interval" with which a present's swap interval holds a frame for more than one refresh.
  inline constexpr uint32_t MinHoldingSwapInterval = 2;

  //! Whether the set reaches the hold tier.
  //! DisplaySide: PresentAtTime; or PresentAfterDuration; or PresentSwapInterval with a longest swap interval of 2 or more.
  //! VBlank: VBlankTimes. Timer: nothing, so every set does.
  [[nodiscard]] constexpr bool Reaches(const PacerCapabilities& capabilities, const HoldTier tier) noexcept
  {
    switch (tier)
    {
    case HoldTier::DisplaySide:
      return capabilities.Has(PacerCapability::PresentAtTime) || capabilities.Has(PacerCapability::PresentAfterDuration) ||
             (capabilities.Has(PacerCapability::PresentSwapInterval) && capabilities.MaxPresentSwapInterval() >= MinHoldingSwapInterval);
    case HoldTier::VBlank:
      return capabilities.Has(PacerCapability::VBlankTimes);
    case HoldTier::Timer:
      break;
    }
    return true;
  }

  //! Whether the set reaches the queue tier.
  //! WaitForPresent: WaitForPresent. DisplayTimes: DisplayTimes. PeriodOnly: nothing, so every set does.
  [[nodiscard]] constexpr bool Reaches(const PacerCapabilities& capabilities, const QueueTier tier) noexcept
  {
    switch (tier)
    {
    case QueueTier::WaitForPresent:
      return capabilities.Has(PacerCapability::WaitForPresent);
    case QueueTier::DisplayTimes:
      return capabilities.Has(PacerCapability::DisplayTimes);
    case QueueTier::PeriodOnly:
      break;
    }
    return true;
  }

  //! The rating of a set: the best tier it reaches for each question, and the capabilities that would raise each.
  [[nodiscard]] constexpr PacerRating Rate(const PacerCapabilities& capabilities) noexcept
  {
    constexpr PacerCapability DisplaySideSets =
      PacerCapability::PresentAtTime | PacerCapability::PresentAfterDuration | PacerCapability::PresentSwapInterval;
    PacerRating rating;
    if (Reaches(capabilities, HoldTier::DisplaySide))
    {
      rating.Hold = HoldTier::DisplaySide;
    }
    else if (Reaches(capabilities, HoldTier::VBlank))
    {
      rating.Hold = HoldTier::VBlank;
      rating.RaisesHold = DisplaySideSets;
    }
    else
    {
      rating.RaisesHold = DisplaySideSets | PacerCapability::VBlankTimes;
    }

    if (Reaches(capabilities, QueueTier::WaitForPresent))
    {
      rating.Queue = QueueTier::WaitForPresent;
    }
    else if (Reaches(capabilities, QueueTier::DisplayTimes))
    {
      rating.Queue = QueueTier::DisplayTimes;
      rating.RaisesQueue = PacerCapability::WaitForPresent;
    }
    else
    {
      rating.RaisesQueue = PacerCapability::WaitForPresent | PacerCapability::DisplayTimes;
    }
    return rating;
  }
}

#endif
