#ifndef MB_FRAMEPACING_PACER_CAPABILITY_PACERTIER_HPP
#define MB_FRAMEPACING_PACER_CAPABILITY_PACERTIER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built). The tiers of pacers: one list, 1 the
  //! best. A tier is the capabilities a set needs to reach it (PacerTierUtil::Reaches): what the application can give the pacer
  //! decides how its frames are paced. Every tier has both aims (PacerAim), so a tier says nothing of latency.
  //!
  //! A tier is a way of placing a frame on its refresh, and a present uses one: what can be combined with every way is a
  //! capability beside the tier, not a tier (decision 19 of the design record). The list is three major tiers (PacerMajorTier: who
  //! places a frame, by what the present takes) of four sub tiers each, and a tier is written as the two: "3.1"
  //! (PacerTierUtil::MajorOf and SubTierOf, PacerTierText::NumberOf). A sub tier is a rank inside its major tier, from two
  //! capabilities:
  //! - a wait for a present (WaitForPresent): the loop is held until the display took an earlier frame, where without one it is
  //!   held on a timer (or until the GPU finished an earlier frame, where the application can wait for that);
  //! - vertical blank times (VBlankTimes): the pacer knows where the display's refreshes are, where without them it counts
  //!   refresh periods on the clock.
  //! Where the display's side places the frame the wait comes first; where the frame loop places it the vertical blank times do,
  //! as they are what makes the loop's placing good. Every order here is a proposal until the tiers have been measured against
  //! each other, the second major tier against 3.1 first (the record has what is measured).
  //!
  //! The second major tier (AfterDuration...) needs to know where a frame was shown, as every later duration counts from it: its
  //! sub tiers without a wait for a present are reached only with display times (DisplayTimes, the "+"). It is rated and has no
  //! pacer of its own yet: a set that reaches it is paced as the frame loop's tier of the same capabilities, with the duration
  //! given on every present (PacerTierUtil::PacedAs). The first (AtTime...) is paced by TierPacer and built against the
  //! simulation's display only: no system has been measured with it.
  enum class PacerTier : uint8_t
  {
    //! 1.1: a time on the present, vertical blank times and a wait for a present
    AtTimeVBlankWaitForPresent = 1,
    //! 1.2: a time on the present and a wait for a present, on a timer
    AtTimeTimerWaitForPresent = 2,
    //! 1.3: a time on the present and vertical blank times
    AtTimeVBlankPeriodOnly = 3,
    //! 1.4: a time on the present, on a timer and the refresh period only
    AtTimeTimerPeriodOnly = 4,
    //! 2.1: a duration on the present, vertical blank times and a wait for a present
    AfterDurationVBlankWaitForPresent = 5,
    //! 2.2: a duration on the present and a wait for a present, on a timer
    AfterDurationTimerWaitForPresent = 6,
    //! 2.3: a duration on the present, vertical blank times and display times
    AfterDurationVBlankDisplayTimes = 7,
    //! 2.4: a duration on the present and display times, on a timer
    AfterDurationTimerDisplayTimes = 8,
    //! 3.1: vertical blank times and a wait for a present
    VBlankWaitForPresent = 9,
    //! 3.2: vertical blank times
    VBlankPeriodOnly = 10,
    //! 3.3: a wait for a present, on a timer
    TimerWaitForPresent = 11,
    //! 3.4: a timer and the refresh period only: the baseline, every set reaches it
    TimerPeriodOnly = 12,
  };
}

#endif
