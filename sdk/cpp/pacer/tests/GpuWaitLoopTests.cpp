// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The wait for the GPU's work on an earlier frame as what holds the loop, on the simulation's display
// (SimulateTierLoop): the tiers without a wait for a present, both aims. The simulation only: not measured, and what it is worth
// under a real GPU load is to be measured before more is said of it.
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <gtest/gtest.h>
#include <algorithm>
#include <array>
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
  //! The two ways of pacing without a wait for a present
  constexpr std::array<PacerCapability, 2> Kinds = {PacerCapability::NoCapabilities, PacerCapability::VBlankTimes};

  //! 240 Hz, a tenth of a refresh of work on the CPU, GPU work as a share of a refresh in percent, no pause after start-up
  Sim::LoopSettings Loop(const PC::PacerAim aim, const int64_t gpuPercent)
  {
    Sim::LoopSettings settings;
    settings.Frames = 1'200;
    settings.Aim = aim;
    settings.StartupPauseRefreshes = 0;
    const int64_t period = PC::RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator).ToNanosecondTimeSpan().Nanoseconds();
    settings.GpuWork = {(period * gpuPercent) / 100, (period * gpuPercent) / 100};
    return settings;
  }

  int64_t PeriodNanoseconds(const Sim::LoopSettings& settings)
  {
    return PC::RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator).ToNanosecondTimeSpan().Nanoseconds();
  }

  //! The most frames the GPU had not finished when a frame started, the frames before it only, from first on
  int32_t MostInFlight(const std::vector<Sim::LoopFrame>& frames, const std::size_t first)
  {
    int32_t most = 0;
    for (std::size_t index = first; index < frames.size(); ++index)
    {
      int32_t inFlight = 0;
      for (std::size_t earlier = index; earlier > 0 && inFlight < 64; --earlier)
      {
        if (frames[earlier - 1].GpuEndNanoseconds <= frames[index].StartNanoseconds)
        {
          break;
        }
        ++inFlight;
      }
      most = std::max(most, inFlight);
    }
    return most;
  }

  //! The frames from first on that were not shown their swap interval after the frame before them
  int32_t OffTheSwapInterval(const std::vector<Sim::LoopFrame>& frames, const std::size_t first, const int64_t periodNanoseconds)
  {
    int32_t off = 0;
    for (std::size_t index = first; index < frames.size(); ++index)
    {
      const int64_t refreshes = ((frames[index].ShownNanoseconds - frames[index - 1].ShownNanoseconds) + (periodNanoseconds / 2)) / periodNanoseconds;
      off += refreshes != int64_t{frames[index].SwapInterval} ? 1 : 0;
    }
    return off;
  }
}

TEST(GpuWaitLoop, WithLightWorkTheWaitMovesNoFrame)
{
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    for (const PacerCapability kind : Kinds)
    {
      // The loop that waits for its frame slot by itself, and the one whose pacer asks for that wait
      Sim::LoopSettings settings = Loop(aim, 20);
      const int64_t period = PeriodNanoseconds(settings);
      const std::vector<Sim::LoopFrame> expected = Sim::SimulateTierLoop(settings, kind);
      settings.HasGpuWait = true;
      const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, kind);
      ASSERT_EQ(frames.size(), expected.size());
      for (std::size_t index = 0; index < frames.size(); ++index)
      {
        ASSERT_EQ(frames[index].StartNanoseconds, expected[index].StartNanoseconds) << index;
        ASSERT_EQ(frames[index].ShownNanoseconds, expected[index].ShownNanoseconds) << index;
        // The wait is made first, when the frame before was just presented, and the wait for the start time after it: the
        // GPU is done long before that time
        ASSERT_LT(frames[index].GpuWaitBlockedNanoseconds, period / 2) << index;
        // The frame before with low latency; with smoothness too, as the application lets one frame be in flight
        ASSERT_EQ(frames[index].GpuWaitFrameId, static_cast<uint64_t>(index)) << index;
      }
    }
  }
}

