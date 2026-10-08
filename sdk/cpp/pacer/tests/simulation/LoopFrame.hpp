#ifndef MB_FRAMEPACING_PACER_SIMULATION_LOOPFRAME_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_LOOPFRAME_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer::Simulation
{
  //! A frame of a simulated frame loop: the moments of its stages on the loop's steady clock, what the pacer said, and what the
  //! display did with it. A moment of 0 is one the frame did not have.
  struct LoopFrame
  {
    uint64_t FrameId{0};
    //! The wait for the frame's start: where it began and the time it waited for
    int64_t WaitBeginNanoseconds{0};
    int64_t WaitTargetNanoseconds{0};
    int64_t StartNanoseconds{0};
    //! The CPU's work done: the submit, and where the pacer's EndFrame is called
    int64_t WorkEndNanoseconds{0};
    int64_t GpuBeginNanoseconds{0};
    int64_t GpuEndNanoseconds{0};
    //! The wait before the present: where it began and the time it waited for
    int64_t PresentWaitBeginNanoseconds{0};
    int64_t PresentWaitTargetNanoseconds{0};
    int64_t PresentNanoseconds{0};
    int64_t ShownNanoseconds{0};
    //! The frames presented and not yet shown when this frame started
    int32_t PendingAtStart{0};
    //! The work the pacer was given: the CPU's, and the GPU's of the frame before
    int64_t WorkCpuNanoseconds{0};
    int64_t WorkGpuNanoseconds{0};
    uint32_t SwapInterval{1};
    int64_t AnimationNanoseconds{0};
    int64_t AnimationStepNanoseconds{0};
    int64_t IntendedDisplayNanoseconds{0};
    int64_t NextFrameStartNanoseconds{0};
    int64_t TargetFrameTimeNanoseconds{0};
    uint32_t WindowFrames{0};
    uint32_t WindowLateFrames{0};
    //! For a tier pacer's loop: the frame whose GPU work a plan had the loop wait for before this frame (0: none), and how
    //! long that wait held the loop
    uint64_t GpuWaitFrameId{0};
    int64_t GpuWaitBlockedNanoseconds{0};
    //! For a tier pacer's loop: what was active when the frame was made (PacerCapability's bits), and how far the pacer's
    //! animation time was behind the clock by then, in refreshes
    uint32_t ActiveCapabilities{0};
    uint64_t RefreshesBehindClock{0};
    //! For a tier pacer's loop that reports display times: what the pacer counted from them when this frame started
    //! (DisplayErrorState)
    uint64_t DisplayJudgedFrames{0};
    uint64_t DisplayErrorFrames{0};
    uint64_t DisplayOffTargetFrames{0};
    uint64_t DisplayLateFrames{0};
    uint64_t DisplayStartToDisplayFrames{0};
    int64_t DisplayStartToDisplayTotalNanoseconds{0};
    int64_t DisplayStartToDisplayLongestNanoseconds{0};
  };
}

#endif
