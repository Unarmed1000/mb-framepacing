#ifndef MB_FRAMEPACING_PACER_SIMULATION_SPLITMIX64_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_SPLITMIX64_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer::Simulation
{
  //! SplitMix64 (Steele, Lea and Flood, 2014): 64-bit integer arithmetic only, so C# draws the same numbers
  class SplitMix64
  {
    uint64_t m_state;

  public:
    explicit SplitMix64(const uint64_t seed) noexcept
      : m_state(seed)
    {
    }

    uint64_t Next() noexcept
    {
      m_state += 0x9E37'79B9'7F4A'7C15u;
      uint64_t z = m_state;
      z = (z ^ (z >> 30u)) * 0xBF58'476D'1CE4'E5B9u;
      z = (z ^ (z >> 27u)) * 0x94D0'49BB'1331'11EBu;
      return z ^ (z >> 31u);
    }

    //! A whole number from [min, max]
    int64_t Draw(const int64_t min, const int64_t max) noexcept
    {
      const auto range = static_cast<uint64_t>(max - min + 1);
      return min + static_cast<int64_t>(Next() % range);
    }
  };
}

#endif
