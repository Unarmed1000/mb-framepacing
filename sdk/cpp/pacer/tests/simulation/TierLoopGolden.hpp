#ifndef MB_FRAMEPACING_PACER_SIMULATION_TIERLOOPGOLDEN_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_TIERLOOPGOLDEN_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <string>
#include <string_view>
#include <vector>
#include "LoopFrame.hpp"
#include "TierLoopGoldenRun.hpp"

//! The golden data of the tier pacer (test-data/pacer, written by pacer-sim --golden): runs of the simulated loop on the display
//! model, with every way of pacing that has a pacer, both aims and a list of cases. A run's frames are written as text, and
//! what is golden is that text: for a few runs the text itself, a file a difference can be read in, and for every run one line
//! of a digest file with the text's length and its CRC-32. Integer arithmetic only, so every platform gives the same bytes.
namespace MB::FramePacing::Pacer::Simulation::TierLoopGolden
{
  //! The digest file: a line per run.
  inline constexpr std::string_view DigestFileName = "tier-loops.csv";

  //! The runs, in the order of the digest file.
  [[nodiscard]] std::vector<TierLoopGoldenRun> Runs();

  //! The file of a run that is written whole: tier-loop-<name>.csv.
  [[nodiscard]] std::string FileNameOf(const TierLoopGoldenRun& run);

  //! A run's frames as text: a header line, and a line per frame with every value of the frame.
  [[nodiscard]] std::string ToCsv(const std::vector<LoopFrame>& frames);

  //! Simulates the run and gives its frames as text.
  [[nodiscard]] std::string Simulate(const TierLoopGoldenRun& run);

  //! The digest file's text for the runs: a header line, and for each run its name, its frames, the length of its text in bytes
  //! and the text's CRC-32.
  [[nodiscard]] std::string Digests(const std::vector<TierLoopGoldenRun>& runs);
}

#endif
