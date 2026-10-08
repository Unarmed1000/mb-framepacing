#ifndef MB_FRAMEPACING_PACER_SIMULATION_LOOPACTIVESETCHANGE_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_LOOPACTIVESETCHANGE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer::Simulation
{
  //! A change of what a tier pacer's loop has active, made before a frame of a simulated run (LoopSettings::ActiveSetChanges):
  //! from that frame on the loop does what the new set asks of it (it gives vertical blank readings or not, it waits for a
  //! present when the plan asks or is not asked), and tells the pacer so.
  struct LoopActiveSetChange
  {
    //! The frame (by its number from 0) before which the change is made
    int32_t Frame{0};
    //! Which of vertical blank times and the wait for a present are active from then on. The timed presents and the display
    //! reports of the settings stay as they are
    PacerCapability Named{PacerCapability::NoCapabilities};
  };
}

#endif
