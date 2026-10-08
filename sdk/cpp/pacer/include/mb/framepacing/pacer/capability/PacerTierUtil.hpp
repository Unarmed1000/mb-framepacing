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

  //! Whether the display's side places a frame: the present takes a time before which the frame is not shown (PresentAtTime),
  //! so the frame is shown at the refresh it is for whenever the present is made. A time the frame before it stays on screen
  //! (PresentAfterDuration) places nothing by itself, as it counts from wherever that frame was shown: it is in a rating as
  //! DisplaySideHolds.
  [[nodiscard]] constexpr bool HasTimedPresent(const PacerCapabilities& capabilities) noexcept
  {
    return capabilities.Has(PacerCapability::PresentAtTime);
  }

  //! Whether the display's side places a frame and leaves out one that is overdue: a time on the present (PresentAtTime), and
  //! of two presents that are both due the later is shown and the earlier never (PresentSkipsOverdue). Without a time on the
  //! present there is no "due", so the second alone is nothing.
  [[nodiscard]] constexpr bool SkipsOverduePresents(const PacerCapabilities& capabilities) noexcept
  {
    return HasTimedPresent(capabilities) && capabilities.Has(PacerCapability::PresentSkipsOverdue);
  }

  //! The major tier of a tier: who places a frame on its refresh. A value that is no tier is taken as the tier next to it.
  [[nodiscard]] constexpr PacerMajorTier MajorOf(const PacerTier tier) noexcept
  {
    const auto number = static_cast<uint32_t>(tier);
    if (number <= SubTiersPerMajorTier)
    {
      return PacerMajorTier::DisplayPlacesAndSkips;
    }
    return number <= (2u * SubTiersPerMajorTier) ? PacerMajorTier::DisplayPlaces : PacerMajorTier::LoopPlaces;
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

  //! Whether a pacer is built for the tier. None is for the major tier of a display that skips a frame that is overdue: it is
  //! rated, and that is all.
  [[nodiscard]] constexpr bool HasPacer(const PacerTier tier) noexcept
  {
    return MajorOf(tier) != PacerMajorTier::DisplayPlacesAndSkips;
  }

  //! The tier a set of that tier is paced as: the tier itself where a pacer is built for it, and for a tier of a display that
  //! skips the same sub tier of the display that shows every frame. That pacer takes every frame as shown, which a frame the
  //! display skipped is not.
  [[nodiscard]] constexpr PacerTier PacedAs(const PacerTier tier) noexcept
  {
    return HasPacer(tier) ? tier : static_cast<PacerTier>(static_cast<uint32_t>(tier) + SubTiersPerMajorTier);
  }

  //! Whether the set reaches the tier: it has what the tier's major tier needs (a time on the present, and a display's side
  //! that skips), and the wait for a present and the vertical blank times that the tier is named for. TimerPeriodOnly needs
  //! nothing, so every set reaches it.
  [[nodiscard]] constexpr bool Reaches(const PacerCapabilities& capabilities, const PacerTier tier) noexcept
  {
    const bool skips = SkipsOverduePresents(capabilities);
    const bool timed = HasTimedPresent(capabilities);
    const bool wait = capabilities.Has(PacerCapability::WaitForPresent);
    const bool vblank = capabilities.Has(PacerCapability::VBlankTimes);
    switch (tier)
    {
    case PacerTier::TimedSkipVBlankWaitForPresent:
      return skips && vblank && wait;
    case PacerTier::TimedSkipTimerWaitForPresent:
      return skips && wait;
    case PacerTier::TimedSkipVBlankPeriodOnly:
      return skips && vblank;
    case PacerTier::TimedSkipTimerPeriodOnly:
      return skips;
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
    return capabilities.Has(PacerCapability::PresentAtTime) || capabilities.Has(PacerCapability::PresentAfterDuration) ||
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
    auto number = static_cast<uint8_t>(PacerTier::TimedSkipVBlankWaitForPresent);
    while (!Reaches(capabilities, static_cast<PacerTier>(number)))
    {
      ++number;
    }
    rating.Tier = static_cast<PacerTier>(number);
    // What the set does not have and would be raised by: a time on the present; with one, a display's side that skips (which
    // is nothing without the time); a wait for a present; vertical blank times
    rating.RaisesTier = PacerCapability::NoCapabilities;
    if (!HasTimedPresent(capabilities))
    {
      rating.RaisesTier = rating.RaisesTier | PacerCapability::PresentAtTime;
    }
    else if (!capabilities.Has(PacerCapability::PresentSkipsOverdue))
    {
      rating.RaisesTier = rating.RaisesTier | PacerCapability::PresentSkipsOverdue;
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
