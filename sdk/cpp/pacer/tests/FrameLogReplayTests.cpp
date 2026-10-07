// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// A frame log replayed through the pacer (FrameLogReplay): the reader, and the replay of a simulated loop's log, where what the
// pacer answered and what the display did are both known. The first integration's logs of 2026-10-06 replay to the pacer's logged
// answers on every frame (2400 of 2400 in each of three runs, with pacer-replay); they are not in the repository.
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <gtest/gtest.h>
#include <algorithm>
#include <cstdint>
#include <filesystem>
#include <map>
#include <stdexcept>
#include <string>
#include <string_view>
#include <vector>
#include "FrameLogReplay.hpp"
#include "FrameLoopSimulation.hpp"
#include "LoggedFrame.hpp"
#include "LoopProfile.hpp"
#include "LoopSettings.hpp"
#include "ReplayResult.hpp"

namespace PC = MB::FramePacing::Pacer;
namespace Sim = MB::FramePacing::Pacer::Simulation;

namespace
{
  int64_t CountOf(const std::map<int64_t, int64_t>& counts, const int64_t value)
  {
    const auto found = counts.find(value);
    return found != counts.end() ? found->second : 0;
  }
}

TEST(FrameLogReplay, ALogIsReadByItsColumnNames)
{
  // The columns in another order than the sample writes them, one that is not read, an empty cell and a cell that is no number
  const std::string_view text =
    "\xEF\xBB\xBF"
    "swapInterval,frameIndex,other,frameStartTicks,firstPixelOutTicks,workCpuTicks,workGpuTicks,pacerOn,endFrameTicks\r\n"
    "1,7,x,1000,,30,400,1,1030\r\n"
    "2,8,1.5,2000,2500,31,401,0,\r\n";
  const std::vector<Sim::LoggedFrame> frames = Sim::ReadFrameLog(text);

  ASSERT_EQ(frames.size(), 2u);
  EXPECT_EQ(frames[0].FrameIndex, 7);
  EXPECT_EQ(frames[0].SwapInterval, 1u);
  EXPECT_EQ(frames[0].StartNanoseconds, 100'000);
  EXPECT_EQ(frames[0].ShownNanoseconds, 0);
  EXPECT_EQ(frames[0].WorkNanoseconds, 43'000);
  EXPECT_EQ(frames[0].EndFrameNanoseconds, 103'000);
  EXPECT_TRUE(frames[0].PacerOn);
  EXPECT_EQ(frames[1].FrameIndex, 8);
  EXPECT_EQ(frames[1].ShownNanoseconds, 250'000);
  EXPECT_EQ(frames[1].EndFrameNanoseconds, 0);
  EXPECT_FALSE(frames[1].PacerOn);
  // A column the log does not have
  EXPECT_EQ(frames[1].NextFrameStartNanoseconds, 0);
}

TEST(FrameLogReplay, ADisplayTimeIsTheDriversElseTheOneGivenAsFeedbackAndALogWithoutAPacerColumnIsOfAPacedRun)
{
  const std::vector<Sim::LoggedFrame> frames = Sim::ReadFrameLog(
    "frameIndex,frameStartTicks,firstPixelOutTicks,feedbackDisplayTicks\n"
    "0,1000,1500,1600\n"
    "1,2000,,2600\n");
  ASSERT_EQ(frames.size(), 2u);
  EXPECT_EQ(frames[0].ShownNanoseconds, 150'000);
  EXPECT_EQ(frames[1].ShownNanoseconds, 260'000);
  EXPECT_TRUE(frames[0].PacerOn);
}

TEST(FrameLogReplay, WhatIsNoFrameLogIsRefused)
{
  EXPECT_THROW(static_cast<void>(Sim::ReadFrameLog("")), std::runtime_error);
  EXPECT_THROW(static_cast<void>(Sim::ReadFrameLog("frame,startTicks\n1,1000\n")), std::runtime_error);
  // A frame's start one row early: the pacer's frame id is two ahead of the row's frame index
  EXPECT_THROW(static_cast<void>(Sim::ReadFrameLog("frameIndex,frameStartTicks,pacerFrameId\n0,1000,2\n1,2000,3\n2,3000,4\n")), std::runtime_error);
  EXPECT_NO_THROW(static_cast<void>(Sim::ReadFrameLog("frameIndex,frameStartTicks,pacerFrameId\n0,1000,1\n1,2000,2\n2,3000,3\n")));
  EXPECT_THROW(static_cast<void>(Sim::ReadFrameLogFile(std::filesystem::path("no-such-folder/no-such-log.csv"))), std::runtime_error);
}

TEST(FrameLogReplay, TheLogsRefreshPeriodIsItsTargetFrameTimeOverItsSwapInterval)
{
  const std::vector<Sim::LoggedFrame> frames = Sim::ReadFrameLog(
    "frameIndex,frameStartTicks,swapInterval,targetFrameTimeTicks\n"
    "0,1000,1,41664\n"
    "1,2000,2,83328\n"
    "2,3000,1,41664\n"
    "3,4000,1,41700\n");
  EXPECT_EQ(Sim::LoggedRefreshPeriod(frames).ToNanosecondTimeSpan().Nanoseconds(), 4'166'400);
  EXPECT_THROW(static_cast<void>(Sim::LoggedRefreshPeriod(Sim::ReadFrameLog("frameIndex,frameStartTicks\n0,1000\n"))), std::runtime_error);
}

TEST(FrameLogReplay, ASimulatedLoopsLogReplaysToThePacersAnswersOnEveryFrame)
{
  for (const Sim::LoopProfile profile : {Sim::LoopProfile::RenderLate, Sim::LoopProfile::RenderEarly})
  {
    Sim::LoopSettings settings;
    settings.Profile = profile;
    settings.Frames = 800;
    settings.TimerLate = {0, 200'000};
    settings.Display.HeldBlanks = {150, 300, 450};
    const std::vector<Sim::LoggedFrame> frames = Sim::ReadFrameLog(Sim::ToFrameLog(Sim::SimulateLoop(settings), settings));
    const PC::RefreshPeriod period = PC::RefreshPeriod::FromRate(settings.RateNumerator, settings.RateDenominator);

    const Sim::ReplayResult result = Sim::ReplayLog(frames, period, true);
    EXPECT_EQ(result.Frames, 800);
    EXPECT_EQ(result.Compared, 800);
    EXPECT_EQ(result.Agreeing, 800);
    EXPECT_EQ(result.Shown, 800);
    EXPECT_TRUE(result.Csv.starts_with(Sim::ReplayHeader));
    EXPECT_EQ(std::count(result.Csv.begin(), result.Csv.end(), '\n'), 801);

    // With the rule off the pacer answers the same here (the rule never changed the swap interval); on another refresh period it
    // does not
    EXPECT_EQ(Sim::ReplayLog(frames, period, false).Agreeing, 800);
    EXPECT_LT(Sim::ReplayLog(frames, PC::RefreshPeriod::FromRate(120), true).Agreeing, 800);
  }
}

TEST(FrameLogReplay, TodayAReplayShowsTheRefreshesTheDisplayFellBehindAndThePacerDidNotSee)
{
  Sim::LoopSettings settings;
  settings.Frames = 800;
  settings.Display.HeldBlanks = {150, 300, 450};
  const std::vector<Sim::LoggedFrame> frames = Sim::ReadFrameLog(Sim::ToFrameLog(Sim::SimulateLoop(settings), settings));
  const Sim::ReplayResult result = Sim::ReplayLog(frames, PC::RefreshPeriod::FromRate(settings.RateNumerator), true);

  // A stretch of about 150 frames at each latency, and the rest of the run at the last one
  EXPECT_EQ(CountOf(result.RefreshesToDisplay, 1), 149);
  EXPECT_EQ(CountOf(result.RefreshesToDisplay, 2), 149);
  EXPECT_EQ(CountOf(result.RefreshesToDisplay, 3), 149);
  EXPECT_EQ(CountOf(result.RefreshesToDisplay, 4), 353);
  EXPECT_EQ(CountOf(result.PendingAtStart, 0), 150);
  EXPECT_EQ(CountOf(result.PendingAtStart, 3), 350);
  // The pacer's intended display time is the frame's start plus a refresh throughout: the frames are shown up to three refreshes
  // after it
  EXPECT_EQ(CountOf(result.RefreshesAfterIntended, 0), 149);
  EXPECT_EQ(CountOf(result.RefreshesAfterIntended, 3), 353);
}

TEST(FrameLogReplay, FramesWithThePacerOffOrWithoutAStartAreNotReplayed)
{
  const std::vector<Sim::LoggedFrame> frames = Sim::ReadFrameLog(
    "frameIndex,frameStartTicks,pacerOn,presentCallTicks,firstPixelOutTicks\n"
    "0,10000000,0,10001000,10050000\n"
    "1,,1,,\n"
    "2,10041667,1,10042000,\n"
    "3,10083334,1,10084000,10250000\n");
  const Sim::ReplayResult result = Sim::ReplayLog(frames, PC::RefreshPeriod::FromRate(240), false);

  EXPECT_EQ(result.Frames, 2);
  // The log has no answer of its pacer to compare with
  EXPECT_EQ(result.Compared, 0);
  EXPECT_EQ(result.Shown, 1);
  // Frame 2 has no display time: its row's display cells are empty. Frame 0 was presented before frame 2 started and shown after
  EXPECT_NE(result.Csv.find("\n2,1004166700,1,0,"), std::string::npos);
  EXPECT_NE(result.Csv.find(",,,1\n"), std::string::npos);
  EXPECT_EQ(CountOf(result.RefreshesToDisplay, 4), 1);
}
