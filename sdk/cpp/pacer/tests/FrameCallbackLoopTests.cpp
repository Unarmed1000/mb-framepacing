// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The frame callback as what holds the loop, on the simulation's display (SimulateTierLoop): tier 3.3c. The knobs
// here are what the window protocol allows, not a description of any system (sdk/doc/pacer-design.md has the one test compositor
// it has run on). A compositor is a display that takes a frame a while before its vertical blank and calls for a new one just
// after that.
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <gtest/gtest.h>
#include <algorithm>
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
  //! A compositor at 60 Hz that repaints this long before a vertical blank: a frame has to be there by then
  constexpr int64_t RepaintLead = 5'000'000;
  //! The frames left out at a run's start
  constexpr std::size_t Settled = 240;

  //! 60 Hz, a compositor that calls for a new frame just after it repainted, a present that waits for the call after the present
  //! before it, for two refreshes at the most, and work that takes a third of a refresh
  Sim::LoopSettings Loop(const PC::PacerAim aim)
  {
    Sim::LoopSettings settings;
    settings.RateNumerator = 60;
    settings.Frames = 1'200;
    settings.Aim = aim;
    settings.StartupPauseRefreshes = 0;
    settings.Display.LatchLeadNanoseconds = RepaintLead;
    settings.FrameCallbackReturn = {-RepaintLead + 100'000, -RepaintLead + 900'000};
    settings.PresentWaitsForFrameCallbackNanoseconds = 33'000'000;
    settings.CpuWork = {2'000'000, 2'000'000};
    settings.GpuWork = {3'000'000, 3'000'000};
    settings.TimerLate = {0, 300'000};
    return settings;
  }

  int64_t PeriodNanoseconds(const Sim::LoopSettings& settings)
  {
    return PC::RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator).ToNanosecondTimeSpan().Nanoseconds();
  }

  //! The frames in the stretch that were not shown their swap interval after the frame before them
  int32_t OffTheSwapInterval(const std::vector<Sim::LoopFrame>& frames, const std::size_t first, const std::size_t end, const int64_t period)
  {
    int32_t off = 0;
    for (std::size_t index = std::max<std::size_t>(first, 1); index < end; ++index)
    {
      const int64_t refreshes = ((frames[index].ShownNanoseconds - frames[index - 1].ShownNanoseconds) + (period / 2)) / period;
      off += refreshes != int64_t{frames[index].SwapInterval} ? 1 : 0;
    }
    return off;
  }

  int32_t OffTheSwapInterval(const std::vector<Sim::LoopFrame>& frames, const int64_t period)
  {
    return OffTheSwapInterval(frames, Settled, frames.size(), period);
  }

  int64_t Median(std::vector<int64_t> values)
  {
    std::sort(values.begin(), values.end());
    return values[values.size() / 2];
  }

  //! A frame's start to its display, the median of a settled run
  int64_t StartToShown(const std::vector<Sim::LoopFrame>& frames)
  {
    std::vector<int64_t> times;
    for (std::size_t index = Settled; index < frames.size(); ++index)
    {
      times.push_back(frames[index].ShownNanoseconds - frames[index].StartNanoseconds);
    }
    return Median(times);
  }

  //! How long the present held the loop, the median of a settled run
  int64_t PresentBlocked(const std::vector<Sim::LoopFrame>& frames)
  {
    std::vector<int64_t> times;
    for (std::size_t index = Settled; index < frames.size(); ++index)
    {
      times.push_back(frames[index].PresentReturnNanoseconds - frames[index].PresentNanoseconds);
    }
    return Median(times);
  }

  //! The shortest step between two frame starts of a settled run
  int64_t ShortestStartStep(const std::vector<Sim::LoopFrame>& frames)
  {
    int64_t shortest = INT64_MAX;
    for (std::size_t index = Settled; index < frames.size(); ++index)
    {
      shortest = std::min(shortest, frames[index].StartNanoseconds - frames[index - 1].StartNanoseconds);
    }
    return shortest;
  }

  //! A wait for a present made with the frame callback: what an application would get by giving the callback as that wait
  Sim::LoopSettings GivenAsAWaitForAPresent(Sim::LoopSettings settings)
  {
    settings.PresentWaitReturn = settings.FrameCallbackReturn;
    return settings;
  }
}

