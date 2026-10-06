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
    int64_t WaitBeginTicks{0};
    int64_t WaitTargetTicks{0};
    int64_t StartTicks{0};
    //! The CPU's work done: the submit, and where the pacer's EndFrame is called
    int64_t WorkEndTicks{0};
    int64_t GpuBeginTicks{0};
    int64_t GpuEndTicks{0};
    //! The wait before the present: where it began and the time it waited for
    int64_t PresentWaitBeginTicks{0};
    int64_t PresentWaitTargetTicks{0};
    int64_t PresentTicks{0};
    int64_t ShownTicks{0};
    //! The frames presented and not yet shown when this frame started
    int32_t PendingAtStart{0};
    //! The work the pacer was given: the CPU's, and the GPU's of the frame before
    int64_t WorkCpuTicks{0};
    int64_t WorkGpuTicks{0};
    uint32_t SwapInterval{1};
    int64_t AnimationTicks{0};
    int64_t AnimationStepTicks{0};
    int64_t IntendedDisplayTicks{0};
    int64_t NextFrameStartTicks{0};
    int64_t TargetFrameTimeTicks{0};
    uint32_t WindowFrames{0};
    uint32_t WindowLateFrames{0};
  };
}

#endif
