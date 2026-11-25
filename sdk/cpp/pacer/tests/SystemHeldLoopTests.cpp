// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The system's hold (the third major tier) in a frame loop on the simulation's display: a swap chain whose acquire holds the loop
// while it is full, a window that is covered for a while, and an acquire that holds a share of a refresh.
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <mb/framepacing/pacer/tier/SystemHoldState.hpp>
#include <gtest/gtest.h>
#include <cstddef>
#include <cstdint>
#include <vector>
#include "FrameLoopSimulation.hpp"
#include "LoopFrame.hpp"
#include "LoopSettings.hpp"

namespace PC = MB::FramePacing::Pacer;
namespace Sim = MB::FramePacing::Pacer::Simulation;

namespace
{
  constexpr int64_t Second = 1'000'000'000;

  //! 240 Hz, light GPU work, three images, the aim of smoothness, the loop's waits reported
  Sim::LoopSettings HeldLoop()
  {
    Sim::LoopSettings settings;
    settings.Frames = 2'400;
    settings.Aim = PC::PacerAim::Smoothness;
    settings.AutoSwapInterval = false;
    settings.ReportsSystemWaits = true;
    settings.Display.Images = 3;
    const int64_t period = PC::RefreshPeriod::FromRate(settings.RateNumerator).ToNanosecondTimeSpan().Nanoseconds();
    settings.GpuWork = {period / 5, period / 5};
    return settings;
  }

  int64_t PeriodOf(const Sim::LoopSettings& settings)
  {
    return PC::RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator).ToNanosecondTimeSpan().Nanoseconds();
  }

  uint8_t Hold(const PC::SystemHoldState state)
  {
    return static_cast<uint8_t>(state);
  }
}

TEST(SystemHeldLoop, ACoveredWindowIsLimitedToTheDisplaysRateAndHeldAgainWhenItIsBack)
{
  Sim::LoopSettings settings = HeldLoop();
  const int64_t firstBlank = settings.Display.FirstBlankNanoseconds;
  settings.Display.CoveredFromNanoseconds = firstBlank + (3 * Second);
  settings.Display.CoveredUntilNanoseconds = firstBlank + (6 * Second);
  const int64_t period = PeriodOf(settings);
  const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, PC::PacerCapability::AcquireWaits);

  int32_t backToBack = 0;
  int32_t coveredFrames = 0;
  int32_t heldAfter = 0;
  for (std::size_t index = 1; index < frames.size(); ++index)
  {
    const Sim::LoopFrame& frame = frames[index];
    const int64_t apart = frame.StartNanoseconds - frames[index - 1].StartNanoseconds;
    if (frame.StartNanoseconds < settings.Display.CoveredFromNanoseconds)
    {
      // Held: the swap chain is full, nothing to limit
      ASSERT_EQ(frame.SystemHold, Hold(PC::SystemHoldState::Holding)) << index;
    }
    else if (frame.StartNanoseconds < settings.Display.CoveredUntilNanoseconds)
    {
      // Covered: a few frames back to back, until the rate gives it away; then no frame sooner than the limit lets it, an
      // eighth of a refresh faster than the display
      ++coveredFrames;
      if (frames[index - 1].SystemHold == Hold(PC::SystemHoldState::Holding))
      {
        backToBack += apart < period / 2 ? 1 : 0;
      }
      else
      {
        ASSERT_GE(apart, period - (period / 8) - 1) << index;
        ASSERT_EQ(frame.SystemHold, Hold(PC::SystemHoldState::NotHolding)) << index;
      }
    }
    else if (frame.StartNanoseconds > settings.Display.CoveredUntilNanoseconds + (Second / 2))
    {
      // Back: the system holds the loop again, and the swap chain is full
      heldAfter += frame.SystemHold == Hold(PC::SystemHoldState::Holding) ? 1 : 0;
      // A refresh to the nanosecond: 240 Hz is 4,166,666.67 ns
      ASSERT_NEAR(static_cast<double>(frame.AnimationStepNanoseconds), static_cast<double>(period), 1.0) << index;
    }
  }
  // The queue holds four frames: no more start back to back before the pacer sees it
  EXPECT_LE(backToBack, 4);
  // About 3 s of frames at an eighth more than 240 Hz, not the thousands an unlimited loop would start
  EXPECT_GT(coveredFrames, 790);
  EXPECT_LT(coveredFrames, 850);
  EXPECT_GT(heldAfter, 100);
  // Seen to hold again within half a second of the window being back, and from then on
  std::size_t firstHeld = frames.size();
  for (std::size_t index = frames.size(); index > 0 && frames[index - 1].SystemHold == Hold(PC::SystemHoldState::Holding); --index)
  {
    firstHeld = index - 1;
  }
  ASSERT_LT(firstHeld, frames.size());
  EXPECT_LT(frames[firstHeld].StartNanoseconds - settings.Display.CoveredUntilNanoseconds, Second / 2);
  EXPECT_EQ(frames.back().SystemHold, Hold(PC::SystemHoldState::Holding));
  EXPECT_EQ(frames.back().PendingAtStart, settings.Display.Images - 2);
}

TEST(SystemHeldLoop, AnAcquireThatHoldsAShareOfARefreshIsLimited)
{
  // The acquire lets the loop through after a third of a refresh at the most: the frames would run at more than twice the rate
  Sim::LoopSettings settings = HeldLoop();
  settings.Frames = 600;
  const int64_t period = PeriodOf(settings);
  settings.Display.AcquireWaitsAtMostNanoseconds = period / 3;
  const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, PC::PacerCapability::AcquireWaits);
  // Limited from the first frames on, and never taken for a system that holds: its hold ends a third of a refresh after the
  // limit at the most
  const int64_t probe = period - (period / 8);
  for (std::size_t index = 30; index < frames.size(); ++index)
  {
    ASSERT_EQ(frames[index].SystemHold, Hold(PC::SystemHoldState::NotHolding)) << index;
    ASSERT_GE(frames[index].StartNanoseconds - frames[index - 1].StartNanoseconds, probe - (period / 3) - 1) << index;
  }
  // The limit's rate on average
  const int64_t span = frames.back().StartNanoseconds - frames[30].StartNanoseconds;
  EXPECT_NEAR(static_cast<double>(span) / static_cast<double>(probe * static_cast<int64_t>(frames.size() - 31)), 1.0, 0.02);
}

TEST(SystemHeldLoop, ADisplayALittleOffWhatTheLoopWasToldStaysHeld)
{
  for (const int64_t ppm : {int64_t{-2'000}, int64_t{2'000}})
  {
    Sim::LoopSettings settings = HeldLoop();
    settings.DisplayPeriodPpm = ppm;
    const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, PC::PacerCapability::AcquireWaits);
    for (std::size_t index = 0; index < frames.size(); ++index)
    {
      ASSERT_EQ(frames[index].SystemHold, Hold(PC::SystemHoldState::Holding)) << ppm << ' ' << index;
    }
  }
}
