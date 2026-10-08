// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include "PacerSimulation.hpp"
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/capability/PacerCapabilities.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
#include <mb/framepacing/pacer/tier/TierPacer.hpp>
#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <fstream>
#include <sstream>
#include <stdexcept>
#include <string>
#include "SplitMix64.hpp"

namespace MB::FramePacing::Pacer::Simulation
{
  namespace
  {
    constexpr int64_t Milliseconds(const int64_t milliseconds) noexcept
    {
      return milliseconds * NanosecondTimeSpan::NanosecondsPerMillisecond;
    }

    int64_t Draw(SplitMix64& random, const LoadStage& stage) noexcept
    {
      // In steps of 100 ns, the steps the golden scenarios were first drawn in: they stay the frames they were
      constexpr int64_t Step = 100;
      return random.Draw(stage.MinWorkNanoseconds / Step, stage.MaxWorkNanoseconds / Step) * Step;
    }

    //! The first refresh at or after nanoseconds (from refresh 0 at 0)
    int64_t FirstRefreshAtOrAfter(const RefreshPeriod period, const int64_t nanoseconds) noexcept
    {
      const int64_t refresh = period.FloorRefreshes(NanosecondTimeSpan(nanoseconds));
      return period.TimeFor(refresh).Nanoseconds() < nanoseconds ? refresh + 1 : refresh;
    }

    std::string_view ChangeName(const SwapIntervalChange change) noexcept
    {
      switch (change)
      {
      case SwapIntervalChange::Slower:
        return "Slower";
      case SwapIntervalChange::Faster:
        return "Faster";
      case SwapIntervalChange::Unchanged:
        break;
      }
      // The golden files' word for it
      return "None";
    }

    std::string_view RuleName(const SlowDownRule rule) noexcept
    {
      return rule == SlowDownRule::FullWindow ? "FullWindow" : "LateCount";
    }

    std::vector<std::string> SplitCsv(const std::string& line)
    {
      std::vector<std::string> fields;
      std::stringstream stream(line);
      std::string field;
      while (std::getline(stream, field, ','))
      {
        fields.push_back(field);
      }
      return fields;
    }
  }

  std::vector<ScenarioFrame> ReadFrames(const std::filesystem::path& path)
  {
    std::ifstream file(path);
    if (!file)
    {
      throw std::runtime_error("Cannot open " + path.string());
    }
    std::vector<ScenarioFrame> frames;
    std::string line;
    std::getline(file, line);
    if (line.starts_with("\xEF\xBB\xBF"))
    {
      line.erase(0, 3);
    }
    if (line != "workNs,referenceSwapInterval,referenceShownRefresh")
    {
      throw std::runtime_error(path.string() + ": not a scenario frames file");
    }
    while (std::getline(file, line))
    {
      if (!line.empty() && line.back() == '\r')
      {
        line.pop_back();
      }
      if (line.empty())
      {
        continue;
      }
      const std::vector<std::string> fields = SplitCsv(line);
      if (fields.size() != 3)
      {
        throw std::runtime_error(path.string() + ": a row without three fields");
      }
      frames.push_back({std::stoll(fields[0]), std::stoi(fields[1]), std::stoll(fields[2])});
    }
    return frames;
  }

