#ifndef MB_FRAMEPACING_PACER_SIMULATION_TIERLOOPGOLDENRUN_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_TIERLOOPGOLDENRUN_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <string>
#include "LoopSettings.hpp"

namespace MB::FramePacing::Pacer::Simulation
{
  //! A run of the tier pacer's simulated loop whose result is golden data (TierLoopGolden): its name, the loop, and which of
  //! vertical blank times and the wait for a present the pacer is made with.
  struct TierLoopGoldenRun
  {
    std::string Name;
    LoopSettings Settings;
    PacerCapability Named{PacerCapability::NoCapabilities};
    //! true: every frame of the run is a golden file of its own. false: the run is one line of the digest file
    bool WritesFrames{false};
  };
}

#endif