TEST(FrameCallbackLoop, TheLoopFindsTheCompositorWhereverInARefreshItStarted)
{
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    for (const int64_t phase : {0, 3'000'000, 6'000'000, 9'000'000, 11'000'000, 13'000'000, 15'000'000})
    {
      Sim::LoopSettings settings = Loop(aim);
      settings.FirstStartDelayNanoseconds = phase;
      const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, PacerCapability::FrameCallback);
      const int64_t period = PeriodNanoseconds(settings);
      EXPECT_EQ(OffTheSwapInterval(frames, period), 0) << phase;
      // From the call, a refresh to the next repaint, and the repaint's lead to the vertical blank that shows the frame
      EXPECT_NEAR(static_cast<double>(StartToShown(frames)), static_cast<double>(period + RepaintLead - 500'000), 1'000'000.0) << phase;
      // The frame is held before it starts, not in the present when it is done
      EXPECT_LT(PresentBlocked(frames), 100'000) << phase;
      EXPECT_EQ(frames.back().SwapInterval, 1u) << phase;
    }
  }
}

TEST(FrameCallbackLoop, TheCallbackOfThePresentBeforeTheLastWouldHoldNothing)
{
  // Given as a wait for a present the wait is as far back as one for a present that was shown, and a present that waits for
  // the same call has had that one by then: the wait never holds, and the loop stays held in the present with its frame done
  for (const int64_t phase : {0, 6'000'000, 11'000'000})
  {
    Sim::LoopSettings settings = Loop(PC::PacerAim::Smoothness);
    settings.FirstStartDelayNanoseconds = phase;
    const std::vector<Sim::LoopFrame> timer = Sim::SimulateTierLoop(settings, PacerCapability::NoCapabilities);
    const std::vector<Sim::LoopFrame> waited = Sim::SimulateTierLoop(GivenAsAWaitForAPresent(settings), PacerCapability::WaitForPresent);
    const std::vector<Sim::LoopFrame> callback = Sim::SimulateTierLoop(settings, PacerCapability::FrameCallback);
    EXPECT_NEAR(static_cast<double>(StartToShown(waited)), static_cast<double>(StartToShown(timer)), 200'000.0) << phase;
    EXPECT_GT(PresentBlocked(waited), 3'000'000) << phase;
    EXPECT_LT(StartToShown(callback) + 5'000'000, StartToShown(waited)) << phase;
  }
}

TEST(FrameCallbackLoop, ADisplayOffItsPeriodIsFollowed)
{
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    for (const int64_t ppm : {-2'000, 2'000})
    {
      Sim::LoopSettings settings = Loop(aim);
      settings.DisplayPeriodPpm = ppm;
      settings.Frames = 6'000;
      const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, PacerCapability::FrameCallback);
      EXPECT_EQ(OffTheSwapInterval(frames, PeriodNanoseconds(settings)), 0) << ppm;
      EXPECT_EQ(frames.back().RefreshesBehindClock, 0u) << ppm;
      EXPECT_LT(PresentBlocked(frames), 100'000) << ppm;
    }
  }
}

TEST(FrameCallbackLoop, CallbacksThatStopStopTheWaitsAndTheFramesGoOnAsBefore)
{
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    // No call for 15 s of a run of 40 s, a present that does not wait for one
    Sim::LoopSettings settings = Loop(aim);
    settings.Frames = 2'400;
    settings.PresentWaitsForFrameCallbackNanoseconds = 0;
    settings.FrameCallbackGaps = {{10'000'000'000, 15'000'000'000}};
    const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, PacerCapability::FrameCallback);
    ASSERT_EQ(frames.size(), 2'400u);
    const int64_t period = PeriodNanoseconds(settings);
    const int64_t begin = frames[0].StartNanoseconds + 10'000'000'000;
    const int64_t end = begin + 15'000'000'000;

    int32_t stoppedFrames = 0;
    int32_t framesInTheGap = 0;
    std::size_t firstAfter = 0;
    for (std::size_t index = 0; index < frames.size(); ++index)
    {
      const Sim::LoopFrame& frame = frames[index];
      framesInTheGap += frame.StartNanoseconds >= begin && frame.StartNanoseconds < end ? 1 : 0;
      stoppedFrames += frame.StartNanoseconds >= begin && frame.StartNanoseconds < end && frame.PresentWaitsStopped ? 1 : 0;
      firstAfter = firstAfter == 0 && frame.StartNanoseconds >= end ? index : firstAfter;
      // The rule sees no frame of it
      ASSERT_EQ(frame.SwapInterval, 1u) << index;
    }
    // The frame rate goes on on the timer, and the waits are stopped but for the first frames and the asking
    EXPECT_GE(framesInTheGap, 890);
    EXPECT_GE(stoppedFrames, framesInTheGap - 40);
    // A second after they came back the waits are made again, and every frame is on its refresh
    ASSERT_GT(firstAfter, 0u);
    const std::size_t settled = firstAfter + 60u;
    EXPECT_FALSE(frames[settled].PresentWaitsStopped);
    EXPECT_FALSE(frames.back().PresentWaitsStopped);
    EXPECT_EQ(OffTheSwapInterval(frames, settled, frames.size(), period), 0);
    EXPECT_LT(frames.back().ShownNanoseconds - frames.back().StartNanoseconds, 2 * period);
  }
}

