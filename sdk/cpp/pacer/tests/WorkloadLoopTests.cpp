// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The loads the tests of Android's frame pacing library put on that library (a 60 Hz display; CPU and GPU work of
// 10, 30 and 40 ms a frame; a load that ends after three seconds), put on the tier pacer in the simulation's loop
// (SimulateTierLoop), to hold what the swap interval rule does with them against what those tests expect
// (sdk/doc/pacer-design.md has the two side by side). The simulation only: numbers of a model, and nothing measured.
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <gtest/gtest.h>
#include <cstddef>
#include <cstdint>
#include <vector>
#include "FrameLoopSimulation.hpp"
#include "LoopFrame.hpp"
#include "LoopSettings.hpp"

namespace PC = MB::FramePacing::Pacer;
namespace Sim = MB::FramePacing::Pacer::Simulation;
using PC::PacerCapability;

namespace
{
  constexpr int64_t Millisecond = 1'000'000;
  constexpr int64_t Second = 1'000'000'000;

  //! A 60 Hz display and a loop that has what that library uses: where the refreshes are, a time on the present (or none: its
  //! path without one) and a wait for the GPU's work, with the GPU's work reported and two frames let be in flight
  Sim::LoopSettings Loop(const PC::PacerAim aim, const bool timedPresent, const int64_t cpuMilliseconds, const int64_t gpuMilliseconds,
                         const int32_t seconds)
  {
    Sim::LoopSettings settings;
    settings.RateNumerator = 60;
    settings.Frames = seconds * 60;
    settings.Aim = aim;
    settings.StartupPauseRefreshes = 0;
    settings.CpuWork = {cpuMilliseconds * Millisecond, cpuMilliseconds * Millisecond};
    settings.GpuWork = {gpuMilliseconds * Millisecond, gpuMilliseconds * Millisecond};
    settings.ReportsGpuWork = true;
    settings.MaxFramesInFlight = 2;
    settings.WaitsForPreviousGpuWork = false;
    settings.HasGpuWait = true;
    settings.PresentsAtTime = timedPresent;
    return settings;
  }

  //! What a run came to
  struct Outcome
  {
    //! The frames shown in each second of the run, from its first frame's start
    std::vector<int32_t> ShownPerSecond;
    //! When the swap interval changed, in milliseconds after the first frame's start, and what it changed to
    std::vector<int64_t> ChangeMilliseconds;
    std::vector<uint32_t> ChangeTo;
  };

  Outcome RunLoop(const Sim::LoopSettings& settings, const int32_t seconds)
  {
    const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, PacerCapability::VBlankTimes);
    Outcome outcome;
    outcome.ShownPerSecond.assign(static_cast<std::size_t>(seconds), 0);
    const int64_t origin = frames.front().StartNanoseconds;
    uint32_t swapInterval = frames.front().SwapInterval;
    for (const Sim::LoopFrame& frame : frames)
    {
      const int64_t second = (frame.ShownNanoseconds - origin) / Second;
      if (second >= 0 && second < seconds)
      {
        ++outcome.ShownPerSecond[static_cast<std::size_t>(second)];
      }
      if (frame.SwapInterval != swapInterval)
      {
        swapInterval = frame.SwapInterval;
        outcome.ChangeMilliseconds.push_back((frame.StartNanoseconds - origin) / Millisecond);
        outcome.ChangeTo.push_back(swapInterval);
      }
    }
    return outcome;
  }

  //! A steady load: the swap interval the rule goes to, once and within the first second and a half, and the frames a second
  //! from the third second on
  void ExpectSteady(const PC::PacerAim aim, const int64_t cpuMilliseconds, const int64_t gpuMilliseconds, const uint32_t swapInterval,
                    const int32_t framesPerSecond)
  {
    for (const bool timed : {true, false})
    {
      const Outcome outcome = RunLoop(Loop(aim, timed, cpuMilliseconds, gpuMilliseconds, 6), 6);
      if (swapInterval == 1u)
      {
        EXPECT_TRUE(outcome.ChangeTo.empty()) << timed;
      }
      else
      {
        ASSERT_EQ(outcome.ChangeTo.size(), 1u) << timed;
        EXPECT_EQ(outcome.ChangeTo[0], swapInterval) << timed;
        EXPECT_LT(outcome.ChangeMilliseconds[0], 1'500) << timed;
      }
      for (std::size_t second = 2; second < outcome.ShownPerSecond.size(); ++second)
      {
        EXPECT_EQ(outcome.ShownPerSecond[second], framesPerSecond) << timed << " " << second;
      }
    }
  }
}

