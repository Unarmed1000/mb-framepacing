#ifndef MB_FRAMEPACING_PACER_SIMULATION_LOADSTAGE_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_LOADSTAGE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer::Simulation
{
  //! A stretch of a scenario's time in which every frame's work is drawn from [MinWorkTicks, MaxWorkTicks].
  struct LoadStage
  {
    //! From and to (excluded) the simulation's start
    int64_t FromTicks{0};
    int64_t ToTicks{0};
    int64_t MinWorkTicks{0};
    int64_t MaxWorkTicks{0};
  };
}

#endif
