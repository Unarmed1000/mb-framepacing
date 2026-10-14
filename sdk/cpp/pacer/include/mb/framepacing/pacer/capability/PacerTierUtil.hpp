#ifndef MB_FRAMEPACING_PACER_CAPABILITY_PACERTIERUTIL_HPP
#define MB_FRAMEPACING_PACER_CAPABILITY_PACERTIERUTIL_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/pacer/capability/PacerCapabilities.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <mb/framepacing/pacer/capability/PacerMajorTier.hpp>
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
  //! The sub tiers a major tier has.
  inline constexpr uint32_t SubTiersPerMajorTier = 4;

  //! Whether the display's side places a frame against a clock: the present takes a time before which the frame is not shown
  //! (PresentAtTime), so the frame is shown at the refresh it is for whenever the present is made.
  [[nodiscard]] constexpr bool HasPresentAtTime(const PacerCapabilities& capabilities) noexcept
  {
    return capabilities.Has(PacerCapability::PresentAtTime);
  }

  //! Whether the display's side can place a frame relative to the one before it: the present takes a time the frame before it
  //! stays on screen at least (PresentAfterDuration), or a swap interval of two or more (PresentSwapInterval with a longest swap
  //! interval of MinHoldingSwapInterval or more), the same in whole refreshes. A swap interval of one places nothing beyond what
  //! a display that shows one frame per refresh does.
  [[nodiscard]] constexpr bool HasPresentAfterDuration(const PacerCapabilities& capabilities) noexcept
  {
    return capabilities.Has(PacerCapability::PresentAfterDuration) ||
           (capabilities.Has(PacerCapability::PresentSwapInterval) && capabilities.MaxPresentSwapInterval() >= MinHoldingSwapInterval);
  }

  //! Whether the pacer learns where a frame was shown: a wait for a present (WaitForPresent) or display times (DisplayTimes).
  //! Placed relative to the frame before, a late frame moves every frame after it a refresh later; without either the pacer
  //! does not see that, and the latency creeps up a refresh at a time.
  [[nodiscard]] constexpr bool SeesWhereFramesWereShown(const PacerCapabilities& capabilities) noexcept
  {
    return capabilities.Has(PacerCapability::WaitForPresent) || capabilities.Has(PacerCapability::DisplayTimes);
  }

  //! The major tier of a tier: who places a frame on its refresh. A value that is no tier is taken as the tier next to it.
  [[nodiscard]] constexpr PacerMajorTier MajorOf(const PacerTier tier) noexcept
  {
    const auto number = static_cast<uint32_t>(tier);
    if (number <= SubTiersPerMajorTier)
    {
      return PacerMajorTier::DisplayPlacesAtTime;
    }
    return number <= (2u * SubTiersPerMajorTier) ? PacerMajorTier::DisplayPlacesAfterDuration : PacerMajorTier::LoopPlaces;
  }

  //! The sub tier of a tier, 1 to SubTiersPerMajorTier: its rank inside its major tier. 3 and 2 are tier "3.2". A value that is
  //! no tier is taken as the tier next to it.
  [[nodiscard]] constexpr uint32_t SubTierOf(const PacerTier tier) noexcept
  {
    const auto number = static_cast<uint32_t>(tier);
    if (number == 0u)
    {
      return 1u;
    }
    return number > (3u * SubTiersPerMajorTier) ? SubTiersPerMajorTier : (((number - 1u) % SubTiersPerMajorTier) + 1u);
  }

  //! Whether a pacer is built for the tier. None is for the major tier of a duration on the present yet: it is rated, and paced
  //! as the frame loop's tier of the same capabilities (PacedAs).
  [[nodiscard]] constexpr bool HasPacer(const PacerTier tier) noexcept
  {
    return MajorOf(tier) != PacerMajorTier::DisplayPlacesAfterDuration;
  }

  //! The tier a set of that tier is paced as: the tier itself where a pacer is built for it, and for a tier of a duration on the
  //! present the frame loop's tier of the same capabilities, with the duration given on every present: 2.1 as 3.1 (vertical
  //! blank times and a wait), 2.2 as 3.3 (a wait), 2.3 as 3.2 (vertical blank times), 2.4 as 3.4.
  [[nodiscard]] constexpr PacerTier PacedAs(const PacerTier tier) noexcept
  {
    switch (tier)
    {
    case PacerTier::AfterDurationVBlankWaitForPresent:
      return PacerTier::VBlankWaitForPresent;
    case PacerTier::AfterDurationTimerWaitForPresent:
      return PacerTier::TimerWaitForPresent;
    case PacerTier::AfterDurationVBlankDisplayTimes:
      return PacerTier::VBlankPeriodOnly;
    case PacerTier::AfterDurationTimerDisplayTimes:
      return PacerTier::TimerPeriodOnly;
    default:
      return tier;
    }
  }

  //! Whether the set reaches the tier: it has what the tier's major tier needs (a time on the present; a duration on the present),
  //! and the wait for a present, the vertical blank times and the display times that the tier is named for. TimerPeriodOnly needs
  //! nothing, so every set reaches it.
  [[nodiscard]] constexpr bool Reaches(const PacerCapabilities& capabilities, const PacerTier tier) noexcept
  {
    const bool atTime = HasPresentAtTime(capabilities);
    const bool afterDuration = HasPresentAfterDuration(capabilities);
    const bool wait = capabilities.Has(PacerCapability::WaitForPresent);
    const bool vblank = capabilities.Has(PacerCapability::VBlankTimes);
    const bool displayTimes = capabilities.Has(PacerCapability::DisplayTimes);
    switch (tier)
    {
    case PacerTier::AtTimeVBlankWaitForPresent:
      return atTime && vblank && wait;
    case PacerTier::AtTimeTimerWaitForPresent:
      return atTime && wait;
    case PacerTier::AtTimeVBlankPeriodOnly:
      return atTime && vblank;
    case PacerTier::AtTimeTimerPeriodOnly:
      return atTime;
    case PacerTier::AfterDurationVBlankWaitForPresent:
      return afterDuration && vblank && wait;
    case PacerTier::AfterDurationTimerWaitForPresent:
      return afterDuration && wait;
    case PacerTier::AfterDurationVBlankDisplayTimes:
      return afterDuration && vblank && displayTimes;
    case PacerTier::AfterDurationTimerDisplayTimes:
      return afterDuration && displayTimes;
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
    return HasPresentAtTime(capabilities) || HasPresentAfterDuration(capabilities);
  }

  //! The rating of a set: the best tier it reaches, the capabilities that would raise it, and the facts beside it: whether display
  //! times are reported, whether the display's side holds and whether it skips a frame that is overdue.
  [[nodiscard]] constexpr PacerRating Rate(const PacerCapabilities& capabilities) noexcept
  {
    PacerRating rating;
    rating.ReportsDisplayTimes = capabilities.Has(PacerCapability::DisplayTimes);
    rating.DisplaySideHolds = DisplaySideHolds(capabilities);
    rating.SkipsOverdue = capabilities.Has(PacerCapability::PresentSkipsOverdue);
    // The best tier: the first one of the list that the set reaches (every set reaches the last)
    auto number = static_cast<uint8_t>(PacerTier::AtTimeVBlankWaitForPresent);
    while (!Reaches(capabilities, static_cast<PacerTier>(number)))
    {
      ++number;
    }
    rating.Tier = static_cast<PacerTier>(number);
    // What the set does not have and would be raised by: a time on the present; without one, a duration on the present (or a
    // swap interval of two or more) where the set sees where frames were shown, and display times where it has a duration and no
    // wait for a present; a wait for a present; vertical blank times
    const bool atTime = HasPresentAtTime(capabilities);
    const bool afterDuration = HasPresentAfterDuration(capabilities);
    const bool wait = capabilities.Has(PacerCapability::WaitForPresent);
    rating.RaisesTier = PacerCapability::NoCapabilities;
    if (!atTime)
    {
      rating.RaisesTier = rating.RaisesTier | PacerCapability::PresentAtTime;
      if (!afterDuration && SeesWhereFramesWereShown(capabilities))
      {
        rating.RaisesTier = rating.RaisesTier | PacerCapability::PresentAfterDuration;
        // A swap interval on the present does the same, of two or more, where the present takes none yet
        if (!capabilities.Has(PacerCapability::PresentSwapInterval))
        {
          rating.RaisesTier = rating.RaisesTier | PacerCapability::PresentSwapInterval;
        }
      }
      if (afterDuration && !wait && !rating.ReportsDisplayTimes)
      {
        rating.RaisesTier = rating.RaisesTier | PacerCapability::DisplayTimes;
      }
    }
    if (!wait)
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
