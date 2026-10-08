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

  //! Whether the present takes a time: one before which the frame is not shown (PresentAtTime), or one the frame before it stays
  //! on screen at least (PresentAfterDuration). The display's side then shows a frame at the refresh it is for.
  [[nodiscard]] constexpr bool HasTimedPresent(const PacerCapabilities& capabilities) noexcept
  {
    return capabilities.Has(PacerCapability::PresentAtTime) || capabilities.Has(PacerCapability::PresentAfterDuration);
  }

  //! Whether the set reaches the tier: it has the timed present, the wait for a present and the vertical blank times that the
  //! tier is named for. TimerPeriodOnly needs nothing, so every set reaches it.
  [[nodiscard]] constexpr bool Reaches(const PacerCapabilities& capabilities, const PacerTier tier) noexcept
  {
    const bool timed = HasTimedPresent(capabilities);
    const bool wait = capabilities.Has(PacerCapability::WaitForPresent);
    const bool vblank = capabilities.Has(PacerCapability::VBlankTimes);
    switch (tier)
    {
    case PacerTier::TimedVBlankWaitForPresent:
      return timed && vblank && wait;
    case PacerTier::TimedTimerWaitForPresent:
      return timed && wait;
    case PacerTier::TimedVBlankPeriodOnly:
      return timed && vblank;
    case PacerTier::TimedTimerPeriodOnly:
      return timed;
    case PacerTier::VBlankWaitForPresent:
      return vblank && wait;
    case PacerTier::VBlankPeriodOnly:
      return vblank;
    case PacerTier::TimerWaitForPresent:
      return wait;
    case PacerTier::TimerPeriodOnly:
      break;
    }
    return true;
  }

  //! Whether the display's side can hold a frame for two refreshes or more: PresentAtTime; or PresentAfterDuration; or
  //! PresentSwapInterval with a longest swap interval of 2 or more.
  [[nodiscard]] constexpr bool DisplaySideHolds(const PacerCapabilities& capabilities) noexcept
  {
    return HasTimedPresent(capabilities) ||
           (capabilities.Has(PacerCapability::PresentSwapInterval) && capabilities.MaxPresentSwapInterval() >= MinHoldingSwapInterval);
  }

  //! The rating of a set: the best tier it reaches, the capabilities that would raise it, whether display times are reported
  //! and whether the display's side holds.
  [[nodiscard]] constexpr PacerRating Rate(const PacerCapabilities& capabilities) noexcept
  {
    PacerRating rating;
    rating.ReportsDisplayTimes = capabilities.Has(PacerCapability::DisplayTimes);
    rating.DisplaySideHolds = DisplaySideHolds(capabilities);
    // The best tier: the first one of the list that the set reaches (every set reaches the last)
    auto number = static_cast<uint8_t>(PacerTier::TimedVBlankWaitForPresent);
    while (!Reaches(capabilities, static_cast<PacerTier>(number)))
    {
      ++number;
    }
    rating.Tier = static_cast<PacerTier>(number);
    // Each of the three that the set does not have raises it: a timed present by either of its two capabilities
    rating.RaisesTier = PacerCapability::NoCapabilities;
    if (!HasTimedPresent(capabilities))
    {
      rating.RaisesTier = rating.RaisesTier | PacerCapability::PresentAtTime | PacerCapability::PresentAfterDuration;
    }
    if (!capabilities.Has(PacerCapability::WaitForPresent))
    {
      rating.RaisesTier = rating.RaisesTier | PacerCapability::WaitForPresent;
    }
    if (!capabilities.Has(PacerCapability::VBlankTimes))
    {
      rating.RaisesTier = rating.RaisesTier | PacerCapability::VBlankTimes;
    }
    return rating;
  }
}

#endif
