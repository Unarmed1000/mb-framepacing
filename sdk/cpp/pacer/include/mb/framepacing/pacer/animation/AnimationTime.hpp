#ifndef MB_FRAMEPACING_PACER_ANIMATION_ANIMATIONTIME_HPP
#define MB_FRAMEPACING_PACER_ANIMATION_ANIMATIONTIME_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer.md). The time a frame animates for (PacerAnimationClock).
  struct AnimationTime
  {
    //! The animation time: the frame's predicted display time on the display's clock. Render the frame for it; it is the marker's
    //! animation time.
    TimeSpan Time;
    //! The step from the previous frame's animation time.
    TimeSpan Step;
    //! The step in whole refreshes.
    uint32_t StepRefreshes{0};
  };
}

#endif
