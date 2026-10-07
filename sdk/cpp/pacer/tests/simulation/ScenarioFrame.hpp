#ifndef MB_FRAMEPACING_PACER_SIMULATION_SCENARIOFRAME_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_SCENARIOFRAME_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer::Simulation
{
  //! One frame of a scenario given frame by frame: how long it works, and what a reference simulation paced it at (for a cross-check).
  struct ScenarioFrame
  {
    int64_t WorkNanoseconds{0};
    //! The reference's swap interval for this frame, 0 = none
    int32_t ReferenceSwapInterval{0};
    //! The refresh the reference showed this frame on (any origin), -1 = none
    int64_t ReferenceShownRefresh{-1};
  };
}

#endif
