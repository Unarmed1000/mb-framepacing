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

  int64_t PeriodNanoseconds(const Sim::LoopSettings& settings)
  {
    return PC::RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator).ToNanosecondTimeSpan().Nanoseconds();
  }

  //! From a frame's start to its display, in half refreshes (the early profile's frames start in the middle of a refresh)
  int64_t HalfRefreshesToDisplay(const Sim::LoopFrame& frame, const int64_t periodNanoseconds)
  {
    return (((frame.ShownNanoseconds - frame.StartNanoseconds) * 2) + (periodNanoseconds / 2)) / periodNanoseconds;
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
      EXPECT_GE(frames[index].PresentNanoseconds, frames[index].WorkEndNanoseconds) << index;
      EXPECT_GE(frames[index].ShownNanoseconds, frames[index].PresentNanoseconds) << index;
      EXPECT_GE(frames[index].ShownNanoseconds, frames[index].GpuEndNanoseconds) << index;
      if (index > 0)
      {
        EXPECT_GT(frames[index].ShownNanoseconds, frames[index - 1].ShownNanoseconds) << index;
        EXPECT_GT(frames[index].StartNanoseconds, frames[index - 1].StartNanoseconds) << index;
        // The GPU works on one frame at a time
        EXPECT_GE(frames[index].GpuBeginNanoseconds, frames[index - 1].GpuEndNanoseconds) << index;
      }
    }
  }
}

TEST(FrameLoop, ALoopInStepWithTheDisplayKeepsItsLatency)
{
  const Sim::LoopSettings late = Loop(Sim::LoopProfile::RenderLate);
  const int64_t period = PeriodNanoseconds(late);
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
    const int64_t period = PeriodNanoseconds(settings);
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
      // A refresh, to the nanosecond the period is rounded to
      EXPECT_NEAR(static_cast<double>(frames[index].StartNanoseconds - frames[index - 1].StartNanoseconds), static_cast<double>(period), 1.0)
        << index;
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
    EXPECT_EQ(held[index].StartNanoseconds, inStep[index].StartNanoseconds) << index;
    EXPECT_EQ(held[index].SwapInterval, inStep[index].SwapInterval) << index;
    EXPECT_EQ(held[index].AnimationNanoseconds, inStep[index].AnimationNanoseconds) << index;
    EXPECT_EQ(held[index].IntendedDisplayNanoseconds, inStep[index].IntendedDisplayNanoseconds) << index;
    EXPECT_EQ(held[index].WindowLateFrames, 0u) << index;
  }
  // While the display shows the last frames three refreshes later
  EXPECT_EQ(held.back().ShownNanoseconds - inStep.back().ShownNanoseconds,
            PC::RefreshPeriod::FromRate(settings.RateNumerator).TimeFor(3).Nanoseconds());
}