TEST(WorkloadLoop, WithSmoothnessTheCpuAndTheGpuWorkSideBySideAndTheLongerOfTheTwoSetsTheFrameRate)
{
  // 10 ms on each fit a refresh of 16.7 ms side by side: every refresh a frame. That library's tests expect 60 frames in a
  // second
  ExpectSteady(PC::PacerAim::Smoothness, 10, 10, 1, 60);
  // 30 ms on either side need two refreshes: 30 frames a second. Its tests expect 33 and 30 in a first second in which it has
  // not slowed down yet
  ExpectSteady(PC::PacerAim::Smoothness, 30, 10, 2, 30);
  ExpectSteady(PC::PacerAim::Smoothness, 10, 30, 2, 30);
  // 40 ms on both need three: 20 frames a second, where its tests expect 22
  ExpectSteady(PC::PacerAim::Smoothness, 40, 40, 3, 20);
}

TEST(WorkloadLoop, WithLowLatencyTheyWorkOneAfterTheOtherAndTheTwoAddedSetTheFrameRate)
{
  // One frame in flight: 10 ms and 10 ms are 20 ms, which is two refreshes. What the aim costs a loop that would fit side by
  // side: that library runs such a loop the other way by itself
  ExpectSteady(PC::PacerAim::LowLatency, 10, 10, 2, 30);
  ExpectSteady(PC::PacerAim::LowLatency, 30, 10, 3, 20);
  ExpectSteady(PC::PacerAim::LowLatency, 10, 30, 3, 20);
  // 80 ms are five refreshes
  ExpectSteady(PC::PacerAim::LowLatency, 40, 40, 5, 12);
}

TEST(WorkloadLoop, ALoadThatEndsIsFollowedDownAtOnceAndUpAgainWithinTwoSeconds)
{
  // CPU work of 30 ms for three seconds, then 10 ms. That library's test expects two refreshes per frame from two seconds on
  // (it takes two seconds of frames before it changes anything) and one again 4.6 s into the run
  for (const bool timed : {true, false})
  {
    Sim::LoopSettings settings = Loop(PC::PacerAim::Smoothness, timed, 30, 10, 12);
    settings.WorkChanges.push_back({3 * Second, {10 * Millisecond, 10 * Millisecond}, {10 * Millisecond, 10 * Millisecond}});
    const Outcome smooth = RunLoop(settings, 12);
    ASSERT_EQ(smooth.ChangeTo.size(), 2u) << timed;
    // Slower within the first half second, as the frames are late from the first one on
    EXPECT_EQ(smooth.ChangeTo[0], 2u);
    EXPECT_LT(smooth.ChangeMilliseconds[0], 500) << timed;
    // Faster again once the frame window has no frame of the load left in it
    EXPECT_EQ(smooth.ChangeTo[1], 1u);
    EXPECT_GT(smooth.ChangeMilliseconds[1], 4'000) << timed;
    EXPECT_LT(smooth.ChangeMilliseconds[1], 5'000) << timed;
    EXPECT_EQ(smooth.ShownPerSecond[1], 30) << timed;
    EXPECT_EQ(smooth.ShownPerSecond[2], 30) << timed;
    for (std::size_t second = 5; second < smooth.ShownPerSecond.size(); ++second)
    {
      EXPECT_EQ(smooth.ShownPerSecond[second], 60) << timed << " " << second;
    }

    // With low latency: three refreshes per frame under the load, and two after it
    settings.Aim = PC::PacerAim::LowLatency;
    const Outcome lowLatency = RunLoop(settings, 12);
    ASSERT_EQ(lowLatency.ChangeTo.size(), 2u) << timed;
    EXPECT_EQ(lowLatency.ChangeTo[0], 3u);
    EXPECT_LT(lowLatency.ChangeMilliseconds[0], 1'000) << timed;
    EXPECT_EQ(lowLatency.ChangeTo[1], 2u);
    EXPECT_GT(lowLatency.ChangeMilliseconds[1], 3'000) << timed;
    EXPECT_LT(lowLatency.ChangeMilliseconds[1], 5'000) << timed;
    for (std::size_t second = 5; second < lowLatency.ShownPerSecond.size(); ++second)
    {
      EXPECT_EQ(lowLatency.ShownPerSecond[second], 30) << timed << " " << second;
    }
  }
}
