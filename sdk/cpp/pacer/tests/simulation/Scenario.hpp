#ifndef MB_FRAMEPACING_PACER_SIMULATION_SCENARIO_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_SCENARIO_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>
#include <string>
#include <vector>
#include "LoadStage.hpp"
#include "ScenarioFrame.hpp"

namespace MB::FramePacing::Pacer::Simulation
{
  //! A load to pace: given frame by frame (Frames, played Passes times), or as stages of time (Stages, Calm outside them) until
  //! DurationNanoseconds, each frame's work drawn by the stage its start falls in with SplitMix64 from Seed.
  struct Scenario
  {
    std::string Name;
    //! The refresh rate in Hz, RateNumerator / RateDenominator
    uint32_t RateNumerator{60};
    uint32_t RateDenominator{1};
    std::vector<ScenarioFrame> Frames;
    //! false: paced at a fixed swap interval of 1, the rule off (both rules give the same frames: one result, <name>-Fixed.csv)
    bool AutoSwapInterval{true};
    int32_t Passes{1};
    std::vector<LoadStage> Stages;
    LoadStage Calm;
    int64_t DurationNanoseconds{0};
    uint64_t Seed{0};
  };
}

#endif