TEST(FrameCallbackLoop, APresentThatWaitsForACallbackThatDoesNotComeIsNoMoreThanItsOwnTime)
{
  // A present that gives up after two refreshes: the loop goes on at that rate, and is back when the calls are
  Sim::LoopSettings settings = Loop(PC::PacerAim::LowLatency);
  settings.Frames = 1'500;
  settings.FrameCallbackGaps = {{10'000'000'000, 5'000'000'000}};
  const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, PacerCapability::FrameCallback);
  const int64_t period = PeriodNanoseconds(settings);
  EXPECT_EQ(frames.back().SwapInterval, 1u);
  EXPECT_FALSE(frames.back().PresentWaitsStopped);
  EXPECT_EQ(OffTheSwapInterval(frames, frames.size() - 120u, frames.size(), period), 0);
}

TEST(FrameCallbackLoop, APresentThatWaitsForCallsThatStayAwayHoldsTheLoopUntilTheyAreBack)
{
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    for (const int64_t milliseconds : {500, 1'000, 3'000, 4'000})
    {
      // As on the first test compositor: a FIFO present does not return while the calls are held back. One wait runs out, and
      // the present after it stands, so the two waits that stop the waits never happen and no plan is made until it returns
      Sim::LoopSettings settings = Loop(aim);
      settings.Frames = 1'500;
      settings.PresentWaitsForFrameCallbackNanoseconds = 10'000'000'000;
      const int64_t hold = milliseconds * 1'000'000;
      settings.FrameCallbackGaps = {{10'000'000'000, hold}};
      const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, PacerCapability::FrameCallback);
      const int64_t period = PeriodNanoseconds(settings);

      int64_t longest = 0;
      std::size_t longestAt = 0;
      for (std::size_t index = 0; index < frames.size(); ++index)
      {
        const int64_t blocked = frames[index].PresentReturnNanoseconds - frames[index].PresentNanoseconds;
        longestAt = blocked > longest ? index : longestAt;
        longest = std::max(longest, blocked);
        ASSERT_FALSE(frames[index].PresentWaitsStopped) << index;
      }
      EXPECT_GT(longest, hold - (10 * period)) << milliseconds;
      EXPECT_LT(longest, hold) << milliseconds;
      // The loop goes on by itself, on its refreshes and at the swap interval it had
      EXPECT_EQ(frames.back().SwapInterval, 1u) << milliseconds;
      EXPECT_EQ(OffTheSwapInterval(frames, longestAt + 120u, frames.size(), period), 0) << milliseconds;
      EXPECT_LT(frames.back().ShownNanoseconds - frames.back().StartNanoseconds, 2 * period) << milliseconds;
    }
  }
}

TEST(FrameCallbackLoop, CallsThatComeTogetherDoNotRunTheLoopAhead)
{
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    // Up to two refreshes late, in the order of the presents: some come together. A present that does not wait for them
    Sim::LoopSettings settings = Loop(aim);
    settings.PresentWaitsForFrameCallbackNanoseconds = 0;
    const int64_t period = PeriodNanoseconds(settings);
    settings.FrameCallbackReturn = {-RepaintLead, 2 * period};
    const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, PacerCapability::FrameCallback);
    // A wait that returned at once says nothing, and the grid still holds the frame's start: never a burst of frames
    EXPECT_GE(ShortestStartStep(frames), period / 2);
    int32_t mostPending = 0;
    for (std::size_t index = Settled; index < frames.size(); ++index)
    {
      mostPending = std::max(mostPending, frames[index].PendingAtStart);
    }
    EXPECT_LE(mostPending, 3);
  }
}

TEST(FrameCallbackLoop, WorkThatDoesNotFitARefreshIsTwoRefreshesPerFrame)
{
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    Sim::LoopSettings settings = Loop(aim);
    const int64_t period = PeriodNanoseconds(settings);
    settings.GpuWork = {period, period};
    settings.ReportsGpuWork = true;
    const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, PacerCapability::FrameCallback);
    EXPECT_EQ(frames.back().SwapInterval, 2u);
    EXPECT_EQ(OffTheSwapInterval(frames, period), 0);
  }
}

