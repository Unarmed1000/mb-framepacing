// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// A frame loop on a display that queues its presents (FrameLoopSimulation): the first integration's loop as it was on 2026-10-06
// around the pacer as it is. These tests pin the state the pacer's redesign starts from, the fault included: a vertical blank at
// which the display takes no frame costs every later frame a refresh of latency, and neither the pacer nor the loop takes it back.
// The first integration's logs of 2026-10-06 show that on a real swap chain in a window (frame start to display climbing from 2
// to 5 refreshes over a run); what makes a display hold a frame there is not understood, so the model is told at which blanks.
//
// When the pacer handles a missed refresh, the tests named "Today..." are the ones that change.
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <gtest/gtest.h>
#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>
#include "FrameLoopSimulation.hpp"
#include "LoopFrame.hpp"
#include "LoopProfile.hpp"
#include "LoopSettings.hpp"

namespace PC = MB::FramePacing::Pacer;
namespace Sim = MB::FramePacing::Pacer::Simulation;

namespace
{
  //! 240 Hz, GPU work of 90 % of a refresh, a fixed swap interval of 1: the case of the logs
  Sim::LoopSettings Loop(const Sim::LoopProfile profile)
  {
    Sim::LoopSettings settings;
    settings.Profile = profile;
    settings.AutoSwapInterval = false;
    return settings;
  }

  int64_t PeriodTicks(const Sim::LoopSettings& settings)
  {
    return PC::RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator).ToTimeSpan().Ticks();
  }

  //! From a frame's start to its display, in half refreshes (the early profile's frames start in the middle of a refresh)
  int64_t HalfRefreshesToDisplay(const Sim::LoopFrame& frame, const int64_t periodTicks)
  {
    return (((frame.ShownTicks - frame.StartTicks) * 2) + (periodTicks / 2)) / periodTicks;
  }
}

TEST(FrameLoop, EveryFrameIsShownAfterItsWorkAndInTheOrderOfThePresents)
{
  for (const Sim::LoopProfile profile : {Sim::LoopProfile::RenderLate, Sim::LoopProfile::RenderEarly})
  {
    Sim::LoopSettings settings = Loop(profile);
    settings.Frames = 300;
    settings.Display.HeldBlanks = {100, 200};
    const std::vector<Sim::LoopFrame> frames = Sim::SimulateLoop(settings);

    ASSERT_EQ(frames.size(), 300u);
    for (std::size_t index = 0; index < frames.size(); ++index)
    {
      EXPECT_EQ(frames[index].FrameId, index + 1);
      EXPECT_GE(frames[index].PresentTicks, frames[index].WorkEndTicks) << index;
      EXPECT_GE(frames[index].ShownTicks, frames[index].PresentTicks) << index;
      EXPECT_GE(frames[index].ShownTicks, frames[index].GpuEndTicks) << index;
      if (index > 0)
      {
        EXPECT_GT(frames[index].ShownTicks, frames[index - 1].ShownTicks) << index;
        EXPECT_GT(frames[index].StartTicks, frames[index - 1].StartTicks) << index;
        // The GPU works on one frame at a time
        EXPECT_GE(frames[index].GpuBeginTicks, frames[index - 1].GpuEndTicks) << index;
      }
    }
  }
}

TEST(FrameLoop, ALoopInStepWithTheDisplayKeepsItsLatency)
{
  const Sim::LoopSettings late = Loop(Sim::LoopProfile::RenderLate);
  const int64_t period = PeriodTicks(late);
  for (const Sim::LoopFrame& frame : Sim::SimulateLoop(late))
  {
    // Started at a vertical blank, shown at the next
    EXPECT_EQ(HalfRefreshesToDisplay(frame, period), 2) << frame.FrameId;
    EXPECT_EQ(frame.PendingAtStart, 0) << frame.FrameId;
  }

  const std::vector<Sim::LoopFrame> early = Sim::SimulateLoop(Loop(Sim::LoopProfile::RenderEarly));
  for (std::size_t index = 10; index < early.size(); ++index)
  {
    // Started right after the present before it, in the middle of a refresh, so the frame before is still on its way
    EXPECT_EQ(HalfRefreshesToDisplay(early[index], period), 3) << index;
    EXPECT_EQ(early[index].PendingAtStart, 1) << index;
  }
}

