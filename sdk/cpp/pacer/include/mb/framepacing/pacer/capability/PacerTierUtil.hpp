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

  //! Whether a frame callback would hold the loop in this set, were it there. Not with a wait for a present, which says more.
  //! Not with vertical blank times: there a wait's return is read as the refresh a frame was shown at, and a window system's call
  //! says that the display's side took a frame, which may be before it is shown. And not where display times reach the second
  //! major tier: the display's side places the frames there, and the loop follows its slip.
  [[nodiscard]] constexpr bool FrameCallbackCanHold(const PacerCapabilities& capabilities) noexcept
  {
    const bool placedAfterFrameBefore =
      HasPresentAfterDuration(capabilities) && !HasPresentAtTime(capabilities) && capabilities.Has(PacerCapability::DisplayTimes);
    return !capabilities.Has(PacerCapability::WaitForPresent) && !capabilities.Has(PacerCapability::VBlankTimes) && !placedAfterFrameBefore;
  }

  //! Whether a frame callback holds the loop: the set has one, and it can (FrameCallbackCanHold). The plan's wait for a present
  //! is then that present's callback.
  [[nodiscard]] constexpr bool HoldsByFrameCallback(const PacerCapabilities& capabilities) noexcept
  {
    return capabilities.Has(PacerCapability::FrameCallback) && FrameCallbackCanHold(capabilities);
  }

  //! Whether the display's side holds the loop before a frame: a wait for a present, or a frame callback where one holds it.
  [[nodiscard]] constexpr bool HasDisplayHold(const PacerCapabilities& capabilities) noexcept
  {
    return capabilities.Has(PacerCapability::WaitForPresent) || HoldsByFrameCallback(capabilities);
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

  //! Whether a pacer is built for the tier: for every tier (TierPacer), since the second major tier has its own (decision 20 of
  //! sdk/doc/pacer-design.md). Kept for applications that asked before.
  [[nodiscard]] constexpr bool HasPacer(const PacerTier /*tier*/) noexcept
  {
    return true;
  }

  //! The tier a set of that tier is paced as: the tier itself, as every tier has a pacer (HasPacer). Until decision 20 a tier of
  //! the second major tier was paced as the frame loop's tier of the same capabilities. Kept for applications that asked before.
  [[nodiscard]] constexpr PacerTier PacedAs(const PacerTier tier) noexcept
  {
    return tier;
  }

  //! Whether the set reaches the tier: it has what the tier's major tier needs (a time on the present; a duration on the present),
  //! and the wait for a present, the vertical blank times and the display times that the tier is named for. On a grid on the clock
  //! a frame callback is that wait, but for the second major tier, which has to see where frames were shown. TimerPeriodOnly needs
  //! nothing, so every set reaches it.
  [[nodiscard]] constexpr bool Reaches(const PacerCapabilities& capabilities, const PacerTier tier) noexcept
  {
    const bool atTime = HasPresentAtTime(capabilities);
    const bool afterDuration = HasPresentAfterDuration(capabilities);
    const bool wait = capabilities.Has(PacerCapability::WaitForPresent);
    const bool hold = HasDisplayHold(capabilities);
    const bool vblank = capabilities.Has(PacerCapability::VBlankTimes);
    const bool displayTimes = capabilities.Has(PacerCapability::DisplayTimes);
    switch (tier)
    {
    case PacerTier::AtTimeVBlankWaitForPresent:
      return atTime && vblank && wait;
    case PacerTier::AtTimeTimerWaitForPresent:
      return atTime && hold;
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
      return hold;
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
  //! times are reported, whether the display's side holds, whether it skips a frame that is overdue and whether a frame callback
  //! holds the loop.
  [[nodiscard]] constexpr PacerRating Rate(const PacerCapabilities& capabilities) noexcept
  {
    PacerRating rating;
    rating.ReportsDisplayTimes = capabilities.Has(PacerCapability::DisplayTimes);
    rating.DisplaySideHolds = DisplaySideHolds(capabilities);
    rating.SkipsOverdue = capabilities.Has(PacerCapability::PresentSkipsOverdue);
    rating.HoldsByFrameCallback = HoldsByFrameCallback(capabilities);
    // The best tier: the first one of the list that the set reaches (every set reaches the last)
    auto number = static_cast<uint8_t>(PacerTier::AtTimeVBlankWaitForPresent);
    while (!Reaches(capabilities, static_cast<PacerTier>(number)))
    {
      ++number;
    }
    rating.Tier = static_cast<PacerTier>(number);
    // What the set does not have and would be raised by: a time on the present; without one, a duration on the present (or a
    // swap interval of two or more) where the set sees where frames were shown, and display times where it has a duration and no
    // wait for a present; a wait for a present, which a frame callback that holds the loop is at its tier unless a duration needs
    // the real one; a frame callback where one would hold the loop and the set has none; vertical blank times
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
    if (!wait && (!rating.HoldsByFrameCallback || (afterDuration && !atTime)))
    {
      rating.RaisesTier = rating.RaisesTier | PacerCapability::WaitForPresent;
    }
    if (!capabilities.Has(PacerCapability::FrameCallback) && FrameCallbackCanHold(capabilities))
    {
      rating.RaisesTier = rating.RaisesTier | PacerCapability::FrameCallback;
    }
    if (!capabilities.Has(PacerCapability::VBlankTimes))
    {
      rating.RaisesTier = rating.RaisesTier | PacerCapability::VBlankTimes;
    }
    return rating;
  }
}

#endif
