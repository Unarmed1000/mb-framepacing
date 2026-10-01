// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
#include "PacerSimulation.hpp"
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/animation/AnimationClock.hpp>
#include <mb/framepacing/pacer/animation/AnimationTime.hpp>
#include <mb/framepacing/pacer/frame/FrameEnd.hpp>
#include <mb/framepacing/pacer/frame/FrameInput.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <fstream>
#include <sstream>
#include <stdexcept>
#include <string>

namespace MB::FramePacing::Pacer::Simulation
{
  namespace
  {
    constexpr int64_t Milliseconds(const int64_t milliseconds) noexcept
    {
      return milliseconds * TimeSpan::TicksPerMillisecond;
    }

    //! SplitMix64 (Steele, Lea and Flood, 2014): 64-bit integer arithmetic only, so C# draws the same numbers
    class SplitMix64
    {
      uint64_t m_state;

    public:
      explicit SplitMix64(const uint64_t seed) noexcept
        : m_state(seed)
      {
      }

      uint64_t Next() noexcept
      {
        m_state += 0x9E37'79B9'7F4A'7C15u;
        uint64_t z = m_state;
        z = (z ^ (z >> 30u)) * 0xBF58'476D'1CE4'E5B9u;
        z = (z ^ (z >> 27u)) * 0x94D0'49BB'1331'11EBu;
        return z ^ (z >> 31u);
      }
    };

    int64_t Draw(SplitMix64& random, const LoadStage& stage) noexcept
    {
      const auto range = static_cast<uint64_t>(stage.MaxWorkTicks - stage.MinWorkTicks + 1);
      return stage.MinWorkTicks + static_cast<int64_t>(random.Next() % range);
    }

    //! The first refresh at or after ticks (from refresh 0 at 0)
    int64_t FirstRefreshAtOrAfter(const RefreshPeriod period, const int64_t ticks) noexcept
    {
      const int64_t refresh = period.FloorRefreshes(ticks);
      return period.TicksFor(refresh) < ticks ? refresh + 1 : refresh;
    }

    std::string_view ChangeName(const SwapIntervalChange change) noexcept
    {
      switch (change)
      {
      case SwapIntervalChange::Slower:
        return "Slower";
      case SwapIntervalChange::Faster:
        return "Faster";
      case SwapIntervalChange::None:
        break;
      }
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
    if (line != "workTicks,referenceSwapInterval,referenceShownRefresh")
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
    // The busy stretch of mb-framepacing-explained's 60-busy clip, frame by frame, played twice as its simulation does (so the clip
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
    stages.DurationTicks = Milliseconds(24'000);
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
    relapse.DurationTicks = Milliseconds(13'000);
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
    FramePacer pacer(settings);
    AnimationClock clock(period);
    SplitMix64 random(scenario.Seed);

    const std::size_t passFrames = scenario.Frames.size();
    const std::size_t listFrames = passFrames * static_cast<std::size_t>(std::max(scenario.Passes, 1));
    std::ostringstream out;
    out << ResultHeader << '\n';
    int64_t now = StartTicks;
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
        const int64_t elapsed = now - StartTicks;
        if (elapsed >= scenario.DurationTicks)
        {
          break;
        }
        const auto stage = std::find_if(scenario.Stages.begin(), scenario.Stages.end(), [elapsed](const LoadStage& candidate)
                                        { return candidate.FromTicks <= elapsed && elapsed < candidate.ToTicks; });
        source.WorkTicks = Draw(random, stage != scenario.Stages.end() ? *stage : scenario.Calm);
      }

      const FrameSchedule schedule = pacer.BeginFrame({now});
      const WindowState window = pacer.Window();
      const int64_t target = period.NearestRefreshes(schedule.IntendedDisplayTicks - StartTicks);
      const int64_t done = now + source.WorkTicks;
      const int64_t shown = std::max(target, FirstRefreshAtOrAfter(period, done - StartTicks));
      pacer.EndFrame({done, source.WorkTicks});
      const AnimationTime animation = clock.Advance(schedule);

      out << frame << ',' << source.WorkTicks << ',' << target << ',' << shown << ',' << (shown > target ? 1 : 0) << ',' << schedule.SwapInterval
          << ',' << ChangeName(schedule.Change) << ',' << schedule.IntendedDisplayTicks << ',' << animation.AnimationTicks << ',' << window.Frames
          << ',' << window.LateFrames << ',' << source.ReferenceSwapInterval << ',' << source.ReferenceShownRefresh << '\n';
      // The next frame starts when this one is shown
      now = StartTicks + period.TicksFor(shown);
    }
    return out.str();
  }
}