  std::vector<Scenario> GoldenScenarios(const std::filesystem::path& testDataPacer)
  {
    // The busy stretch of mb-framepacing-explained's 60-busy-adaptive clip, frame by frame, played twice as its simulation does (so the clip
    // starts in the state it ends in); its swap intervals and refreshes are the reference
    Scenario busy;
    busy.Name = "60-busy";
    busy.RateNumerator = 60;
    busy.Frames = ReadFrames(testDataPacer / "60-busy-frames.csv");
    busy.Passes = 2;

    // mb-framepacing-explained's 60-busy-full-rate clip: the same kind of busy stretch, presented at every refresh without a pacer that
    // adapts. At a fixed swap interval of 1 the pacer must show every frame on the clip's refresh
    Scenario fullRate;
    fullRate.Name = "60-busy-full-rate";
    fullRate.RateNumerator = 60;
    fullRate.Frames = ReadFrames(testDataPacer / "60-busy-full-rate-frames.csv");
    fullRate.AutoSwapInterval = false;

    // mb-framepacing-explained's 100 Hz chart (tools/timing_diagrams/generate_adaptive_rate.py): calm 5 to 8 ms frames, and a load that
    // rises one refresh at a time from 1.5 s and falls back until 20.5 s, of 24 s
    Scenario stages;
    stages.Name = "100-stages";
    stages.RateNumerator = 100;
    stages.DurationNanoseconds = Milliseconds(24'000);
    stages.Seed = 20'260'930;
    stages.Calm = {0, 0, Milliseconds(5), Milliseconds(8)};
    stages.Stages = {
      {Milliseconds(1'500), Milliseconds(5'500), Milliseconds(13), Milliseconds(17)},
      {Milliseconds(5'500), Milliseconds(9'500), Milliseconds(22), Milliseconds(27)},
      {Milliseconds(9'500), Milliseconds(13'500), Milliseconds(31), Milliseconds(37)},
      {Milliseconds(13'500), Milliseconds(17'000), Milliseconds(22), Milliseconds(27)},
      {Milliseconds(17'000), Milliseconds(20'500), Milliseconds(13), Milliseconds(17)},
    };
    // The case mb-framepacing-explained gives for the late count fix (web/content/slides/adapt-rate.md): the busy stretch of the 60-busy
    // clip (12 to 24 ms frames from 1.5 s, calm 9 to 13 ms frames around it), and the load back again right after the rule has sped up
    // (it does so at 6.47 s): the full-window rule waits a full window before it slows down again, the fix slows down after the late frames of one
    Scenario relapse;
    relapse.Name = "60-relapse";
    relapse.RateNumerator = 60;
    relapse.DurationNanoseconds = Milliseconds(13'000);
    relapse.Seed = 20'261'001;
    relapse.Calm = {0, 0, Milliseconds(9), Milliseconds(13)};
    relapse.Stages = {
      {Milliseconds(1'500), Milliseconds(5'500), Milliseconds(12), Milliseconds(24)},
      {Milliseconds(6'600), Milliseconds(10'600), Milliseconds(12), Milliseconds(24)},
    };
    return {busy, fullRate, stages, relapse};
  }

  std::vector<SlowDownRule> RulesFor(const Scenario& scenario)
  {
    if (!scenario.AutoSwapInterval)
    {
      return {SlowDownRule::LateCount};
    }
    return {SlowDownRule::FullWindow, SlowDownRule::LateCount};
  }

  std::string ResultFileName(const Scenario& scenario, const SlowDownRule rule)
  {
    return scenario.Name + "-" + std::string(scenario.AutoSwapInterval ? RuleName(rule) : "Fixed") + ".csv";
  }

  std::string Simulate(const Scenario& scenario, const SlowDownRule rule)
  {
    const RefreshPeriod period = RefreshPeriod::FromRate(scenario.RateNumerator, scenario.RateDenominator);
    PacerSettings settings(period);
    settings.SetSlowDown(rule);
    settings.SetAutoSwapInterval(scenario.AutoSwapInterval);
    // The frame model has a display that shows a frame at the first refresh after it is done and a loop that starts the next
    // frame then: the baseline, a clock and the refresh period, with nothing made ahead and no pause of the pacer's own
    settings.SetAim(PacerAim::LowLatency);
    settings.SetStartupPauseRefreshes(0);
    TierPacer pacer(settings, PacerCapabilities());
    SplitMix64 random(scenario.Seed);

    const std::size_t passFrames = scenario.Frames.size();
    const std::size_t listFrames = passFrames * static_cast<std::size_t>(std::max(scenario.Passes, 1));
    std::ostringstream out;
    out << ResultHeader << '\n';
    int64_t now = StartNanoseconds;
    for (std::size_t frame = 0;; ++frame)
    {
      ScenarioFrame source;
      if (passFrames > 0)
      {
        if (frame >= listFrames)
        {
          break;
        }
        source = scenario.Frames[frame % passFrames];
        // The reference describes the last pass only
        if (frame < listFrames - passFrames)
        {
          source.ReferenceSwapInterval = 0;
          source.ReferenceShownRefresh = -1;
        }
      }
      else
      {
        const int64_t elapsed = now - StartNanoseconds;
        if (elapsed >= scenario.DurationNanoseconds)
        {
          break;
        }
        const auto stage = std::find_if(scenario.Stages.begin(), scenario.Stages.end(), [elapsed](const LoadStage& candidate)
                                        { return candidate.FromNanoseconds <= elapsed && elapsed < candidate.ToNanoseconds; });
        source.WorkNanoseconds = Draw(random, stage != scenario.Stages.end() ? *stage : scenario.Calm);
      }

      const FrameSchedule schedule = pacer.BeginFrame(NanosecondTickCount(now));
      const FrameWindowState window = pacer.FrameWindow();
      const int64_t intendedDisplayNanoseconds = schedule.IntendedDisplayTime.Nanoseconds();
      const int64_t target = period.NearestRefreshes(NanosecondTimeSpan(intendedDisplayNanoseconds - StartNanoseconds));
      const int64_t done = now + source.WorkNanoseconds;
      const int64_t shown = std::max(target, FirstRefreshAtOrAfter(period, done - StartNanoseconds));
      const PresentPlan plan = pacer.EndFrame(NanosecondTickCount(done));
      // Presented when the work is done, and the present returns at once
      PresentReport report;
      report.FrameId = plan.FrameId;
      report.CallTime = NanosecondTickCount(done);
      report.ReturnTime = NanosecondTickCount(done);
      pacer.AddPresent(report);

      out << frame << ',' << source.WorkNanoseconds << ',' << target << ',' << shown << ',' << (shown > target ? 1 : 0) << ','
          << schedule.SwapInterval << ',' << ChangeName(schedule.Change) << ',' << intendedDisplayNanoseconds << ','
          << schedule.AnimationTime.Nanoseconds() << ',' << window.Frames << ',' << window.LateFrames << ',' << source.ReferenceSwapInterval << ','
          << source.ReferenceShownRefresh << '\n';
      // The next frame starts when this one is shown
      now = StartNanoseconds + period.TimeFor(shown).Nanoseconds();
    }
    return out.str();
  }
}
