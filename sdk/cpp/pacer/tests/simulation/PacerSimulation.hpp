#ifndef MB_FRAMEPACING_PACER_SIMULATION_PACERSIMULATION_HPP
#define MB_FRAMEPACING_PACER_SIMULATION_PACERSIMULATION_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The frame model of mb-framepacing-explained's simulations (tools/frame_pacing_video/adaptive_rate.py): a frame starts when the previous
// one is shown, works its work time, and is shown at the refresh it targets, or at the first refresh after it is done when it is done too
// late. The pacer paces it through its public API (BeginFrame, EndFrame; no display-time feedback: it infers the display as the model
// shows it). pacer-sim writes the golden results with it (sdk/test-data/pacer); the C++ and C# tests compare with them.

#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/pacer/SlowDownRule.hpp>
#include <filesystem>
#include <string>
#include <string_view>
#include <vector>
#include "Scenario.hpp"

namespace MB::FramePacing::Pacer::Simulation
{
  //! The first frame starts at 1 s on the steady clock, on a refresh (refresh 0 of the results)
  inline constexpr int64_t StartTicks = TimeSpan::TicksPerSecond;

  //! The columns of a result: one row per frame
  inline constexpr std::string_view ResultHeader =
    "frame,workTicks,targetRefresh,shownRefresh,late,swapInterval,change,intendedDisplayTicks,"
    "animationTicks,windowFrames,windowLateFrames,referenceSwapInterval,referenceShownRefresh";

  //! A scenario's frames from a CSV file with the columns workTicks, referenceSwapInterval and referenceShownRefresh.
  std::vector<ScenarioFrame> ReadFrames(const std::filesystem::path& path);

  //! The golden scenarios: 60-busy (test-data/pacer/60-busy-frames.csv, twice), 60-busy-full-rate (its frames file, at a fixed swap
  //! interval), 100-stages and 60-relapse.
  std::vector<Scenario> GoldenScenarios(const std::filesystem::path& testDataPacer);

  //! The rules a scenario is paced with: both, or LateCount alone for a scenario at a fixed swap interval (no rule decides there).
  std::vector<SlowDownRule> RulesFor(const Scenario& scenario);

  //! The file name of a scenario's result with a rule: <name>-<rule>.csv, or <name>-Fixed.csv at a fixed swap interval
  std::string ResultFileName(const Scenario& scenario, SlowDownRule rule);

  //! Pace the scenario with the rule (the other settings at their defaults); the result as CSV text (ResultHeader, "\n" line ends).
  std::string Simulate(const Scenario& scenario, SlowDownRule rule);
}

#endif
