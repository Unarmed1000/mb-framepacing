// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// pacer-replay: gives the pacer module what a frame log's application gave its pacer, frame by frame, and writes what the pacer
// answers next to what the log shows (FrameLogReplay.hpp): a row per frame to stdout, a summary to stderr.
//
//   pacer-replay <log.csv> [--period-ns <n> | --rate <numerator> <denominator>] [--fixed] [--summary]
//
//   --period-ns   the refresh period the log's application gave its pacer, in nanoseconds; without it the log's target frame time
//                 (to the tick, which a replay that has to agree with the log to the tick may not be exact enough for)
//   --rate        the same as a refresh rate in Hz, numerator / denominator (a simulated loop's: pacer-sim --loop --rate)
//   --fixed       a fixed swap interval, the rule off
//   --summary     the summary only
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <cstddef>
#include <cstdint>
#include <exception>
#include <filesystem>
#include <iostream>
#include <map>
#include <string>
#include <string_view>
#include <vector>
#include "FrameLogReplay.hpp"

namespace Sim = MB::FramePacing::Pacer::Simulation;
using MB::FramePacing::Pacer::RefreshPeriod;

namespace
{
  int Usage()
  {
    std::cerr << "pacer-replay <log.csv> [--period-ns <n> | --rate <numerator> <denominator>] [--fixed] [--summary]\n";
    return 2;
  }

  void Counts(const std::string_view title, const std::map<int64_t, int64_t>& counts)
  {
    std::cerr << title;
    for (const auto& [value, frames] : counts)
    {
      std::cerr << ' ' << value << ": " << frames << ';';
    }
    std::cerr << '\n';
  }
}

// Every exception is caught below; clang-tidy still follows MSVC's standard library into allocation failures past the handlers
// NOLINTNEXTLINE(bugprone-exception-escape)
int main(const int argc, char** argv)
{
  try
  {
    const std::vector<std::string_view> args(argv + 1, argv + argc);
    if (args.empty())
    {
      return Usage();
    }
    int64_t periodNanoseconds = 0;
    uint32_t rateNumerator = 0;
    uint32_t rateDenominator = 1;
    bool autoSwapInterval = true;
    bool summaryOnly = false;
    for (std::size_t index = 1; index < args.size(); ++index)
    {
      if (args[index] == "--fixed")
      {
        autoSwapInterval = false;
      }
      else if (args[index] == "--summary")
      {
        summaryOnly = true;
      }
      else if (args[index] == "--period-ns" && index + 1 < args.size())
      {
        periodNanoseconds = std::stoll(std::string(args[++index]));
      }
      else if (args[index] == "--rate" && index + 2 < args.size())
      {
        rateNumerator = static_cast<uint32_t>(std::stoul(std::string(args[index + 1])));
        rateDenominator = static_cast<uint32_t>(std::stoul(std::string(args[index + 2])));
        index += 2;
      }
      else
      {
        return Usage();
      }
    }

    const std::vector<Sim::LoggedFrame> frames = Sim::ReadFrameLogFile(std::filesystem::path(args[0]));
    RefreshPeriod period = rateNumerator > 0 ? RefreshPeriod::FromRate(rateNumerator, rateDenominator) : Sim::LoggedRefreshPeriod(frames);
    if (rateNumerator == 0 && periodNanoseconds > 0)
    {
      period = RefreshPeriod::FromNanoseconds(periodNanoseconds);
    }
    const Sim::ReplayResult result = Sim::ReplayLog(frames, period, autoSwapInterval);
    if (!summaryOnly)
    {
      std::cout << result.Csv;
    }
    std::cerr << "frames replayed: " << result.Frames << " of " << frames.size() << " rows, at a refresh of " << period.ToTimeSpan().Ticks()
              << " ticks\n"
              << "the pacer's answers are the log's for " << result.Agreeing << " of " << result.Compared << " frames\n"
              << "frames with a display time: " << result.Shown << '\n';
    Counts("refreshes from a frame's start to its display (refreshes: frames):", result.RefreshesToDisplay);
    Counts("earlier frames presented and not yet shown at a frame's start (frames: frames):", result.PendingAtStart);
    Counts("refreshes a frame was shown after the pacer's intended display time (refreshes: frames):", result.RefreshesAfterIntended);
    return 0;
  }
  catch (const std::exception& error)
  {
    std::cerr << "error: " << error.what() << '\n';
    return 1;
  }
}