TEST(GpuWaitLoop, ALoopTheGpuLimitsIsHeldToTheFramesThatMayBeInFlight)
{
  // GPU work of 130 % of a refresh, and an application that lets two frames be in flight
  for (const PacerCapability kind : Kinds)
  {
    for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
    {
      Sim::LoopSettings settings = Loop(aim, 130);
      settings.MaxFramesInFlight = 2;
      settings.WaitsForPreviousGpuWork = false;
      const int64_t period = PeriodNanoseconds(settings);
      const int32_t mayBeInFlight = aim == PC::PacerAim::Smoothness ? 1 : 0;
      SCOPED_TRACE(testing::Message() << static_cast<uint32_t>(kind) << " aim " << static_cast<int32_t>(aim));

      // Nobody reports the GPU's work, and the loop makes no wait: it makes frames the GPU has not got to, more and more
      const std::vector<Sim::LoopFrame> unheld = Sim::SimulateTierLoop(settings, kind);
      EXPECT_GT(MostInFlight(unheld, 100), 20);

      // With the pacer's wait: one frame before it with the aim of low latency, two with smoothness, and never more
      settings.HasGpuWait = true;
      const std::vector<Sim::LoopFrame> held = Sim::SimulateTierLoop(settings, kind);
      EXPECT_EQ(MostInFlight(held, 100), mayBeInFlight);
      for (std::size_t index = 100; index < held.size(); ++index)
      {
        ASSERT_EQ(held[index].GpuWaitFrameId, static_cast<uint64_t>(index) - static_cast<uint64_t>(mayBeInFlight)) << index;
      }

      // The wait says that the GPU is done with a frame, not how long it worked on it: with the GPU's work reported too the
      // rule knows what a frame takes, stays at two refreshes per frame, and every frame is on screen for two
      settings.ReportsGpuWork = true;
      const std::vector<Sim::LoopFrame> reported = Sim::SimulateTierLoop(settings, kind);
      EXPECT_LE(MostInFlight(reported, 100), mayBeInFlight);
      EXPECT_EQ(reported.back().SwapInterval, 2u);
      EXPECT_EQ(OffTheSwapInterval(reported, 800, period), 0);
    }
  }
}

TEST(GpuWaitLoop, WorkOfThreeQuartersOfARefreshOnEachSideHoldsOneRefreshPerFrameWithSmoothnessAndNeedsTwoWithLowLatency)
{
  // The CPU and the GPU each work 72 % of a refresh on a frame, the GPU's work is reported, and the application lets two
  // frames be in flight
  for (const PacerCapability kind : Kinds)
  {
    Sim::LoopSettings settings = Loop(PC::PacerAim::Smoothness, 72);
    settings.CpuWork = {3'000'000, 3'000'000};
    settings.ReportsGpuWork = true;
    settings.MaxFramesInFlight = 2;
    settings.WaitsForPreviousGpuWork = false;
    settings.HasGpuWait = true;
    const int64_t period = PeriodNanoseconds(settings);
    SCOPED_TRACE(testing::Message() << static_cast<uint32_t>(kind));

    // Smoothness: the wait is for the frame before the last one, so the CPU works on a frame while the GPU has the one before
    // it: side by side they fit a refresh
    const std::vector<Sim::LoopFrame> sideBySide = Sim::SimulateTierLoop(settings, kind);
    EXPECT_EQ(sideBySide.back().SwapInterval, 1u);
    EXPECT_EQ(OffTheSwapInterval(sideBySide, 400, period), 0);
    EXPECT_EQ(MostInFlight(sideBySide, 100), 1);

    // Low latency: the wait is for the frame before, so the two come one after the other, which is a refresh and a half:
    // two refreshes per frame. What the aim costs a loop like this one, and to be measured before more is said of it
    settings.Aim = PC::PacerAim::LowLatency;
    const std::vector<Sim::LoopFrame> inSeries = Sim::SimulateTierLoop(settings, kind);
    EXPECT_EQ(inSeries.back().SwapInterval, 2u);
    EXPECT_EQ(OffTheSwapInterval(inSeries, 800, period), 0);
    EXPECT_EQ(MostInFlight(inSeries, 100), 0);
  }
}
