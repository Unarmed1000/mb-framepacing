#ifndef MB_FRAMEPACING_PACER_SIMULATION_LOOPWORKCHANGE_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_LOOPWORKCHANGE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>
#include "NanosecondRange.hpp"

namespace MB::FramePacing::Pacer::Simulation
{
  //! A change of the work a tier pacer's loop does, in a simulated run (LoopSettings::WorkChanges): a frame that starts that
  //! long after the run's first frame or later has this work, until the next change. A load that comes and goes.
  struct LoopWorkChange
  {
    //! How long after the start of the run's first frame the change is in force
    int64_t AfterNanoseconds{0};
    //! A frame's work on the CPU and on the GPU from then on (LoopSettings::CpuWork and GpuWork until then)
    NanosecondRange CpuWork;
    NanosecondRange GpuWork;
  };
}

#endif
