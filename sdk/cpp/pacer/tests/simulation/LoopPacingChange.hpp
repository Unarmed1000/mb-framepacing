#ifndef MB_FRAMEPACING_PACER_SIMULATION_LOOPPACINGCHANGE_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_LOOPPACINGCHANGE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer::Simulation
{
  //! The application stops pacing or starts again, before a frame of a simulated run (LoopSettings::PacingChanges): from that
  //! frame on the pacer is told so (TierPacer::SetPacing). The loop goes on carrying out what the plans give, which while it
  //! is not pacing is nothing: it then runs as fast as its work and the display's side let it.
  struct LoopPacingChange
  {
    //! The frame (by its number from 0) before which the change is made
    int32_t Frame{0};
    bool IsPacing{true};
  };
}

#endif
