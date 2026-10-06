#ifndef MB_FRAMEPACING_PACER_SIMULATION_TICKRANGE_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_TICKRANGE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer::Simulation
{
  //! A length of time drawn anew for every frame from [MinTicks, MaxTicks]; the same value twice is that value every time.
  struct TickRange
  {
    int64_t MinTicks{0};
    int64_t MaxTicks{0};
  };
}

#endif