TEST(FrameLoop, TodayAVerticalBlankWithoutAFrameTakenCostsARefreshOfLatencyThatNeverComesBack)
{
  for (const Sim::LoopProfile profile : {Sim::LoopProfile::RenderLate, Sim::LoopProfile::RenderEarly})
  {
    Sim::LoopSettings settings = Loop(profile);
    settings.Frames = 800;
    settings.Display.HeldBlanks = {150, 300, 450};
    const int64_t period = PeriodTicks(settings);
    const std::vector<Sim::LoopFrame> frames = Sim::SimulateLoop(settings);
    const int64_t base = HalfRefreshesToDisplay(frames[100], period);
    const int32_t basePending = frames[100].PendingAtStart;

    // One refresh more after each held blank
    EXPECT_EQ(HalfRefreshesToDisplay(frames[250], period), base + 2);
    EXPECT_EQ(HalfRefreshesToDisplay(frames[400], period), base + 4);
    EXPECT_EQ(frames[250].PendingAtStart, basePending + 1);
    EXPECT_EQ(frames[400].PendingAtStart, basePending + 2);
    // And three refreshes more for every frame of the rest of the run: the loop goes on at one frame per refresh
    for (std::size_t index = 460; index < frames.size(); ++index)
    {
      EXPECT_EQ(HalfRefreshesToDisplay(frames[index], period), base + 6) << index;
      EXPECT_EQ(frames[index].PendingAtStart, basePending + 3) << index;
      // A refresh, to the tick the period is rounded to
      EXPECT_NEAR(static_cast<double>(frames[index].StartTicks - frames[index - 1].StartTicks), static_cast<double>(period), 1.0) << index;
    }
  }
}

TEST(FrameLoop, TodayThePacerSeesNothingOfIt)
{
  Sim::LoopSettings settings = Loop(Sim::LoopProfile::RenderLate);
  settings.AutoSwapInterval = true;
  settings.Frames = 800;
  settings.Display.HeldBlanks = {150, 300, 450};
  const std::vector<Sim::LoopFrame> held = Sim::SimulateLoop(settings);
  settings.Display.HeldBlanks = {};
  const std::vector<Sim::LoopFrame> inStep = Sim::SimulateLoop(settings);

  // The pacer is given frame starts and work: they are the same in both runs, so its answers are
  ASSERT_EQ(held.size(), inStep.size());
  for (std::size_t index = 0; index < held.size(); ++index)
  {
    EXPECT_EQ(held[index].StartTicks, inStep[index].StartTicks) << index;
    EXPECT_EQ(held[index].SwapInterval, inStep[index].SwapInterval) << index;
    EXPECT_EQ(held[index].AnimationTicks, inStep[index].AnimationTicks) << index;
    EXPECT_EQ(held[index].IntendedDisplayTicks, inStep[index].IntendedDisplayTicks) << index;
    EXPECT_EQ(held[index].WindowLateFrames, 0u) << index;
  }
  // While the display shows the last frames three refreshes later
  EXPECT_EQ(held.back().ShownTicks - inStep.back().ShownTicks, PC::RefreshPeriod::FromRate(settings.RateNumerator).TimeFor(3).Ticks());
}

