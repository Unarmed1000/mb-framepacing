#ifndef MB_FRAMEPACING_PACER_CAPABILITY_QUEUETIER_HPP
#define MB_FRAMEPACING_PACER_CAPABILITY_QUEUETIER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: part of a redesign that is not built yet). How the frames that were
  //! presented and wait to be shown are kept few: a tier is the capabilities a set needs to reach it (PacerTierUtil::Reaches), 1
  //! the best.
  enum class QueueTier : uint8_t
  {
    //! A wait until a present was shown: the frame loop is held until the display took an earlier frame
    WaitForPresent = 1,
    //! Display times: the presents not yet shown are counted, frames later, and a frame start is taken back for each one too many
    DisplayTimes = 2,
    //! The refresh period only: never more frames than the display takes; a frame that waits is not seen. The baseline, every set
    //! reaches it
    PeriodOnly = 3,
  };
}

#endif
