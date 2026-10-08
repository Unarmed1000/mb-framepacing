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
  //! Three capabilities change how a frame is paced, and a tier is one combination of them:
  //! - a timed present (PresentAtTime or PresentAfterDuration): the display's side shows a frame at the refresh it is for, where
  //!   without one the frame loop has to present at the right moment;
  //! - a wait for a present (WaitForPresent): the loop is held until the display took an earlier frame, where without one it is
  //!   held on a timer (or until the GPU finished an earlier frame, where the application can wait for that);
  //! - vertical blank times (VBlankTimes): the pacer knows where the display's refreshes are, where without them it counts
  //!   refresh periods on the clock.
  //!
  //! The order: who places the frame first, then what holds the loop, then where the refreshes are. Without a timed present the
  //! vertical blank times come before the wait, as they are what makes the loop's placing good. Every order here is a proposal
  //! until the tiers have been measured against each other (the proposal has what is measured).
  //!
  //! Tiers 1 to 4 are paced by TierPacer and built against the simulation's display only: no system has been measured with
  //! them.
  enum class PacerTier : uint8_t
  {
    //! A timed present, vertical blank times and a wait for a present
    TimedVBlankWaitForPresent = 1,
    //! A timed present and a wait for a present, on a timer
    TimedTimerWaitForPresent = 2,
    //! A timed present and vertical blank times
    TimedVBlankPeriodOnly = 3,
    //! A timed present, on a timer and the refresh period only
    TimedTimerPeriodOnly = 4,
    //! Vertical blank times and a wait for a present (VBlankWaitForPresentPacer)
    VBlankWaitForPresent = 5,
    //! Vertical blank times (VBlankPeriodOnlyPacer)
    VBlankPeriodOnly = 6,
    //! A wait for a present, on a timer (TimerWaitForPresentPacer)
    TimerWaitForPresent = 7,
    //! A timer and the refresh period only (TimerPeriodOnlyPacer): the baseline, every set reaches it
    TimerPeriodOnly = 8,
  };
}

#endif
