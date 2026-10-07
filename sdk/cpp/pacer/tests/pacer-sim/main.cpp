// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// pacer-sim: paces a scenario with the pacer module in the frame model of mb-framepacing-explained's simulations (PacerSimulation.hpp) and
// writes one row per frame: the refresh it targets and is shown on, the swap interval, the rule's decisions, the animation time.
//
//   pacer-sim --golden <dir>                                     every golden scenario with its rules into <dir> (sdk/test-data/pacer)
//   pacer-sim <frames.csv> <rate> [denominator] [--rule FullWindow|LateCount]   one scenario to stdout (rate: Hz, numerator / denominator)
//
// And a frame loop on a display that queues its presents (FrameLoopSimulation.hpp), as a frame log to stdout that
// tools/frame_stages_chart.py draws:
//
//   pacer-sim --loop late|early [--rate <Hz>] [--frames <n>] [--gpu-percent <of a refresh>] [--cpu-ticks <n>] [--timer-only]
//             [--timer-late-ticks <max>] [--fixed] [--seed <n>] [--latch-lead-percent <of a refresh>] [--pipeline <refreshes>]
//             [--images <n>] [--hold <blank>,<blank>,...] [--long-frame <frame>,<more CPU ticks>] [--tier-pacer]
//             [--wait-for-present <presents that may wait>] [--gpu-reports] [--frames-in-flight <n>] [--startup-pause <refreshes>]
//
//   --tier-pacer  the application carries out what the pacer of the lowest pair of tiers gives it, in place of today's pacer and
//                 the first integration's own calculations
//   --wait-for-present  the same with the pacer of a timer and a wait for a present
//   --gpu-reports       the loop gives a tier pacer each frame's GPU work, begin and end
//   --frames-in-flight  1: a frame starts when the GPU is done with the one before it; 2: the CPU works on a frame while the GPU
//                       works on the one before it. The loop does it and says so to a tier pacer
//   --startup-pause     the refreshes of the lowest pair's pause after start-up; 0 for none
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <algorithm>
#include <exception>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>
#include <string>
#include <string_view>
#include <vector>
#include "FrameLoopSimulation.hpp"
#include "PacerSimulation.hpp"

namespace Sim = MB::FramePacing::Pacer::Simulation;
using MB::FramePacing::Pacer::SlowDownRule;

namespace
{
  int Usage()
  {
    std::cerr << "pacer-sim --golden <dir>\n"
                 "pacer-sim <frames.csv> <rate> [denominator] [--rule FullWindow|LateCount]\n"
                 "pacer-sim --loop late|early [--rate <Hz>] [--frames <n>] [--gpu-percent <n>] [--cpu-ticks <n>] [--timer-only]\n"
                 "          [--timer-late-ticks <max>] [--fixed] [--seed <n>] [--latch-lead-percent <n>] [--pipeline <refreshes>]\n"
                 "          [--images <n>] [--hold <blank>,<blank>,...] [--long-frame <frame>,<more CPU ticks>] [--tier-pacer]\n"
                 "          [--wait-for-present <presents that may wait>] [--gpu-reports] [--frames-in-flight <n>]\n"
                 "          [--startup-pause <refreshes>]\n";
    return 2;
  }

  int64_t Number(const std::string_view text)
  {
    return std::stoll(std::string(text));
  }

  //! A frame loop on a queueing display, as a frame log
  int WriteLoop(const std::vector<std::string_view>& args)
  {
    if (args.size() < 2 || (args[1] != "late" && args[1] != "early"))
    {
      return Usage();
    }
    Sim::LoopSettings settings;
    settings.Profile = args[1] == "late" ? Sim::LoopProfile::RenderLate : Sim::LoopProfile::RenderEarly;
    int64_t gpuPercent = 90;
    int64_t latchLeadPercent = 0;
    bool tierPacer = false;
    bool waitForPresent = false;
    for (std::size_t index = 2; index < args.size(); ++index)
    {
      const std::string_view name = args[index];
      if (name == "--timer-only")
      {
        settings.HasVBlankTimes = false;
        continue;
      }
      if (name == "--fixed")
      {
        settings.AutoSwapInterval = false;
        continue;
      }
      if (name == "--tier-pacer")
      {
        tierPacer = true;
        continue;
      }
      if (name == "--gpu-reports")
      {
        settings.ReportsGpuWork = true;
        continue;
      }
      if (index + 1 >= args.size())
      {
        return Usage();
      }
      const std::string_view value = args[++index];
      if (name == "--rate")
      {
        settings.RateNumerator = static_cast<uint32_t>(Number(value));
      }
      else if (name == "--frames")
      {
        settings.Frames = static_cast<int32_t>(Number(value));
      }
      else if (name == "--gpu-percent")
      {
        gpuPercent = Number(value);
      }
      else if (name == "--cpu-ticks")
      {
        settings.CpuWork = {Number(value), Number(value)};
      }
      else if (name == "--timer-late-ticks")
      {
        settings.TimerLate = {0, Number(value)};
      }
      else if (name == "--seed")
      {
        settings.Seed = static_cast<uint64_t>(Number(value));
      }
      else if (name == "--latch-lead-percent")
      {
        latchLeadPercent = Number(value);
      }
      else if (name == "--pipeline")
      {
        settings.Display.PipelineRefreshes = static_cast<int32_t>(Number(value));
      }
      else if (name == "--images")
      {
        settings.Display.Images = static_cast<int32_t>(Number(value));
      }
      else if (name == "--frames-in-flight")
      {
        // What the loop does, and what it tells the pacer
        settings.MaxFramesInFlight = static_cast<uint32_t>(Number(value));
        settings.WaitsForPreviousGpuWork = settings.MaxFramesInFlight < 2;
      }
      else if (name == "--startup-pause")
      {
        settings.StartupPauseRefreshes = static_cast<uint32_t>(Number(value));
      }
      else if (name == "--wait-for-present")
      {
        waitForPresent = true;
        settings.WaitingPresents = static_cast<uint32_t>(Number(value));
      }
      else if (name == "--long-frame")
      {
        const std::size_t comma = value.find(',');
        if (comma == std::string_view::npos)
        {
          return Usage();
        }
        settings.LongFrames.push_back(static_cast<int32_t>(Number(value.substr(0, comma))));
        settings.LongFrameCpuTicks = Number(value.substr(comma + 1));
      }
      else if (name == "--hold")
      {
        std::string list(value);
        std::replace(list.begin(), list.end(), ',', ' ');
        std::istringstream blanks(list);
        for (int64_t blank = 0; blanks >> blank;)
        {
          settings.Display.HeldBlanks.push_back(blank);
        }
        std::sort(settings.Display.HeldBlanks.begin(), settings.Display.HeldBlanks.end());
      }
      else
      {
        return Usage();
      }
    }
    const int64_t periodTicks = MB::FramePacing::Pacer::RefreshPeriod::FromRate(settings.RateNumerator).ToTimeSpan().Ticks();
    settings.GpuWork = {(periodTicks * gpuPercent) / 100, (periodTicks * gpuPercent) / 100};
    settings.Display.LatchLeadTicks = (periodTicks * latchLeadPercent) / 100;
    if (waitForPresent)
    {
      std::cout << Sim::ToFrameLog(Sim::SimulateTimerWaitForPresentLoop(settings), settings);
      return 0;
    }
    std::cout << Sim::ToFrameLog(tierPacer ? Sim::SimulateTimerPeriodOnlyLoop(settings) : Sim::SimulateLoop(settings), settings);
    return 0;
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
    if (!args.empty() && args[0] == "--loop")
    {
      return WriteLoop(args);
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
