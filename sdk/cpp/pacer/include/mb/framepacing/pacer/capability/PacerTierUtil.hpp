#ifndef MB_FRAMEPACING_PACER_CAPABILITY_PACERTIERUTIL_HPP
#define MB_FRAMEPACING_PACER_CAPABILITY_PACERTIERUTIL_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/pacer/capability/PacerCapabilities.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <mb/framepacing/pacer/capability/PacerRating.hpp>
#include <mb/framepacing/pacer/capability/PacerTier.hpp>
#include <cstdint>

//! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built). The tiers as what they are: sets of
//! capabilities. A set reaches a tier when it has what the tier needs, and its rating is the best tier it reaches. Functions of a
//! set alone: they change nothing and need no pacer, so an application can rate any set it considers.
namespace MB::FramePacing::Pacer::PacerTierUtil
{
  //! The shortest "longest swap interval" with which a present's swap interval holds a frame for more than one refresh.
  inline constexpr uint32_t MinHoldingSwapInterval = 2;

  //! Whether the set reaches the tier.
  //! VBlankWaitForPresent: VBlankTimes and WaitForPresent. VBlankPeriodOnly: VBlankTimes. TimerWaitForPresent: WaitForPresent.
  //! TimerPeriodOnly: nothing, so every set does.
  [[nodiscard]] constexpr bool Reaches(const PacerCapabilities& capabilities, const PacerTier tier) noexcept
  {
    switch (tier)
    {
    case PacerTier::VBlankWaitForPresent:
      return capabilities.Has(PacerCapability::VBlankTimes) && capabilities.Has(PacerCapability::WaitForPresent);
    case PacerTier::VBlankPeriodOnly:
      return capabilities.Has(PacerCapability::VBlankTimes);
    case PacerTier::TimerWaitForPresent:
      return capabilities.Has(PacerCapability::WaitForPresent);
    case PacerTier::TimerPeriodOnly:
      break;
    }
    return true;
  }

  //! Whether the display's side can hold a frame for two refreshes or more: PresentAtTime; or PresentAfterDuration; or
  //! PresentSwapInterval with a longest swap interval of 2 or more. A mechanism every pacer uses when it is there, and no tier.
  [[nodiscard]] constexpr bool DisplaySideHolds(const PacerCapabilities& capabilities) noexcept
  {
    return capabilities.Has(PacerCapability::PresentAtTime) || capabilities.Has(PacerCapability::PresentAfterDuration) ||
           (capabilities.Has(PacerCapability::PresentSwapInterval) && capabilities.MaxPresentSwapInterval() >= MinHoldingSwapInterval);
  }

  //! The rating of a set: the best tier it reaches, the capabilities that would raise it, and whether the display's side holds.
  [[nodiscard]] constexpr PacerRating Rate(const PacerCapabilities& capabilities) noexcept
  {
    PacerRating rating;
    rating.DisplaySideHolds = DisplaySideHolds(capabilities);
    if (Reaches(capabilities, PacerTier::VBlankWaitForPresent))
    {
      rating.Tier = PacerTier::VBlankWaitForPresent;
      rating.RaisesTier = PacerCapability::NoCapabilities;
    }
    else if (Reaches(capabilities, PacerTier::VBlankPeriodOnly))
    {
      rating.Tier = PacerTier::VBlankPeriodOnly;
      rating.RaisesTier = PacerCapability::WaitForPresent;
    }
    else if (Reaches(capabilities, PacerTier::TimerWaitForPresent))
    {
      rating.Tier = PacerTier::TimerWaitForPresent;
      rating.RaisesTier = PacerCapability::VBlankTimes;
    }
    return rating;
  }
}

#endif
