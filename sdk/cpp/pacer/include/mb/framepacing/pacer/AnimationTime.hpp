#ifndef MB_FRAMEPACING_PACER_ANIMATIONTIME_HPP
#define MB_FRAMEPACING_PACER_ANIMATIONTIME_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! The time a frame animates for (AnimationClock).
  struct AnimationTime
  {
    //! The animation time in ticks: the marker's animation time.
    int64_t AnimationTicks{0};
    //! The step from the previous frame's animation time.
    int64_t StepTicks{0};
    //! The step in whole refreshes.
    int64_t StepRefreshes{0};
  };
}

#endif
