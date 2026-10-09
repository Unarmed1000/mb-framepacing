// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// pacer-sim: paces a scenario with the pacer module in the frame model of mb-framepacing-explained's simulations (PacerSimulation.hpp) and
// writes one row per frame: the refresh it targets and is shown on, the swap interval, the rule's decisions, the animation time.
//
//   pacer-sim --golden <dir>                                     every golden scenario with its rules into <dir> (sdk/test-data/pacer)
//   pacer-sim <frames.csv> <rate> [denominator] [--rule FullWindow|LateCount]   one scenario to stdout (rate: Hz, numerator / denominator)
//
// And the tier pacer in a frame loop on a display that queues its presents (FrameLoopSimulation.hpp), as a frame log to stdout
// with the column names of the first integration's logs. The word after --loop is kept from an earlier loop and changes
// nothing. Without --wait-for-present, --vblank-pacer or --vblank-wait-pacer the pacer has a timer and the refresh period only:
//
//   pacer-sim --loop late|early [--rate <Hz>] [--frames <n>] [--gpu-percent <of a refresh>] [--cpu-nanoseconds <n>]// [--timer-late-nanoseconds
//   <max>] [--fixed] [--seed <n>] [--latch-lead-percent <of a refresh>] [--pipeline <refreshes>]
//             [--images <n>] [--hold <blank>,<blank>,...] [--long-frame <frame>,<more CPU nanoseconds>] [--tier-pacer]
//             [--wait-for-present <presents that may wait>] [--gpu-reports] [--frames-in-flight <n>] [--startup-pause <refreshes>]
//
//   --tier-pacer  a timer and the refresh period only, also with --wait-for-present given (which is then the reserve's number)
//   --wait-for-present  the same with the pacer of a timer and a wait for a present
//   --gpu-reports       the loop gives a tier pacer each frame's GPU work, begin and end
//   --frames-in-flight  1: a frame starts when the GPU is done with the one before it; 2: the CPU works on a frame while the GPU
//                       works on the one before it. The loop does it and says so to a tier pacer
//   --startup-pause     the refreshes of the lowest tier's pause after start-up; 0 for none
//   --vblank-pacer      the pacer of vertical blank times, given the display's last vertical blank before every frame;
//                       --ready-place <percent>: where in a refresh a frame is to be ready. The reserve of --smooth is the
//                       --wait-for-present number less one here too
//   --vblank-wait-pacer the pacer of vertical blank times that waits for a present: --wait-for-present is the presents that
//                       may wait
//   --display-ppm       the display's refresh period is that many parts per million longer than the loop was told
//   --swap-interval     the swap interval a tier pacer's application prefers: 4 is 60 frames a second at 240 Hz
//   --system-holds      the loop tells the pacer of the lowest tier of its own waits and that the system holds it while its
//                       queue is full; with --images the display has that many, and the pacer is told so
//   --gpu-wait          a tier pacer's application can wait for the GPU's work on a frame: the plans of the pacers without a
//                       wait for a present ask for it, and the loop makes no frame slot wait of its own
//   --present-at-time   a tier pacer's present takes a time before which the frame is not shown: the tier with a timed present
//   --present-after-duration  the same with a time the frame before it stays on screen at least
//   --smooth            a tier pacer with the aim of smoothness (a reserve of frames that wait); low latency without it.
//                       With --tier-pacer the reserve is the --wait-for-present number less one, and the wait is not made
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/PacerAim.hpp>
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
#include "TierLoopGolden.hpp"
#include "TierLoopGoldenRun.hpp"

namespace Sim = MB::FramePacing::Pacer::Simulation;
using MB::FramePacing::Pacer::SlowDownRule;

