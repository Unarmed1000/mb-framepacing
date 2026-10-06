#ifndef MB_FRAMEPACING_PACER_SIMULATION_LOGGEDFRAME_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_LOGGEDFRAME_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer::Simulation
{
  //! A row of a frame log (ReadFrameLog): what a replay gives the pacer, what the log says the pacer answered when the log was
  //! written, and what the display did. A value the row does not have is 0.
  struct LoggedFrame
  {
    int64_t FrameIndex{0};
    bool PacerOn{false};
    int64_t StartTicks{0};
    //! Where the pacer's EndFrame was called, and the work it was given (the CPU's and the GPU's, added)
    int64_t EndFrameTicks{0};
    int64_t WorkTicks{0};
    int64_t PresentTicks{0};
    //! The frame's display time as the log has it (a driver's report, or a simulation's own)
    int64_t ShownTicks{0};
    //! What the log says the pacer answered
    uint32_t SwapInterval{0};
    int64_t AnimationStepTicks{0};
    int64_t IntendedDisplayTicks{0};
    int64_t NextFrameStartTicks{0};
    //! The frame time the log's pacer aimed for, over its swap interval: its refresh period to the tick
    int64_t TargetFrameTimeTicks{0};
  };
}

#endif
