#ifndef MB_FRAMEPACING_PACER_SIMULATION_LOOPSETTINGS_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_LOOPSETTINGS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>
#include <vector>
#include "DisplayModelSettings.hpp"
#include "LoopProfile.hpp"
#include "TickRange.hpp"

namespace MB::FramePacing::Pacer::Simulation
{
  //! A frame loop to simulate (SimulateLoop): the application's side, and the display it presents to.
  struct LoopSettings
  {
    //! The refresh rate in Hz, RateNumerator / RateDenominator
    uint32_t RateNumerator{240};
    uint32_t RateDenominator{1};
    int32_t Frames{600};
    //! The seed the frames' times are drawn with (SplitMix64)
    uint64_t Seed{1};
    LoopProfile Profile{LoopProfile::RenderLate};
    //! true: the loop knows when the display refreshes and puts its times on the vertical blanks; false: it has a timer only
    bool HasVBlankTimes{true};
    //! Where in a refresh the loop presents when it has the vertical blank times, in percent of the refresh
    int32_t VBlankPhasePercent{65};
    //! A frame's work on the CPU, from its start to its submit
    TickRange CpuWork{1'000, 1'000};
    //! The frames (by their number from 0) whose CPU work is LongFrameCpuTicks longer: frames that run long
    std::vector<int32_t> LongFrames;
    int64_t LongFrameCpuTicks{0};
    //! A frame's work on the GPU, which works on one frame at a time
    TickRange GpuWork{37'500, 37'500};
    //! How long after its time a timer wakes
    TickRange TimerLate{0, 0};
    //! From a present to where the loop is ready for the next frame (the present never waits)
    int64_t LoopTicks{600};
    //! true: one frame in flight, so a frame's start waits for the GPU's work on the frame before it
    bool WaitsForPreviousGpuWork{true};
    //! false: paced at a fixed swap interval of 1, the rule off
    bool AutoSwapInterval{true};
    DisplayModelSettings Display;
  };
}

#endif
