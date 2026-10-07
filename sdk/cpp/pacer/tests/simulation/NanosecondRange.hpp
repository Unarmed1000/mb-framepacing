#ifndef MB_FRAMEPACING_PACER_SIMULATION_NANOSECONDRANGE_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_NANOSECONDRANGE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer::Simulation
{
  //! A length of time drawn anew for every frame from [MinNanoseconds, MaxNanoseconds]; the same value twice is that value every time.
  struct NanosecondRange
  {
    int64_t MinNanoseconds{0};
    int64_t MaxNanoseconds{0};
  };
}

#endif