TEST(FrameLoop, WithImagesThatBoundTheWaitingFramesTheLatencyStopsAtTheBound)
{
  Sim::LoopSettings settings = Loop(Sim::LoopProfile::RenderLate);
  settings.Frames = 800;
  settings.Display.Images = 3;
  settings.Display.HeldBlanks = {150, 300, 450};
  const int64_t period = PeriodNanoseconds(settings);
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
  settings.TimerLate = {0, 300'000};
  settings.Display.HeldBlanks = {150, 300};
  const int64_t period = PeriodNanoseconds(settings);
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
  settings.TimerLate = {0, 200'000};
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
  // Without its pause after start-up, which today's loop on this display has no need of either
  settings.StartupPauseRefreshes = 0;
  const std::vector<Sim::LoopFrame> today = Sim::SimulateLoop(settings);
  const std::vector<Sim::LoopFrame> paced = Sim::SimulateTimerPeriodOnlyLoop(settings);

  ASSERT_EQ(today.size(), paced.size());
  for (std::size_t index = 0; index < today.size(); ++index)
  {
    // The frame starts drift apart by a third of a nanosecond a frame: today's loop adds the period's rounded nanoseconds up, the grid
    // counts in the period itself
    EXPECT_NEAR(static_cast<double>(paced[index].StartNanoseconds), static_cast<double>(today[index].StartNanoseconds),
                1.0 + (static_cast<double>(index) / 3.0))
      << index;
    EXPECT_EQ(paced[index].ShownNanoseconds, today[index].ShownNanoseconds) << index;
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
  settings.LongFrameCpuNanoseconds = (PeriodNanoseconds(settings) * 16) / 10;
  const int64_t period = PeriodNanoseconds(settings);
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
  settings.LongFrameCpuNanoseconds = (PeriodNanoseconds(settings) * 16) / 10;
  // The pause after start-up is another test's
  settings.StartupPauseRefreshes = 0;
  const int64_t period = PeriodNanoseconds(settings);
  const std::vector<Sim::LoopFrame> frames = Sim::SimulateTimerPeriodOnlyLoop(settings);

  EXPECT_EQ(frames[99].PendingAtStart, 0);
  EXPECT_EQ(HalfRefreshesToDisplay(frames[99], period), 2);
  // Two frames after the long one the loop is where it was against the display: no frame waits, and a frame is shown a
  // refresh after its start, for the rest of the run
  for (std::size_t index = 103; index < frames.size(); ++index)
  {
    EXPECT_EQ(frames[index].PendingAtStart, 0) << index;
    EXPECT_EQ(HalfRefreshesToDisplay(frames[index], period), 2) << index;
    EXPECT_NEAR(static_cast<double>(frames[index].StartNanoseconds - frames[index - 1].StartNanoseconds), static_cast<double>(period), 1.0) << index;
  }
  // The animation time went on by a refresh per frame throughout: the refreshes the long frame took are not stepped over
  for (std::size_t index = 1; index < frames.size(); ++index)
  {
    EXPECT_NEAR(static_cast<double>(frames[index].AnimationStepNanoseconds), static_cast<double>(period), 1.0) << index;
  }
}

TEST(FrameLoop, TheLowestPairsPacerDoesNotSeeARefreshTheDisplayLostByItself)
{
  // What this tier can not do, pinned: the frames are ready in time, the display takes none at three vertical blanks, and each
  // costs a refresh of latency for the rest of the run, as with today's pacer
  Sim::LoopSettings settings = Loop(Sim::LoopProfile::RenderLate);
  settings.Frames = 800;
  settings.Display.HeldBlanks = {150, 300, 450};
  const int64_t period = PeriodNanoseconds(settings);
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
  settings.CpuWork = {(PeriodNanoseconds(settings) * 13) / 10, (PeriodNanoseconds(settings) * 13) / 10};
  settings.GpuWork = {PeriodNanoseconds(settings) / 5, PeriodNanoseconds(settings) / 5};
  settings.Display.HeldBlanks = {1'500};
  const int64_t period = PeriodNanoseconds(settings);
  const std::vector<Sim::LoopFrame> frames = Sim::SimulateTimerPeriodOnlyLoop(settings);

  EXPECT_EQ(frames.back().SwapInterval, 2u);
  // The frames at two refreshes per frame start two refreshes apart, wait before their present, and are shown two apart
  std::size_t checked = 0;
  for (std::size_t index = frames.size() - 100; index < frames.size(); ++index)
  {
    ASSERT_EQ(frames[index].SwapInterval, 2u) << index;
    EXPECT_NEAR(static_cast<double>(frames[index].StartNanoseconds - frames[index - 1].StartNanoseconds), static_cast<double>(2 * period), 1.0)
      << index;
    EXPECT_NEAR(static_cast<double>(frames[index].ShownNanoseconds - frames[index - 1].ShownNanoseconds), static_cast<double>(2 * period), 1.0)
      << index;
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
  int64_t RefreshesPerFrameTimes100(const std::vector<Sim::LoopFrame>& frames, const int64_t periodNanoseconds)
  {
    const std::size_t first = frames.size() / 2;
    const int64_t span = frames.back().StartNanoseconds - frames[first].StartNanoseconds;
    return (span * 100) / (periodNanoseconds * static_cast<int64_t>(frames.size() - 1 - first));
  }
}

TEST(FrameLoop, WithAWaitForTheLastPresentNoFrameWaitsAndALostRefreshCostsOneFrameStart)
{
  Sim::LoopSettings settings = Loop(Sim::LoopProfile::RenderLate);
  settings.Frames = 800;
  settings.WaitingPresents = 1;
  // Light work: a fifth of a refresh on the GPU
  settings.GpuWork = {PeriodNanoseconds(settings) / 5, PeriodNanoseconds(settings) / 5};
  settings.Display.HeldBlanks = {150, 300, 450};
  const int64_t period = PeriodNanoseconds(settings);
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
  settings.GpuWork = {PeriodNanoseconds(settings) / 5, PeriodNanoseconds(settings) / 5};
  settings.Display.HeldBlanks = {150, 300, 450};
  const int64_t period = PeriodNanoseconds(settings);
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
  const int64_t period = PeriodNanoseconds(settings);

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
  settings.GpuWork = {PeriodNanoseconds(settings) / 5, PeriodNanoseconds(settings) / 5};
  for (int64_t blank = 100; blank < 400; ++blank)
  {
    settings.Display.HeldBlanks.push_back(blank);
  }
  const std::vector<Sim::LoopFrame> frames = Sim::SimulateTimerWaitForPresentLoop(settings);

  ASSERT_EQ(frames.size(), 200u);
  // The wait runs out after four of the frame's swap intervals, and the loop goes on at that pace until the display takes
  // frames again
  const int64_t period = PeriodNanoseconds(settings);
  EXPECT_GE(frames[102].StartNanoseconds - frames[101].StartNanoseconds, 4 * period);
  EXPECT_LE(frames[102].StartNanoseconds - frames[101].StartNanoseconds, 5 * period);
  EXPECT_GT(frames.back().StartNanoseconds, frames[101].StartNanoseconds);
}

// What the first measurements of the two tier pacers asked for: a pause after start-up in the lowest pair's pacer, the GPU's work
// as its own stretch of time, and an animation step that follows a loss that repeats.

TEST(FrameLoop, TheLowestPairsPauseAfterStartUpLetsTheDisplayTakeTheFramesThatPiledUp)
{
  // Light work, and a display that takes no frame at two of its first vertical blanks: two frames wait from then on
  Sim::LoopSettings settings = Loop(Sim::LoopProfile::RenderLate);
  settings.Frames = 800;
  settings.GpuWork = {PeriodNanoseconds(settings) / 5, PeriodNanoseconds(settings) / 5};
  settings.Display.HeldBlanks = {5, 6};
  const int64_t period = PeriodNanoseconds(settings);

  // Without the pause they wait for the whole run
  settings.StartupPauseRefreshes = 0;
  const std::vector<Sim::LoopFrame> kept = Sim::SimulateTimerPeriodOnlyLoop(settings);
  for (std::size_t index = 20; index < kept.size(); ++index)
  {
    ASSERT_EQ(kept[index].PendingAtStart, 2) << index;
    ASSERT_EQ(HalfRefreshesToDisplay(kept[index], period), 6) << index;
  }

  // With it they wait for half a second, and no frame waits after it
  settings.StartupPauseRefreshes = 4;
  const std::vector<Sim::LoopFrame> frames = Sim::SimulateTimerPeriodOnlyLoop(settings);
  int32_t pauses = 0;
  for (std::size_t index = 20; index < frames.size(); ++index)
  {
    const int64_t refreshes = ((frames[index].StartNanoseconds - frames[index - 1].StartNanoseconds) + (period / 2)) / period;
    pauses += refreshes == 5 ? 1 : 0;
    ASSERT_TRUE(refreshes == 1 || refreshes == 5) << index;
    if (pauses == 0)
    {
      ASSERT_EQ(frames[index].PendingAtStart, 2) << index;
    }
    else
    {
      ASSERT_EQ(frames[index].PendingAtStart, 0) << index;
      ASSERT_EQ(HalfRefreshesToDisplay(frames[index], period), 2) << index;
    }
    // The animation time goes on by a refresh per frame through it
    ASSERT_NEAR(static_cast<double>(frames[index].AnimationStepNanoseconds), static_cast<double>(period), 1.0) << index;
  }
  EXPECT_EQ(pauses, 1);
  // Half a second after the first frame: the frame 120 periods in is the last before it
  EXPECT_NEAR(static_cast<double>(frames[121].StartNanoseconds - frames[120].StartNanoseconds), static_cast<double>(5 * period), 2.0);
}

TEST(FrameLoop, WithoutGpuWorkReportsALoopTheGpuLimitsIsNotSlowedDownAndWithThemItIs)
{
  // GPU work of 130 % of a refresh and little on the CPU, two frames in flight, nothing that bounds the frames that wait
  Sim::LoopSettings settings;
  settings.Frames = 1'200;
  settings.GpuWork = {(PeriodNanoseconds(settings) * 13) / 10, (PeriodNanoseconds(settings) * 13) / 10};
  settings.WaitsForPreviousGpuWork = false;
  settings.MaxFramesInFlight = 2;
  settings.StartupPauseRefreshes = 0;
  const int64_t period = PeriodNanoseconds(settings);

  // The CPU's work fits and every frame starts on its step: the pacer sees nothing, and the frames fall further behind
  const std::vector<Sim::LoopFrame> blind = Sim::SimulateTimerPeriodOnlyLoop(settings);
  EXPECT_EQ(blind.back().SwapInterval, 1u);
  EXPECT_GT(HalfRefreshesToDisplay(blind.back(), period), 600);

  // Told of the GPU's work, a frame's work is over its time: two refreshes per frame, where it fits
  settings.ReportsGpuWork = true;
  const std::vector<Sim::LoopFrame> frames = Sim::SimulateTimerPeriodOnlyLoop(settings);
  EXPECT_EQ(frames.back().SwapInterval, 2u);
  EXPECT_EQ(frames.back().WorkGpuNanoseconds, (period * 13) / 10);
  for (std::size_t index = frames.size() - 200; index < frames.size(); ++index)
  {
    ASSERT_EQ(frames[index].SwapInterval, 2u) << index;
    ASSERT_NEAR(static_cast<double>(frames[index].StartNanoseconds - frames[index - 1].StartNanoseconds), static_cast<double>(2 * period), 1.0)
      << index;
  }
}

TEST(FrameLoop, WorkOfThreeQuartersOfARefreshOnEachHoldsOneRefreshPerFrameSideBySideAndNeedsTwoOneAfterTheOther)
{
  // CPU work of 74 % and GPU work of 72 % of a refresh, as the first integration measured it, the rule on
  Sim::LoopSettings settings;
  settings.Frames = 1'200;
  settings.CpuWork = {(PeriodNanoseconds(settings) * 74) / 100, (PeriodNanoseconds(settings) * 74) / 100};
  settings.GpuWork = {(PeriodNanoseconds(settings) * 72) / 100, (PeriodNanoseconds(settings) * 72) / 100};
  settings.ReportsGpuWork = true;
  settings.StartupPauseRefreshes = 0;
  const int64_t period = PeriodNanoseconds(settings);

  // Two frames in flight, which the loop does not say: the frames' times show the two side by side, the longer of them fits,
  // and the rule stays at one refresh per frame
  settings.WaitsForPreviousGpuWork = false;
  const std::vector<Sim::LoopFrame> sideBySide = Sim::SimulateTimerPeriodOnlyLoop(settings);
  for (std::size_t index = 0; index < sideBySide.size(); ++index)
  {
    ASSERT_EQ(sideBySide[index].SwapInterval, 1u) << index;
    ASSERT_EQ(sideBySide[index].WindowLateFrames, 0u) << index;
  }
  EXPECT_NEAR(static_cast<double>(RefreshesPerFrameTimes100(sideBySide, period)), 100.0, 1.0);

  // One frame in flight: the two added are 146 % of a refresh, and the rule goes to two refreshes per frame
  settings.WaitsForPreviousGpuWork = true;
  const std::vector<Sim::LoopFrame> inSeries = Sim::SimulateTimerPeriodOnlyLoop(settings);
  EXPECT_EQ(inSeries.back().SwapInterval, 2u);
  EXPECT_NEAR(static_cast<double>(RefreshesPerFrameTimes100(inSeries, period)), 200.0, 1.0);
}

TEST(FrameLoop, SideBySideAWaitForAPresentNeedsOnePresentMoreAllowedToWaitThanInSeries)
{
  // The same work with two frames in flight and a wait for a present. On this display a frame is on screen two refreshes after
  // its start, so the present before the last is not shown yet when the next frame is due: with one present allowed to wait
  // the wait holds the loop off its step for some frames, and each of those costs a refresh. With two allowed to wait the wait
  // returns at once and the loop holds one refresh per frame. The simulation's finding, not a measurement
  Sim::LoopSettings settings;
  settings.Frames = 1'200;
  settings.CpuWork = {(PeriodNanoseconds(settings) * 74) / 100, (PeriodNanoseconds(settings) * 74) / 100};
  settings.GpuWork = {(PeriodNanoseconds(settings) * 72) / 100, (PeriodNanoseconds(settings) * 72) / 100};
  settings.ReportsGpuWork = true;
  settings.WaitsForPreviousGpuWork = false;
  settings.MaxFramesInFlight = 2;
  const int64_t period = PeriodNanoseconds(settings);

  settings.WaitingPresents = 2;
  const std::vector<Sim::LoopFrame> held = Sim::SimulateTimerWaitForPresentLoop(settings);
  int32_t lostStarts = 0;
  for (std::size_t index = 1; index < held.size(); ++index)
  {
    lostStarts += (held[index].StartNanoseconds - held[index - 1].StartNanoseconds) > ((period * 3) / 2) ? 1 : 0;
  }
  EXPECT_GT(lostStarts, 30);

  settings.WaitingPresents = 3;
  const std::vector<Sim::LoopFrame> frames = Sim::SimulateTimerWaitForPresentLoop(settings);
  for (std::size_t index = 0; index < frames.size(); ++index)
  {
    ASSERT_EQ(frames[index].SwapInterval, 1u) << index;
    ASSERT_LE(frames[index].PendingAtStart, 2) << index;
  }
  EXPECT_NEAR(static_cast<double>(RefreshesPerFrameTimes100(frames, period)), 100.0, 1.0);
}

TEST(FrameLoop, WhenEveryFrameLosesARefreshTheAnimationKeepsUpWithTheClock)
{
  // GPU work of 90 % of a refresh, a wait for the last present, the rule off: a frame about every two refreshes at a swap
  // interval of one. The first integration measured the animation at half speed in this case
  Sim::LoopSettings settings = Loop(Sim::LoopProfile::RenderLate);
  settings.Frames = 800;
  settings.WaitingPresents = 1;
  const std::vector<Sim::LoopFrame> frames = Sim::SimulateTimerWaitForPresentLoop(settings);

  const std::size_t first = frames.size() / 2;
  const int64_t clock = frames.back().StartNanoseconds - frames[first].StartNanoseconds;
  const int64_t animation = frames.back().AnimationNanoseconds - frames[first].AnimationNanoseconds;
  EXPECT_NEAR(static_cast<double>(animation * 100) / static_cast<double>(clock), 100.0, 5.0);
}

TEST(FrameLoop, WhereverTheGridSitsAgainstTheDisplayAFrameThatRanLongLeavesNoFrameWaiting)
{
  // The lowest pair's pacer does not know where in its step the display takes a frame, so every place is tried (how long
  // before a vertical blank a frame has to be ready, in tenths of a refresh), with long frames of several lengths. Each is
  // presented after the step the next frame was due at, which the pacer knows of. A frame that is presented in time and
  // ready too late for its refresh is another matter: this pacer does not learn of it
  for (int64_t tenth = 0; tenth < 10; ++tenth)
  {
    for (const int64_t longPercent : {110, 135, 160, 190, 240, 265})
    {
      Sim::LoopSettings settings = Loop(Sim::LoopProfile::RenderLate);
      settings.Frames = 300;
      settings.StartupPauseRefreshes = 0;
      const int64_t period = PeriodNanoseconds(settings);
      settings.GpuWork = {period / 5, period / 5};
      settings.Display.LatchLeadNanoseconds = (period * tenth) / 10;
      settings.LongFrames = {100};
      settings.LongFrameCpuNanoseconds = (period * longPercent) / 100;
      const std::vector<Sim::LoopFrame> frames = Sim::SimulateTimerPeriodOnlyLoop(settings);

      // Ten frames after the long one and for the rest of the run: as many frames wait as before it, and a frame is on
      // screen as long after its start
      for (std::size_t index = 110; index < frames.size(); ++index)
      {
        ASSERT_EQ(frames[index].PendingAtStart, frames[90].PendingAtStart) << tenth << ' ' << longPercent << ' ' << index;
        ASSERT_EQ(HalfRefreshesToDisplay(frames[index], period), HalfRefreshesToDisplay(frames[90], period))
          << tenth << ' ' << longPercent << ' ' << index;
      }
    }
  }
}

// The two aims. The aim of low latency is what the tests above are about; these are about the aim of smoothness, where frames
// are made ahead of the display and wait to be shown as a reserve.

namespace
{
  //! The frames from the 30th on that were not on screen for exactly one refresh
  int32_t DisplayStepsOff(const std::vector<Sim::LoopFrame>& frames, const int64_t periodNanoseconds)
  {
    int32_t off = 0;
    for (std::size_t index = 30; index < frames.size(); ++index)
    {
      off += ((frames[index].ShownNanoseconds - frames[index - 1].ShownNanoseconds) + (periodNanoseconds / 2)) / periodNanoseconds != 1 ? 1 : 0;
    }
    return off;
  }

  //! Light work at 240 Hz with the lowest pair's pacer, a display that takes a frame so many tenths of a refresh before its
  //! vertical blank, and one frame that runs long
  Sim::LoopSettings LoopWithALongFrame(const int64_t tenth, const int64_t longPercent, const PC::PacerAim aim)
  {
    Sim::LoopSettings settings = Loop(Sim::LoopProfile::RenderLate);
    settings.Frames = 600;
    settings.Aim = aim;
    settings.StartupPauseRefreshes = 0;
    const int64_t period = PeriodNanoseconds(settings);
    settings.GpuWork = {period / 5, period / 5};
    settings.Display.LatchLeadNanoseconds = (period * tenth) / 10;
    settings.LongFrames = {100};
    settings.LongFrameCpuNanoseconds = (period * longPercent) / 100;
    return settings;
  }
}

TEST(FrameLoop, WithTheAimOfSmoothnessAFrameWaitsAndEveryFrameIsShownForOneRefresh)
{
  Sim::LoopSettings settings = Loop(Sim::LoopProfile::RenderLate);
  settings.Frames = 600;
  settings.Aim = PC::PacerAim::Smoothness;
  const int64_t period = PeriodNanoseconds(settings);
  settings.GpuWork = {period / 5, period / 5};
  const std::vector<Sim::LoopFrame> frames = Sim::SimulateTimerPeriodOnlyLoop(settings);

  // One present may wait beside the frame that is made, and one does: a frame is on screen two refreshes after its start
  for (std::size_t index = 5; index < frames.size(); ++index)
  {
    ASSERT_EQ(frames[index].PendingAtStart, 1) << index;
    ASSERT_EQ(HalfRefreshesToDisplay(frames[index], period), 4) << index;
    ASSERT_NEAR(static_cast<double>(frames[index].AnimationStepNanoseconds), static_cast<double>(period), 1.0) << index;
  }
  EXPECT_EQ(DisplayStepsOff(frames, period), 0);

  // With two that may wait, two do
  settings.WaitingPresents = 3;
  const std::vector<Sim::LoopFrame> deeper = Sim::SimulateTimerPeriodOnlyLoop(settings);
  EXPECT_EQ(deeper.back().PendingAtStart, 2);
  EXPECT_EQ(HalfRefreshesToDisplay(deeper.back(), period), 6);
  EXPECT_EQ(DisplayStepsOff(deeper, period), 0);
}

TEST(FrameLoop, AFrameThatRunsLongWithinTheReserveIsNotSeenWithTheAimOfSmoothnessAndIsARepeatedFrameWithLowLatency)
{
  // CPU work of 0.9 of a refresh more, once. Wherever the display takes its frame in the refresh
  int32_t placesWithARepeat = 0;
  for (int64_t tenth = 0; tenth < 10; ++tenth)
  {
    const Sim::LoopSettings smooth = LoopWithALongFrame(tenth, 90, PC::PacerAim::Smoothness);
    EXPECT_EQ(DisplayStepsOff(Sim::SimulateTimerPeriodOnlyLoop(smooth), PeriodNanoseconds(smooth)), 0) << tenth;

    const Sim::LoopSettings lowLatency = LoopWithALongFrame(tenth, 90, PC::PacerAim::LowLatency);
    placesWithARepeat += DisplayStepsOff(Sim::SimulateTimerPeriodOnlyLoop(lowLatency), PeriodNanoseconds(lowLatency)) > 0 ? 1 : 0;
  }
  EXPECT_GE(placesWithARepeat, 8);
}

TEST(FrameLoop, WithTheAimOfSmoothnessTheReserveIsThereAgainAfterAFrameThatRanLongerThanItCovers)
{
  // CPU work of 2.4 refreshes more, once: more than the one frame made ahead covers. One frame is on screen longer, and after
  // it the frames that wait are the reserve again, or one more: this pacer does not see the display, so it gives up the steps
  // it is sure the display repeated a frame for and no more
  for (int64_t tenth = 0; tenth < 10; ++tenth)
  {
    const Sim::LoopSettings settings = LoopWithALongFrame(tenth, 240, PC::PacerAim::Smoothness);
    const int64_t period = PeriodNanoseconds(settings);
    const std::vector<Sim::LoopFrame> frames = Sim::SimulateTimerPeriodOnlyLoop(settings);
    EXPECT_EQ(DisplayStepsOff(frames, period), 1) << tenth;
    for (std::size_t index = 110; index < frames.size(); ++index)
    {
      ASSERT_GE(frames[index].PendingAtStart, frames[90].PendingAtStart) << tenth << ' ' << index;
      ASSERT_LE(frames[index].PendingAtStart, frames[90].PendingAtStart + 1) << tenth << ' ' << index;
    }
  }
}

TEST(FrameLoop, WithTheAimOfSmoothnessAndAWaitForAPresentTheReserveIsExactlyWhatMayWait)
{
  // The wait is what this pacer has over the one without: after a long frame, and after vertical blanks at which the display
  // took no frame, the frames that wait are the one that may, not more
  Sim::LoopSettings settings = Loop(Sim::LoopProfile::RenderLate);
  settings.Frames = 900;
  settings.Aim = PC::PacerAim::Smoothness;
  const int64_t period = PeriodNanoseconds(settings);
  settings.GpuWork = {period / 5, period / 5};
  settings.LongFrames = {100};
  settings.LongFrameCpuNanoseconds = (period * 24) / 10;
  settings.Display.HeldBlanks = {300, 450, 600};
  const std::vector<Sim::LoopFrame> frames = Sim::SimulateTimerWaitForPresentLoop(settings);

  for (std::size_t index = 5; index < frames.size(); ++index)
  {
    ASSERT_LE(frames[index].PendingAtStart, 1) << index;
  }
  for (const std::size_t index : {std::size_t{90}, std::size_t{250}, std::size_t{400}, std::size_t{550}, frames.size() - 1})
  {
    EXPECT_EQ(frames[index].PendingAtStart, 1) << index;
  }
  EXPECT_NEAR(static_cast<double>(RefreshesPerFrameTimes100(frames, period)), 100.0, 2.0);
}

// The pacer of vertical blank times (VBlankPeriodOnlyPacer): the application gives it the display's last vertical blank before
// every frame, so every frame is for one vertical blank and nothing is a guess but how long before a vertical blank the display
// takes a frame. The model's displays here take one up to 0.4 of a refresh before it.

namespace
{
  //! The frames from the 30th on that were not on screen for their swap interval
  int32_t DisplayStepsOffTheSwapInterval(const std::vector<Sim::LoopFrame>& frames, const int64_t periodNanoseconds)
  {
    int32_t off = 0;
    for (std::size_t index = 30; index < frames.size(); ++index)
    {
      const int64_t refreshes = ((frames[index].ShownNanoseconds - frames[index - 1].ShownNanoseconds) + (periodNanoseconds / 2)) / periodNanoseconds;
      off += refreshes != int64_t{frames[index - 1].SwapInterval} ? 1 : 0;
    }
    return off;
  }

  //! Light work at 240 Hz, a display that takes a frame so many tenths of a refresh before its vertical blank
  Sim::LoopSettings LightLoop(const int64_t tenth, const PC::PacerAim aim, const uint32_t waitingPresents = 2)
  {
    Sim::LoopSettings settings = Loop(Sim::LoopProfile::RenderLate);
    settings.Frames = 600;
    settings.Aim = aim;
    settings.WaitingPresents = waitingPresents;
    settings.StartupPauseRefreshes = 0;
    const int64_t period = PeriodNanoseconds(settings);
    settings.GpuWork = {period / 5, period / 5};
    settings.Display.LatchLeadNanoseconds = (period * tenth) / 10;
    return settings;
  }
}

TEST(FrameLoop, WithVerticalBlankTimesLowLatencyShowsANewerFrameAndSmoothnessMakesItEarlier)
{
  for (int64_t tenth = 0; tenth < 5; ++tenth)
  {
    // Low latency: a frame is on screen about 0.65 of a refresh after its start (the pacer on a timer: a whole one or more)
    const Sim::LoopSettings lowLatency = LightLoop(tenth, PC::PacerAim::LowLatency);
    const int64_t period = PeriodNanoseconds(lowLatency);
    const std::vector<Sim::LoopFrame> fresh = Sim::SimulateVBlankPeriodOnlyLoop(lowLatency);
    // Smoothness without a reserve: made a refresh earlier. With a frame in reserve: one more
    const std::vector<Sim::LoopFrame> early = Sim::SimulateVBlankPeriodOnlyLoop(LightLoop(tenth, PC::PacerAim::Smoothness, 1));
    const std::vector<Sim::LoopFrame> reserve = Sim::SimulateVBlankPeriodOnlyLoop(LightLoop(tenth, PC::PacerAim::Smoothness, 2));
    for (std::size_t index = 30; index < fresh.size(); ++index)
    {
      ASSERT_EQ(HalfRefreshesToDisplay(fresh[index], period), 1) << tenth << ' ' << index;
      ASSERT_EQ(fresh[index].PendingAtStart, 0) << tenth << ' ' << index;
      ASSERT_EQ(HalfRefreshesToDisplay(early[index], period), 3) << tenth << ' ' << index;
      ASSERT_EQ(HalfRefreshesToDisplay(reserve[index], period), 5) << tenth << ' ' << index;
    }
    EXPECT_EQ(DisplayStepsOffTheSwapInterval(fresh, period), 0) << tenth;
    EXPECT_EQ(DisplayStepsOffTheSwapInterval(early, period), 0) << tenth;
    EXPECT_EQ(DisplayStepsOffTheSwapInterval(reserve, period), 0) << tenth;
  }
}

TEST(FrameLoop, ADisplayThatIsSlowerThanItsModeSaysLeavesAPacerOnATimerBehindAndNotOneWithVerticalBlankTimes)
{
  // The display's refresh period is 0.2 % longer than the loop was told: the first integration measured 17 to 19 parts in a
  // million, which is the same thing a hundred times slower
  Sim::LoopSettings settings = LightLoop(2, PC::PacerAim::LowLatency);
  settings.Frames = 1'200;
  settings.DisplayPeriodPpm = 2'000;
  const int64_t period = PeriodNanoseconds(settings);

  // On a timer the loop makes frames faster than the display shows them, and they pile up
  const std::vector<Sim::LoopFrame> timer = Sim::SimulateTimerPeriodOnlyLoop(settings);
  EXPECT_GE(timer.back().PendingAtStart, timer[60].PendingAtStart + 2);

  // With vertical blank times every reading puts the frames back on the display, with either aim
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    settings.Aim = aim;
    const std::vector<Sim::LoopFrame> frames = Sim::SimulateVBlankPeriodOnlyLoop(settings);
    for (std::size_t index = 60; index < frames.size(); ++index)
    {
      ASSERT_EQ(frames[index].PendingAtStart, frames[60].PendingAtStart) << index;
    }
    EXPECT_EQ(DisplayStepsOffTheSwapInterval(frames, period), 0);
  }
}

TEST(FrameLoop, AtFourRefreshesPerFrameAPacerOnATimerDriftsAcrossTheVerticalBlankAndOneWithVerticalBlankTimesDoesNot)
{
  // 60 frames a second at 240 Hz on a display 0.05 % slower than its mode, timers that wake up to 0.1 ms late
  Sim::LoopSettings settings = LightLoop(2, PC::PacerAim::LowLatency);
  settings.Frames = 1'200;
  settings.PreferredSwapInterval = 4;
  settings.DisplayPeriodPpm = 500;
  settings.TimerLate = {0, 100'000};
  const int64_t period = PeriodNanoseconds(settings);

  // On a timer the present is held to a moment that slides against the display, and while it is near a vertical blank frames
  // fall on either side of it: on screen for three or five refreshes
  EXPECT_GT(DisplayStepsOffTheSwapInterval(Sim::SimulateTimerPeriodOnlyLoop(settings), period), 5);

  // With vertical blank times every frame is on screen for four, and with the aim of low latency it is on screen 0.65 of a
  // refresh after its start: it is started in the refresh before its vertical blank, not four refreshes ahead
  const std::vector<Sim::LoopFrame> fresh = Sim::SimulateVBlankPeriodOnlyLoop(settings);
  EXPECT_EQ(DisplayStepsOffTheSwapInterval(fresh, period), 0);
  settings.Aim = PC::PacerAim::Smoothness;
  const std::vector<Sim::LoopFrame> early = Sim::SimulateVBlankPeriodOnlyLoop(settings);
  EXPECT_EQ(DisplayStepsOffTheSwapInterval(early, period), 0);
  for (std::size_t index = 30; index < fresh.size(); ++index)
  {
    ASSERT_EQ(fresh[index].SwapInterval, 4u) << index;
    ASSERT_EQ(HalfRefreshesToDisplay(fresh[index], period), 1) << index;
    // Made early, a frame is on screen four and a half refreshes after its start
    ASSERT_EQ(HalfRefreshesToDisplay(early[index], period), 9) << index;
  }
}

TEST(FrameLoop, WithVerticalBlankTimesAFrameThatRunsLongLeavesTheLoopWhereItWas)
{
  for (int64_t tenth = 0; tenth < 5; ++tenth)
  {
    for (const int64_t longPercent : {90, 240})
    {
      Sim::LoopSettings settings = LightLoop(tenth, PC::PacerAim::LowLatency);
      const int64_t period = PeriodNanoseconds(settings);
      settings.LongFrames = {100};
      settings.LongFrameCpuNanoseconds = (period * longPercent) / 100;

      // Low latency: the long frame misses its vertical blank, and some refreshes later the loop is exactly where it was
      const std::vector<Sim::LoopFrame> fresh = Sim::SimulateVBlankPeriodOnlyLoop(settings);
      EXPECT_GE(DisplayStepsOffTheSwapInterval(fresh, period), 1) << tenth << ' ' << longPercent;
      EXPECT_LE(DisplayStepsOffTheSwapInterval(fresh, period), 2) << tenth << ' ' << longPercent;
      for (std::size_t index = 130; index < fresh.size(); ++index)
      {
        ASSERT_EQ(fresh[index].PendingAtStart, fresh[90].PendingAtStart) << tenth << ' ' << longPercent << ' ' << index;
        ASSERT_EQ(HalfRefreshesToDisplay(fresh[index], period), HalfRefreshesToDisplay(fresh[90], period))
          << tenth << ' ' << longPercent << ' ' << index;
      }

      // Smoothness with a frame in reserve: 0.9 of a refresh more is not seen at all, and after 2.4 more one frame is on screen
      // longer and the reserve is what it was, exactly: this pacer knows which vertical blank a frame was ready for
      settings.Aim = PC::PacerAim::Smoothness;
      const std::vector<Sim::LoopFrame> reserve = Sim::SimulateVBlankPeriodOnlyLoop(settings);
      EXPECT_EQ(DisplayStepsOffTheSwapInterval(reserve, period), longPercent == 90 ? 0 : 1) << tenth << ' ' << longPercent;
      for (std::size_t index = 130; index < reserve.size(); ++index)
      {
        ASSERT_EQ(reserve[index].PendingAtStart, reserve[90].PendingAtStart) << tenth << ' ' << longPercent << ' ' << index;
      }
    }
  }
}

TEST(FrameLoop, WithVerticalBlankTimesAndGpuWorkReportsAHeavyGpuLoadIsHeldAndOneThatDoesNotFitSlowsTheLoopDown)
{
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    // GPU work of 90 % of a refresh at a fixed swap interval of one: a frame is started, or presented, that much sooner, and
    // every frame is on screen for one refresh
    Sim::LoopSettings settings = Loop(Sim::LoopProfile::RenderLate);
    settings.Frames = 800;
    settings.Aim = aim;
    settings.ReportsGpuWork = true;
    settings.StartupPauseRefreshes = 0;
    settings.Display.LatchLeadNanoseconds = PeriodNanoseconds(settings) / 5;
    const int64_t period = PeriodNanoseconds(settings);
    const std::vector<Sim::LoopFrame> held = Sim::SimulateVBlankPeriodOnlyLoop(settings);
    EXPECT_EQ(DisplayStepsOffTheSwapInterval(held, period), 0);
    EXPECT_EQ(held.back().WorkGpuNanoseconds, (period * 9) / 10);

    // GPU work of 130 % with the rule on: two refreshes per frame
    settings.AutoSwapInterval = true;
    settings.Frames = 1'200;
    settings.GpuWork = {(period * 13) / 10, (period * 13) / 10};
    settings.WaitsForPreviousGpuWork = false;
    settings.MaxFramesInFlight = 2;
    const std::vector<Sim::LoopFrame> slowed = Sim::SimulateVBlankPeriodOnlyLoop(settings);
    EXPECT_EQ(slowed.back().SwapInterval, 2u);
    for (std::size_t index = slowed.size() - 200; index < slowed.size(); ++index)
    {
      ASSERT_EQ(slowed[index].SwapInterval, 2u) << index;
      ASSERT_NEAR(static_cast<double>(slowed[index].ShownNanoseconds - slowed[index - 1].ShownNanoseconds), static_cast<double>(2 * period), 2.0)
        << index;
    }
  }
}
