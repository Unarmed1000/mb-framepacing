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
  //! frames in flight, the presents that can be waited for and the pause after start-up, made or still to be made. The swap
  //! interval rule goes with it (SwapIntervalRule::TakeOver).
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
    //! When the last frame's present is made (as far as it is known when the handover is made), and when the present of
    //! the frame after it would be: a swap interval later, or where the part that hands over holds its presents to a place
    //! in a refresh, that place. With HasPresentTime only. The part that takes over keeps that cadence: its first present
    //! is made where the next one would have been, and its first frame is for no refresh sooner than a swap interval
    //! after the last frame's.
    bool HasPresentTime{false};
    NanosecondTickCount LastPresentTime;
    NanosecondTickCount NextPresentTime;
    RefreshTime AnimationTime;
    NanosecondTimeSpan LastAnimationTime;
    uint64_t RefreshesBehindClock{0};
    FrameWorkRule FrameWork;
    PresentWaitRule Wait;
    //! The pause after start-up: still to be made, whether a wait for a present held a frame since it was asked for, the
    //! first frame's start since then (when HasPauseFirstFrame), and whether the system took a present since.
    bool PausePending{true};
    bool PauseHeldByWait{false};
    bool HasPauseFirstFrame{false};
    NanosecondTickCount PauseFirstFrameTime;
    bool PresentTaken{false};
  };
}

#endif