TEST(FrameCallbackLoop, WithTwoFramesInFlightWorkOfMoreThanARefreshInAllStaysAtOneRefreshPerFrame)
{
  // The CPU and the GPU each work 72 % of a refresh, side by side: the wait is one present further back, so that the loop does
  // not make the two come one after the other. The aim of low latency has them so all the same
  Sim::LoopSettings settings = Loop(PC::PacerAim::Smoothness);
  const int64_t period = PeriodNanoseconds(settings);
  settings.CpuWork = {(period * 72) / 100, (period * 72) / 100};
  settings.GpuWork = settings.CpuWork;
  settings.ReportsGpuWork = true;
  settings.MaxFramesInFlight = 2;
  settings.WaitsForPreviousGpuWork = false;
  const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, PacerCapability::FrameCallback);
  EXPECT_EQ(frames.back().SwapInterval, 1u);
  EXPECT_EQ(OffTheSwapInterval(frames, period), 0);
}

TEST(FrameCallbackLoop, ATargetOfTwoRefreshesIsKept)
{
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    Sim::LoopSettings settings = Loop(aim);
    settings.PreferredSwapInterval = 2;
    const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, PacerCapability::FrameCallback);
    EXPECT_EQ(frames.back().SwapInterval, 2u);
    EXPECT_EQ(OffTheSwapInterval(frames, PeriodNanoseconds(settings)), 0);
  }
}

TEST(FrameCallbackLoop, AFrameThatRunsLongCostsItsOwnRefreshes)
{
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    Sim::LoopSettings settings = Loop(aim);
    const int64_t period = PeriodNanoseconds(settings);
    settings.LongFrames = {600};
    settings.LongFrameCpuNanoseconds = (period * 240) / 100;
    const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, PacerCapability::FrameCallback);
    EXPECT_EQ(frames.back().SwapInterval, 1u);
    EXPECT_EQ(OffTheSwapInterval(frames, 0, 600, period) - OffTheSwapInterval(frames, 0, Settled, period), 0);
    EXPECT_EQ(OffTheSwapInterval(frames, 610, frames.size(), period), 0);
  }
}

TEST(FrameCallbackLoop, TheCallbackCanComeAndGoWhileTheFramesGoOn)
{
  // To a wait for a present and vertical blank times and back, by the active set. The frames go on: no pause of more than a few
  // refreshes, and each stretch ends on its refreshes
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    Sim::LoopSettings settings = Loop(aim);
    settings.PresentWaitsForFrameCallbackNanoseconds = 0;
    settings.ActiveSetChanges = {{400, PacerCapability::VBlankTimes | PacerCapability::WaitForPresent},
                                 {800, PacerCapability::FrameCallback},
                                 {1'000, PacerCapability::NoCapabilities},
                                 {1'100, PacerCapability::FrameCallback}};
    const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, PacerCapability::FrameCallback);
    const int64_t period = PeriodNanoseconds(settings);
    for (std::size_t index = 1; index < frames.size(); ++index)
    {
      ASSERT_LT(frames[index].StartNanoseconds - frames[index - 1].StartNanoseconds, 5 * period) << index;
    }
    EXPECT_EQ(OffTheSwapInterval(frames, 1'120, frames.size(), period), 0);
    EXPECT_EQ(frames[399].ActiveCapabilities, static_cast<uint32_t>(PacerCapability::FrameCallback));
    EXPECT_EQ(frames[1'199].ActiveCapabilities, static_cast<uint32_t>(PacerCapability::FrameCallback));
  }
}

TEST(FrameCallbackLoop, WithSomethingBetterTheCallbackMovesNoFrame)
{
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    for (const PacerCapability better : {PacerCapability::WaitForPresent, PacerCapability::VBlankTimes})
    {
      Sim::LoopSettings settings = Loop(aim);
      settings.PresentWaitsForFrameCallbackNanoseconds = 0;
      settings.TimerLate = {0, 0};
      settings.PresentWaitReturn = {100'000, 100'000};
      const std::vector<Sim::LoopFrame> expected = Sim::SimulateTierLoop(settings, better);
      const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, better | PacerCapability::FrameCallback);
      ASSERT_EQ(frames.size(), expected.size());
      for (std::size_t index = 0; index < frames.size(); ++index)
      {
        ASSERT_EQ(frames[index].StartNanoseconds, expected[index].StartNanoseconds) << index;
        ASSERT_EQ(frames[index].ShownNanoseconds, expected[index].ShownNanoseconds) << index;
      }
    }
  }
}

TEST(FrameCallbackLoop, ATimeOnThePresentGoesWithIt)
{
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    Sim::LoopSettings settings = Loop(aim);
    settings.PresentsAtTime = true;
    const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, PacerCapability::FrameCallback);
    EXPECT_EQ(OffTheSwapInterval(frames, PeriodNanoseconds(settings)), 0);
    EXPECT_EQ(frames.back().SwapInterval, 1u);
  }
}
