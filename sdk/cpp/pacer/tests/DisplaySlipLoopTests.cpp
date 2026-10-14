// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The second major tier with display times and no wait for a present (2.3, 2.4) on the simulation's display: the
// display's side places a frame after the one before it, and a refresh the display holds a frame for moves every frame after it.
// With the aim of smoothness the loop takes the hit (the frames wait a refresh longer); with low latency it starts them a refresh
// later (DisplaySlipRule). The display stays smooth either way. The simulation only: no system has been measured with it.
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <gtest/gtest.h>
#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <vector>
#include "FrameLoopSimulation.hpp"
#include "LoopFrame.hpp"
#include "LoopSettings.hpp"

namespace PC = MB::FramePacing::Pacer;
namespace Sim = MB::FramePacing::Pacer::Simulation;

namespace
{
  //! The display holds the frame this one is shown at: well after the frames settled
  constexpr std::size_t HeldFrame = 300;
  //! The frames before it, and long after it, that are compared
  constexpr std::size_t BeforeFrom = 100;
  constexpr std::size_t BeforeTo = 180;
  constexpr std::size_t AfterFrom = 700;

  //! Light work at 240 Hz, a duration on the present, display times, a display that holds one frame
  Sim::LoopSettings SlipLoop(const PC::PacerAim aim, const uint32_t swapInterval)
  {
    Sim::LoopSettings settings;
    settings.Frames = 1'200;
    settings.Aim = aim;
    settings.StartupPauseRefreshes = 0;
    settings.PreferredSwapInterval = swapInterval;
    settings.PresentsAfterDuration = true;
    settings.ReportsDisplayTimes = true;
    const int64_t period = PC::RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator).ToNanosecondTimeSpan().Nanoseconds();
    settings.GpuWork = {period / 5, period / 5};
    return settings;
  }

  int64_t PeriodNanoseconds(const Sim::LoopSettings& settings)
  {
    return PC::RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator).ToNanosecondTimeSpan().Nanoseconds();
  }

  //! The run with the display holding a frame at the vertical blank where HeldFrame is shown without the hold
  std::vector<Sim::LoopFrame> SimulateHeld(Sim::LoopSettings settings, const PC::PacerCapability named)
  {
    const std::vector<Sim::LoopFrame> plain = Sim::SimulateTierLoop(settings, named);
    const int64_t period = PeriodNanoseconds(settings);
    const int64_t shown = plain[HeldFrame].ShownNanoseconds - settings.Display.FirstBlankNanoseconds;
    settings.Display.HeldBlanks = {(shown + (period / 2)) / period};
    return Sim::SimulateTierLoop(settings, named);
  }

  //! The median time from a frame's start to its display, over frames [from, to)
  int64_t MedianToDisplay(const std::vector<Sim::LoopFrame>& frames, const std::size_t from, const std::size_t to)
  {
    std::vector<int64_t> times;
    for (std::size_t index = from; index < std::min(to, frames.size()); ++index)
    {
      times.push_back(frames[index].ShownNanoseconds - frames[index].StartNanoseconds);
    }
    std::sort(times.begin(), times.end());
    return times[times.size() / 2];
  }

  //! The frames from BeforeFrom on that were not on screen their swap interval after the frame before them
  int32_t OffTheSwapInterval(const std::vector<Sim::LoopFrame>& frames, const int64_t periodNanoseconds)
  {
    int32_t off = 0;
    for (std::size_t index = BeforeFrom; index < frames.size(); ++index)
    {
      const int64_t refreshes = ((frames[index].ShownNanoseconds - frames[index - 1].ShownNanoseconds) + (periodNanoseconds / 2)) / periodNanoseconds;
      off += refreshes != int64_t{frames[index].SwapInterval} ? 1 : 0;
    }
    return off;
  }
}

TEST(DisplaySlipLoop, WithLowLatencyTheLoopStartsItsFramesLaterByTheRefreshTheDisplayHeldAFrameFor)
{
  for (const PC::PacerCapability named : {PC::PacerCapability::NoCapabilities, PC::PacerCapability::VBlankTimes})
  {
    for (const uint32_t swapInterval : {1u, 2u})
    {
      const Sim::LoopSettings settings = SlipLoop(PC::PacerAim::LowLatency, swapInterval);
      const int64_t period = PeriodNanoseconds(settings);
      const std::vector<Sim::LoopFrame> frames = SimulateHeld(settings, named);
      ASSERT_EQ(frames.size(), 1'200u);
      // The display stayed smooth: the frame it held is the one off its swap interval
      EXPECT_EQ(OffTheSwapInterval(frames, period), 1) << swapInterval;
      // And the frames wait no longer than before it: the loop started them a refresh later, and the animation time is that
      // refresh further behind the clock
      const int64_t before = MedianToDisplay(frames, BeforeFrom, BeforeTo);
      const int64_t after = MedianToDisplay(frames, AfterFrom, frames.size());
      EXPECT_LT(std::abs(after - before), period / 2) << swapInterval << ' ' << before << ' ' << after;
      EXPECT_EQ(frames.back().RefreshesBehindClock, frames[BeforeTo].RefreshesBehindClock + 1u) << swapInterval;
    }
  }
}

TEST(DisplaySlipLoop, WithSmoothnessTheLoopTakesTheHit)
{
  for (const PC::PacerCapability named : {PC::PacerCapability::NoCapabilities, PC::PacerCapability::VBlankTimes})
  {
    for (const uint32_t swapInterval : {1u, 2u})
    {
      const Sim::LoopSettings settings = SlipLoop(PC::PacerAim::Smoothness, swapInterval);
      const int64_t period = PeriodNanoseconds(settings);
      const std::vector<Sim::LoopFrame> frames = SimulateHeld(settings, named);
      ASSERT_EQ(frames.size(), 1'200u);
      // The display stayed smooth, and every frame after the one it held waits a refresh longer: the loop goes on as planned
      EXPECT_EQ(OffTheSwapInterval(frames, period), 1) << swapInterval;
      const int64_t before = MedianToDisplay(frames, BeforeFrom, BeforeTo);
      const int64_t after = MedianToDisplay(frames, AfterFrom, frames.size());
      EXPECT_LT(std::abs(after - before - period), period / 2) << swapInterval << ' ' << before << ' ' << after;
      EXPECT_EQ(frames.back().RefreshesBehindClock, frames[BeforeTo].RefreshesBehindClock) << swapInterval;
    }
  }
}

TEST(DisplaySlipLoop, WithoutDisplayTimesTheDisplayIsNotFollowed)
{
  // With the duration alone the loop places the frames (tier 3), and nothing moves it: the frames wait a refresh longer
  Sim::LoopSettings settings = SlipLoop(PC::PacerAim::LowLatency, 1);
  settings.ReportsDisplayTimes = false;
  const int64_t period = PeriodNanoseconds(settings);
  const std::vector<Sim::LoopFrame> frames = SimulateHeld(settings, PC::PacerCapability::NoCapabilities);
  const int64_t before = MedianToDisplay(frames, BeforeFrom, BeforeTo);
  const int64_t after = MedianToDisplay(frames, AfterFrom, frames.size());
  EXPECT_GE(after - before, period / 2) << before << ' ' << after;
}