namespace
{
  int Usage()
  {
    std::cerr << "pacer-sim --golden <dir>\n"
                 "pacer-sim <frames.csv> <rate> [denominator] [--rule FullWindow|LateCount]\n"
                 "pacer-sim --loop late|early [--rate <Hz>] [--frames <n>] [--gpu-percent <n>] [--cpu-nanoseconds <n>] [--timer-only]\n"
                 "          [--timer-late-nanoseconds <max>] [--fixed] [--seed <n>] [--latch-lead-percent <n>] [--pipeline <refreshes>]\n"
                 "          [--images <n>] [--hold <blank>,<blank>,...] [--long-frame <frame>,<more CPU nanoseconds>] [--tier-pacer]\n"
                 "          [--wait-for-present <presents that may wait>] [--gpu-reports] [--frames-in-flight <n>]\n"
                 "          [--startup-pause <refreshes>] [--smooth] [--vblank-pacer] [--ready-place <percent>]\n"
                 "          [--display-ppm <parts per million>] [--swap-interval <refreshes>] [--system-holds] [--vblank-wait-pacer]\n"
                 "          [--present-at-time] [--present-after-duration] [--gpu-wait]\n";
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
    int64_t gpuPercent = 90;
    int64_t latchLeadPercent = 0;
    bool tierPacer = false;
    bool waitForPresent = false;
    bool vblankPacer = false;
    bool vblankWaitPacer = false;
    for (std::size_t index = 2; index < args.size(); ++index)
    {
      const std::string_view name = args[index];
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
      if (name == "--vblank-wait-pacer")
      {
        vblankWaitPacer = true;
        continue;
      }
      if (name == "--vblank-pacer")
      {
        vblankPacer = true;
        continue;
      }
      if (name == "--smooth")
      {
        settings.Aim = MB::FramePacing::Pacer::PacerAim::Smoothness;
        continue;
      }
      if (name == "--gpu-wait")
      {
        settings.HasGpuWait = true;
        continue;
      }
      if (name == "--present-at-time")
      {
        settings.PresentsAtTime = true;
        continue;
      }
      if (name == "--present-after-duration")
      {
        settings.PresentsAfterDuration = true;
        continue;
      }
      if (name == "--system-holds")
      {
        settings.SystemHoldsLoop = true;
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
      else if (name == "--cpu-nanoseconds")
      {
        settings.CpuWork = {Number(value), Number(value)};
      }
      else if (name == "--timer-late-nanoseconds")
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
      else if (name == "--swap-interval")
      {
        settings.PreferredSwapInterval = static_cast<uint32_t>(Number(value));
      }
      else if (name == "--display-ppm")
      {
        settings.DisplayPeriodPpm = Number(value);
      }
      else if (name == "--ready-place")
      {
        settings.ReadyPlacePercent = static_cast<uint32_t>(Number(value));
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
        settings.LongFrameCpuNanoseconds = Number(value.substr(comma + 1));
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
    const int64_t periodNanoseconds = MB::FramePacing::Pacer::RefreshPeriod::FromRate(settings.RateNumerator).ToNanosecondTimeSpan().Nanoseconds();
    settings.GpuWork = {(periodNanoseconds * gpuPercent) / 100, (periodNanoseconds * gpuPercent) / 100};
    settings.Display.LatchLeadNanoseconds = (periodNanoseconds * latchLeadPercent) / 100;
    if (vblankWaitPacer)
    {
      std::cout << Sim::ToFrameLog(Sim::SimulateVBlankWaitForPresentLoop(settings), settings);
      return 0;
    }
    if (vblankPacer)
    {
      std::cout << Sim::ToFrameLog(Sim::SimulateVBlankPeriodOnlyLoop(settings), settings);
      return 0;
    }
    if (waitForPresent && !tierPacer)
    {
      std::cout << Sim::ToFrameLog(Sim::SimulateTimerWaitForPresentLoop(settings), settings);
      return 0;
    }
    // Neither: a timer and the refresh period only, the baseline
    std::cout << Sim::ToFrameLog(Sim::SimulateTimerPeriodOnlyLoop(settings), settings);
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
    // The tier pacer's simulated loop: a line per run in the digest file, and the runs that are written whole
    const std::vector<Sim::TierLoopGoldenRun> runs = Sim::TierLoopGolden::Runs();
    {
      const std::filesystem::path path = folder / Sim::TierLoopGolden::DigestFileName;
      std::ofstream file(path, std::ios::binary);
      file << Sim::TierLoopGolden::Digests(runs);
      std::cout << path.string() << '\n';
    }
    for (const Sim::TierLoopGoldenRun& run : runs)
    {
      if (run.WritesFrames)
      {
        const std::filesystem::path path = folder / Sim::TierLoopGolden::FileNameOf(run);
        std::ofstream file(path, std::ios::binary);
        file << Sim::TierLoopGolden::Simulate(run);
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
