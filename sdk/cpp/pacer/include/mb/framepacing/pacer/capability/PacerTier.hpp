#ifndef MB_FRAMEPACING_PACER_CAPABILITY_PACERTIER_HPP
#define MB_FRAMEPACING_PACER_CAPABILITY_PACERTIER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built). The tiers of pacers: one list, 1 the
  //! best, and one pacer for each, named as its class. A tier is the capabilities a set needs to reach it
  //! (PacerTierUtil::Reaches): what the application can give the pacer decides which pacer it gets. Every pacer has both aims
  //! (PacerAim), so a tier says nothing of latency.
  //!
  //! The order of the two in the middle is open until the pacer of vertical blank times has been measured: for now knowing where
  //! the display's refreshes are ranks above a wait for a present.
  enum class PacerTier : uint8_t
  {
    //! Vertical blank times and a wait for a present (VBlankWaitForPresentPacer)
    VBlankWaitForPresent = 1,
    //! Vertical blank times (VBlankPeriodOnlyPacer)
    VBlankPeriodOnly = 2,
    //! A wait for a present, on a timer (TimerWaitForPresentPacer)
    TimerWaitForPresent = 3,
    //! A timer and the refresh period only (TimerPeriodOnlyPacer): the baseline, every set reaches it
    TimerPeriodOnly = 4,
  };
}

#endif
