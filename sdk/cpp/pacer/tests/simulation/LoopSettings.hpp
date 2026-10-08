#ifndef MB_FRAMEPACING_PACER_SIMULATION_LOOPSETTINGS_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_LOOPSETTINGS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/pacer/PacerAim.hpp>
#include <cstdint>
#include <vector>
#include "DisplayModelSettings.hpp"
#include "LoopProfile.hpp"
#include "NanosecondRange.hpp"

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
    NanosecondRange CpuWork{100'000, 100'000};
    //! The frames (by their number from 0) whose CPU work is LongFrameCpuNanoseconds longer: frames that run long
    std::vector<int32_t> LongFrames;
    int64_t LongFrameCpuNanoseconds{0};
    //! A frame's work on the GPU, which works on one frame at a time
    NanosecondRange GpuWork{3'750'000, 3'750'000};
    //! How long after its time a timer wakes
    NanosecondRange TimerLate{0, 0};
    //! From a present to where the loop is ready for the next frame (the present never waits)
    int64_t LoopNanoseconds{60'000};
    //! true: one frame in flight, so a frame's start waits for the GPU's work on the frame before it
    bool WaitsForPreviousGpuWork{true};
    //! For a loop that waits for a present: the presents that may be waiting while a frame is made, and how long after the
    //! display took a frame a wait for it returns
    uint32_t WaitingPresents{2};
    NanosecondRange PresentWaitReturn{60'000, 2'400'000};
    //! For the tier pacers: what they optimize for (PacerSettings::Aim). Low latency unless a run asks for the other
    PacerAim Aim{PacerAim::LowLatency};
    //! For the tier pacers. true: the loop gives the pacer each frame's GPU work, begin and end, once it is done
    bool ReportsGpuWork{false};
    //! What the loop tells the pacer of the frames it lets be in flight (PacerSettings::MaxFramesInFlight)
    uint32_t MaxFramesInFlight{1};
    //! The pause after start-up of the pacer that has the refresh period only (PacerSettings::StartupPauseRefreshes)
    uint32_t StartupPauseRefreshes{4};
    //! For the tier pacers: the swap interval the application prefers (PacerSettings::PreferredSwapInterval): 4 is 60 frames
    //! a second at 240 Hz
    uint32_t PreferredSwapInterval{1};
    //! How much longer the display's refresh period really is than the one the loop was given, in parts per million (below
    //! zero: shorter). The tier pacers' loops only
    int64_t DisplayPeriodPpm{0};
    //! For the pacer of vertical blank times: where in a refresh a frame is to be ready (PacerSettings::ReadyPlacePercent)
    uint32_t ReadyPlacePercent{50};
    //! How far the vertical blank times the loop is given are off the display's: drawn anew for every reading. Zero: exact
    NanosecondRange VBlankReadingError;
    //! The loop tells a tier pacer of its own waits (for a frame slot, for an image), says that the system holds it while its
    //! queue is full, and gives the display's images as the swap chain's (where the display has a number of them)
    bool SystemHoldsLoop{false};
    //! For the tier pacers: the present takes a time before which the frame is not shown (PacerCapability::PresentAtTime),
    //! and the loop gives it the plan's
    bool PresentsAtTime{false};
    //! For the tier pacers: the present takes a time the frame before it stays on screen at least
    //! (PacerCapability::PresentAfterDuration), and the loop gives it the plan's
    bool PresentsAfterDuration{false};
    //! false: paced at a fixed swap interval of 1, the rule off
    bool AutoSwapInterval{true};
    DisplayModelSettings Display;
  };
}

#endif
