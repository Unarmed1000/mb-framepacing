#ifndef MB_FRAMEPACING_PACER_SIMULATION_LOOPFRAMECALLBACKGAP_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_LOOPFRAMECALLBACKGAP_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer::Simulation
{
  //! A stretch of a simulated run in which the window system calls for no new frame (LoopSettings::FrameCallbackGaps): a window
  //! that is not shown. The calls it holds back come together at its end, in the order of the presents.
  struct LoopFrameCallbackGap
  {
    //! How long after the start of the run's first frame the stretch begins
    int64_t AfterNanoseconds{0};
    //! How long it lasts
    int64_t ForNanoseconds{0};
  };
}

#endif
