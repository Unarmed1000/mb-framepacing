#ifndef MB_FRAMEPACING_PACER_TIER_PACERHANDOVER_HPP
#define MB_FRAMEPACING_PACER_TIER_PACERHANDOVER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/RefreshTime.hpp>
#include <mb/framepacing/pacer/hold/PresentWaitRule.hpp>
#include <mb/framepacing/pacer/rule/FrameWorkRule.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built). What goes on when the active
  //! capabilities change and another part places the frames: the frames and their ids, the animation time, the GPU's work on the
  //! frames in flight and the presents that can be waited for. The swap interval rule goes with it (SwapIntervalRule::TakeOver).
  //! What does not go on is where the frames were placed: the part that takes over places them from its first frame.
  struct PacerHandover
  {
    //! False: no frame was begun yet, and there is nothing to go on from.
    bool HasFrame{false};
    //! The last frame that was begun: its id and when it started.
    uint64_t FrameId{0};
    NanosecondTickCount StartTime;
    //! When the frame after it is to start, as that frame's schedule said (FrameSchedule::NextFrameStartTime): the first frame
    //! of the part that takes over is held to it.
    NanosecondTickCount NextFrameStartTime;
    RefreshTime AnimationTime;
    NanosecondTimeSpan LastAnimationTime;
    uint64_t RefreshesBehindClock{0};
    FrameWorkRule FrameWork;
    PresentWaitRule Wait;
  };
}

#endif