TEST(FrameLoop, WithImagesThatBoundTheWaitingFramesTheLatencyStopsAtTheBound)
{
  Sim::LoopSettings settings = Loop(Sim::LoopProfile::RenderLate);
  settings.Frames = 800;
  settings.Display.Images = 3;
  settings.Display.HeldBlanks = {150, 300, 450};
  const int64_t period = PeriodTicks(settings);
  const std::vector<Sim::LoopFrame> frames = Sim::SimulateLoop(settings);

  EXPECT_EQ(HalfRefreshesToDisplay(frames[100], period), 2);
  EXPECT_EQ(HalfRefreshesToDisplay(frames[250], period), 4);
  // The acquire holds the loop for a refresh at the second and the third held blank: one frame waits, never two
  for (std::size_t index = 310; index < frames.size(); ++index)
  {
    EXPECT_LE(frames[index].PendingAtStart, 1) << index;
  }
  EXPECT_EQ(HalfRefreshesToDisplay(frames.back(), period), 4);
}

TEST(FrameLoop, ALoopWithATimerOnlyBehavesTheSame)
{
  Sim::LoopSettings settings = Loop(Sim::LoopProfile::RenderLate);
  settings.Frames = 600;
  settings.HasVBlankTimes = false;
  settings.TimerLate = {0, 3'000};
  settings.Display.HeldBlanks = {150, 300};
  const int64_t period = PeriodTicks(settings);
  const std::vector<Sim::LoopFrame> frames = Sim::SimulateLoop(settings);

  const int64_t base = HalfRefreshesToDisplay(frames[100], period);
  for (std::size_t index = 320; index < frames.size(); ++index)
  {
    EXPECT_EQ(HalfRefreshesToDisplay(frames[index], period), base + 4) << index;
  }
}

TEST(FrameLoop, ARunIsTheSameEveryTimeAndItsFrameLogHasARowPerFrame)
{
  Sim::LoopSettings settings = Loop(Sim::LoopProfile::RenderEarly);
  settings.Frames = 50;
  settings.TimerLate = {0, 2'000};
  const std::string first = Sim::ToFrameLog(Sim::SimulateLoop(settings), settings);
  const std::string second = Sim::ToFrameLog(Sim::SimulateLoop(settings), settings);

  EXPECT_EQ(first, second);
  EXPECT_TRUE(first.starts_with("frameIndex,pacerFrameId,pacerOn,swapInterval,holdMethod,displayRefreshPeriodTicks,"));
  EXPECT_EQ(std::count(first.begin(), first.end(), '\n'), 51);
  // The first frame waits for nothing before its start: its two cells are empty
  const std::size_t row = first.find('\n') + 1;
  EXPECT_TRUE(first.substr(row).starts_with("0,1,1,1,1,41667,41667,"));
  EXPECT_NE(first.substr(row, first.find('\n', row) - row).find(",,,"), std::string::npos);
}

// The pacer of the lowest pair of tiers (TimerPeriodOnlyPacer) in place of today's pacer and the loop's own calculations: the
// application only carries out what it is given.

TEST(FrameLoop, TheLowestPairsPacerPacesAsTodaysLoopOnATimerWhileNothingGoesWrong)
{
  Sim::LoopSettings settings = Loop(Sim::LoopProfile::RenderLate);
  settings.HasVBlankTimes = false;
  settings.Frames = 600;
  const std::vector<Sim::LoopFrame> today = Sim::SimulateLoop(settings);
  const std::vector<Sim::LoopFrame> paced = Sim::SimulateTimerPeriodOnlyLoop(settings);

  ASSERT_EQ(today.size(), paced.size());
  for (std::size_t index = 0; index < today.size(); ++index)
  {
    // The frame starts drift apart by a third of a tick a frame: today's loop adds the period's rounded ticks up, the grid
    // counts in the period itself
    EXPECT_NEAR(static_cast<double>(paced[index].StartTicks), static_cast<double>(today[index].StartTicks), 1.0 + (static_cast<double>(index) / 3.0))
      << index;
    EXPECT_EQ(paced[index].ShownTicks, today[index].ShownTicks) << index;
    EXPECT_EQ(paced[index].PendingAtStart, 0) << index;
    EXPECT_EQ(paced[index].SwapInterval, 1u) << index;
  }
}

TEST(FrameLoop, TodayAFrameThatRanLongLeavesAFrameWaitingForGoodOnATimer)
{
  Sim::LoopSettings settings = Loop(Sim::LoopProfile::RenderLate);
  settings.HasVBlankTimes = false;
  settings.Frames = 400;
  // Frame 100 works 1.6 refreshes longer on the CPU
  settings.LongFrames = {100};
  settings.LongFrameCpuTicks = (PeriodTicks(settings) * 16) / 10;
  const int64_t period = PeriodTicks(settings);
  const std::vector<Sim::LoopFrame> frames = Sim::SimulateLoop(settings);

  EXPECT_EQ(frames[99].PendingAtStart, 0);
  EXPECT_EQ(HalfRefreshesToDisplay(frames[99], period), 2);
  // The loop starts its count again from the late start, half a refresh off where it was: from then on every frame starts
  // while the frame before it still waits, and is shown half a refresh later after its start
  for (std::size_t index = 102; index < frames.size(); ++index)
  {
    EXPECT_EQ(frames[index].PendingAtStart, 1) << index;
    EXPECT_EQ(HalfRefreshesToDisplay(frames[index], period), 3) << index;
  }
}

TEST(FrameLoop, WithTheGridOnTheClockAFrameThatRanLongCostsWholeRefreshesAndNothingAfterIt)
{
  Sim::LoopSettings settings = Loop(Sim::LoopProfile::RenderLate);
  settings.Frames = 400;
  settings.LongFrames = {100};
  settings.LongFrameCpuTicks = (PeriodTicks(settings) * 16) / 10;
  const int64_t period = PeriodTicks(settings);
  const std::vector<Sim::LoopFrame> frames = Sim::SimulateTimerPeriodOnlyLoop(settings);

  EXPECT_EQ(frames[99].PendingAtStart, 0);
  EXPECT_EQ(HalfRefreshesToDisplay(frames[99], period), 2);
  // Two frames after the long one the loop is where it was against the display: no frame waits, and a frame is shown a
  // refresh after its start, for the rest of the run
  for (std::size_t index = 103; index < frames.size(); ++index)
  {
    EXPECT_EQ(frames[index].PendingAtStart, 0) << index;
    EXPECT_EQ(HalfRefreshesToDisplay(frames[index], period), 2) << index;
    EXPECT_NEAR(static_cast<double>(frames[index].StartTicks - frames[index - 1].StartTicks), static_cast<double>(period), 1.0) << index;
  }
  // The animation time went on by a refresh per frame throughout: the refreshes the long frame took are not stepped over
  for (std::size_t index = 1; index < frames.size(); ++index)
  {
    EXPECT_NEAR(static_cast<double>(frames[index].AnimationStepTicks), static_cast<double>(period), 1.0) << index;
  }
}

TEST(FrameLoop, TheLowestPairsPacerDoesNotSeeARefreshTheDisplayLostByItself)
{
  // What this tier can not do, pinned: the frames are ready in time, the display takes none at three vertical blanks, and each
  // costs a refresh of latency for the rest of the run, as with today's pacer
  Sim::LoopSettings settings = Loop(Sim::LoopProfile::RenderLate);
  settings.Frames = 800;
  settings.Display.HeldBlanks = {150, 300, 450};
  const int64_t period = PeriodTicks(settings);
  const std::vector<Sim::LoopFrame> frames = Sim::SimulateTimerPeriodOnlyLoop(settings);

  EXPECT_EQ(HalfRefreshesToDisplay(frames[100], period), 2);
  for (std::size_t index = 460; index < frames.size(); ++index)
  {
    EXPECT_EQ(HalfRefreshesToDisplay(frames[index], period), 8) << index;
    EXPECT_EQ(frames[index].PendingAtStart, 3) << index;
  }
}

TEST(FrameLoop, TheLowestPairsPacerHoldsAFrameOfTwoRefreshesAndAQueueThenEmptiesByItself)
{
  Sim::LoopSettings settings;
  settings.AutoSwapInterval = true;
  settings.Frames = 1'200;
  // CPU work of 1.3 refreshes: the rule goes to two refreshes per frame
  settings.CpuWork = {(PeriodTicks(settings) * 13) / 10, (PeriodTicks(settings) * 13) / 10};
  settings.GpuWork = {PeriodTicks(settings) / 5, PeriodTicks(settings) / 5};
  settings.Display.HeldBlanks = {1'500};
  const int64_t period = PeriodTicks(settings);
  const std::vector<Sim::LoopFrame> frames = Sim::SimulateTimerPeriodOnlyLoop(settings);

  EXPECT_EQ(frames.back().SwapInterval, 2u);
  // The frames at two refreshes per frame start two refreshes apart, wait before their present, and are shown two apart
  std::size_t checked = 0;
  for (std::size_t index = frames.size() - 100; index < frames.size(); ++index)
  {
    ASSERT_EQ(frames[index].SwapInterval, 2u) << index;
    EXPECT_NEAR(static_cast<double>(frames[index].StartTicks - frames[index - 1].StartTicks), static_cast<double>(2 * period), 1.0) << index;
    EXPECT_NEAR(static_cast<double>(frames[index].ShownTicks - frames[index - 1].ShownTicks), static_cast<double>(2 * period), 1.0) << index;
    // The blank the display lost is long gone: no frame waits
    EXPECT_EQ(frames[index].PendingAtStart, 0) << index;
    ++checked;
  }
  EXPECT_EQ(checked, 100u);
}

// The pacer of a timer with a wait for a present (TimerWaitForPresentPacer): the display itself keeps the frames that wait few.
// The model's wait returns 0.06 to 2.4 ms after the display took the frame, as the first integration measured it on one system.

namespace
{
  //! The frame starts of the run's second half, in refreshes per frame (times 100)
  int64_t RefreshesPerFrameTimes100(const std::vector<Sim::LoopFrame>& frames, const int64_t periodTicks)
  {
    const std::size_t first = frames.size() / 2;
    const int64_t span = frames.back().StartTicks - frames[first].StartTicks;
    return (span * 100) / (periodTicks * static_cast<int64_t>(frames.size() - 1 - first));
  }
}

TEST(FrameLoop, WithAWaitForTheLastPresentNoFrameWaitsAndALostRefreshCostsOneFrameStart)
{
  Sim::LoopSettings settings = Loop(Sim::LoopProfile::RenderLate);
  settings.Frames = 800;
  settings.WaitingPresents = 1;
  // Light work: a fifth of a refresh on the GPU
  settings.GpuWork = {PeriodTicks(settings) / 5, PeriodTicks(settings) / 5};
  settings.Display.HeldBlanks = {150, 300, 450};
  const int64_t period = PeriodTicks(settings);
  const std::vector<Sim::LoopFrame> frames = Sim::SimulateTimerWaitForPresentLoop(settings);

  // From the second frame on no earlier frame waits when a frame starts, held blanks or not, and a frame is on screen within
  // a refresh and a half of its start (the lowest pair's pacer ends this run four refreshes behind).
  // The frame the display holds a blank against is itself on screen a refresh later: that one, and no frame after it
  int32_t heldFrames = 0;
  for (std::size_t index = 1; index < frames.size(); ++index)
  {
    EXPECT_EQ(frames[index].PendingAtStart, 0) << index;
    EXPECT_LE(HalfRefreshesToDisplay(frames[index], period), 4) << index;
    heldFrames += HalfRefreshesToDisplay(frames[index], period) > 3 ? 1 : 0;
  }
  EXPECT_LE(heldFrames, 3);
  // The three blanks cost three frame starts and nothing else: one refresh per frame otherwise
  EXPECT_NEAR(static_cast<double>(RefreshesPerFrameTimes100(frames, period)), 100.0, 2.0);
  const std::vector<Sim::LoopFrame> blind = Sim::SimulateTimerPeriodOnlyLoop(settings);
  EXPECT_EQ(HalfRefreshesToDisplay(blind.back(), period), 8);
}

TEST(FrameLoop, WithOnePresentAllowedToWaitOneWaitsAndNoMore)
{
  Sim::LoopSettings settings = Loop(Sim::LoopProfile::RenderLate);
  settings.Frames = 800;
  settings.WaitingPresents = 2;
  settings.GpuWork = {PeriodTicks(settings) / 5, PeriodTicks(settings) / 5};
  settings.Display.HeldBlanks = {150, 300, 450};
  const int64_t period = PeriodTicks(settings);
  const std::vector<Sim::LoopFrame> frames = Sim::SimulateTimerWaitForPresentLoop(settings);

  for (std::size_t index = 0; index < frames.size(); ++index)
  {
    EXPECT_LE(frames[index].PendingAtStart, 1) << index;
    EXPECT_LE(HalfRefreshesToDisplay(frames[index], period), 6) << index;
  }
  // After the first held blank the one that may wait does, for the rest of the run: a refresh more of latency, and no more
  for (std::size_t index = 200; index < frames.size(); ++index)
  {
    EXPECT_EQ(frames[index].PendingAtStart, 1) << index;
  }
  EXPECT_NEAR(static_cast<double>(RefreshesPerFrameTimes100(frames, period)), 100.0, 2.0);
}

TEST(FrameLoop, WorkThatDoesNotFitBesideTheWaitHalvesTheFrameRateWithNoPresentWaitingAndKeepsItWithOne)
{
  // GPU work of 90 % of a refresh. The first integration measured this pair on a real swap chain: a frame every 1.94 refreshes
  // when waiting for the last present, every 0.96 when waiting for the one before it
  Sim::LoopSettings settings = Loop(Sim::LoopProfile::RenderLate);
  settings.Frames = 800;
  const int64_t period = PeriodTicks(settings);

  settings.WaitingPresents = 1;
  const std::vector<Sim::LoopFrame> none = Sim::SimulateTimerWaitForPresentLoop(settings);
  EXPECT_NEAR(static_cast<double>(RefreshesPerFrameTimes100(none, period)), 195.0, 10.0);
  for (std::size_t index = 1; index < none.size(); ++index)
  {
    EXPECT_EQ(none[index].PendingAtStart, 0) << index;
  }

  settings.WaitingPresents = 2;
  const std::vector<Sim::LoopFrame> one = Sim::SimulateTimerWaitForPresentLoop(settings);
  EXPECT_NEAR(static_cast<double>(RefreshesPerFrameTimes100(one, period)), 100.0, 3.0);
  for (std::size_t index = 0; index < one.size(); ++index)
  {
    EXPECT_LE(one[index].PendingAtStart, 1) << index;
  }
}

TEST(FrameLoop, AWaitForAPresentThatRunsOutDoesNotStopTheLoop)
{
  // A display that shows nothing for a long stretch (a window that is hidden): every blank from 100 to 400 takes no frame
  Sim::LoopSettings settings = Loop(Sim::LoopProfile::RenderLate);
  settings.Frames = 200;
  settings.WaitingPresents = 1;
  settings.GpuWork = {PeriodTicks(settings) / 5, PeriodTicks(settings) / 5};
  for (int64_t blank = 100; blank < 400; ++blank)
  {
    settings.Display.HeldBlanks.push_back(blank);
  }
  const std::vector<Sim::LoopFrame> frames = Sim::SimulateTimerWaitForPresentLoop(settings);

  ASSERT_EQ(frames.size(), 200u);
  // The wait runs out after a quarter of a second, and the loop goes on at that pace until the display takes frames again
  EXPECT_GT(frames[102].StartTicks - frames[101].StartTicks, 2'000'000);
  EXPECT_LT(frames[102].StartTicks - frames[101].StartTicks, 3'000'000);
  EXPECT_GT(frames.back().StartTicks, frames[101].StartTicks);
}
