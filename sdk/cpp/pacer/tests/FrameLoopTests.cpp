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
