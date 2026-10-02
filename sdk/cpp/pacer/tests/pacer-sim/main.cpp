// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// pacer-sim: paces a scenario with the pacer module in the frame model of mb-framepacing-explained's simulations (PacerSimulation.hpp) and
// writes one row per frame: the refresh it targets and is shown on, the swap interval, the rule's decisions, the animation time.
//
//   pacer-sim --golden <dir>                                     every golden scenario with its rules into <dir> (sdk/test-data/pacer)
//   pacer-sim <frames.csv> <rate> [denominator] [--rule FullWindow|LateCount]   one scenario to stdout (rate: Hz, numerator / denominator)
#include <exception>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include <string_view>
#include <vector>
#include "PacerSimulation.hpp"

namespace Sim = MB::FramePacing::Pacer::Simulation;
using MB::FramePacing::Pacer::SlowDownRule;

namespace
{
  int Usage()
  {
    std::cerr << "pacer-sim --golden <dir>\n"
                 "pacer-sim <frames.csv> <rate> [denominator] [--rule FullWindow|LateCount]\n";
    return 2;
  }

  int WriteGolden(const std::filesystem::path& folder)
  {
    for (const Sim::Scenario& scenario : Sim::GoldenScenarios(folder))
    {
      for (const SlowDownRule rule : Sim::RulesFor(scenario))
      {
        const std::filesystem::path path = folder / Sim::ResultFileName(scenario, rule);
        std::ofstream file(path, std::ios::binary);
        file << Sim::Simulate(scenario, rule);
        std::cout << path.string() << '\n';
      }
    }
    return 0;
  }
}

// Every exception is caught below; clang-tidy still follows MSVC's standard library into allocation failures past the handlers
// NOLINTNEXTLINE(bugprone-exception-escape)
int main(const int argc, char** argv)
{
  try
  {
    const std::vector<std::string_view> args(argv + 1, argv + argc);
    if (args.size() == 2 && args[0] == "--golden")
    {
      return WriteGolden(args[1]);
    }
    if (args.size() < 2)
    {
      return Usage();
    }
    Sim::Scenario scenario;
    scenario.Name = std::filesystem::path(args[0]).stem().string();
    scenario.Frames = Sim::ReadFrames(args[0]);
    scenario.RateNumerator = static_cast<uint32_t>(std::stoul(std::string(args[1])));
    SlowDownRule rule = SlowDownRule::LateCount;
    for (std::size_t index = 2; index < args.size(); ++index)
    {
      if (args[index] == "--rule" && index + 1 < args.size())
      {
        rule = args[++index] == "FullWindow" ? SlowDownRule::FullWindow : SlowDownRule::LateCount;
      }
      else
      {
        scenario.RateDenominator = static_cast<uint32_t>(std::stoul(std::string(args[index])));
      }
    }
    std::cout << Sim::Simulate(scenario, rule);
    return 0;
  }
  catch (const std::exception& error)
  {
    std::cerr << "error: " << error.what() << '\n';
    return 1;
  }
}
