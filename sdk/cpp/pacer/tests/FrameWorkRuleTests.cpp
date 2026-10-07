// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// A frame's work as two stretches of time: the CPU's and the GPU's, the longer of the two where they lie side by side and the two
// added where one follows the other.
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeDuration.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/pacer/frame/GpuWorkReport.hpp>
#include <mb/framepacing/pacer/rule/FrameWorkRule.hpp>
#include <gtest/gtest.h>
#include <cstdint>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

namespace
{
  constexpr int64_t Period = 100'000;
  constexpr int64_t Start = 10'000'000;
  constexpr FP::TimeSpan Margin(10'000);

  constexpr FP::TickCount64 At(const int64_t ticks) noexcept
  {
    return FP::TickCount64(ticks);
  }

  constexpr FP::TimeSpan Span(const int64_t ticks) noexcept
  {
    return FP::TimeSpan(ticks);
  }

  //! Frames 1 to count, a period apart from Start
  void AddFrames(PC::FrameWorkRule& rRule, const uint64_t count)
  {
    for (uint64_t frameId = 1; frameId <= count; ++frameId)
    {
      rRule.AddFrameStart(frameId, At(Start + (static_cast<int64_t>(frameId - 1) * Period)));
    }
  }
}

TEST(FrameWorkRule, WithoutAGpuWorkReportAFramesWorkIsTheCpus)
{
  PC::FrameWorkRule rule;
  AddFrames(rule, 3);
  EXPECT_FALSE(rule.HasGpuTime());
  EXPECT_FALSE(rule.OverlapSeen());
  EXPECT_EQ(rule.GpuTime(), FP::TimeDuration::Zero());
  EXPECT_EQ(rule.WorkOf(Span(30'000), 1), Span(30'000));
  EXPECT_EQ(rule.WorkOf(Span(30'000), 2), Span(30'000));
}

TEST(FrameWorkRule, OneAfterTheOtherTheTwoAreAdded)
{
  PC::FrameWorkRule rule;
  AddFrames(rule, 3);
  // The GPU was done with frame 1 before frame 2 began
  rule.AddGpuWork(PC::GpuWorkReport::Times(1, At(Start + 30'000), At(Start + 90'000)), Margin);
  EXPECT_TRUE(rule.HasGpuTime());
  EXPECT_FALSE(rule.OverlapSeen());
  EXPECT_EQ(rule.GpuTime(), FP::TimeDuration::FromTicks(60'000));
  EXPECT_EQ(rule.WorkOf(Span(30'000), 1), Span(90'000));
}

TEST(FrameWorkRule, SideBySideItIsTheLongerOfTheTwo)
{
  PC::FrameWorkRule rule;
  AddFrames(rule, 3);
  // The GPU was on frame 1 until 0.4 periods after frame 2 began
  rule.AddGpuWork(PC::GpuWorkReport::Times(1, At(Start + 70'000), At(Start + 140'000)), Margin);
  EXPECT_TRUE(rule.OverlapSeen());
  EXPECT_EQ(rule.WorkOf(Span(30'000), 1), Span(70'000));
  EXPECT_EQ(rule.WorkOf(Span(80'000), 1), Span(80'000));
  // An end and a duration say as much
  rule.AddGpuWork(PC::GpuWorkReport::EndAndDuration(2, At(Start + 250'000), FP::TimeDuration::FromTicks(75'000)), Margin);
  EXPECT_TRUE(rule.OverlapSeen());
  EXPECT_EQ(rule.WorkOf(Span(30'000), 1), Span(75'000));
}

TEST(FrameWorkRule, AFrameThatBeganWithinTheMarginOfTheEndDidNotBeginBeforeIt)
{
  PC::FrameWorkRule rule;
  AddFrames(rule, 3);
  rule.AddGpuWork(PC::GpuWorkReport::Times(1, At(Start + 30'000), At(Start + Period + 10'000)), Margin);
  EXPECT_FALSE(rule.OverlapSeen());
  EXPECT_EQ(rule.WorkOf(Span(30'000), 1), Span(110'000));
  rule.AddGpuWork(PC::GpuWorkReport::Times(2, At(Start + Period + 30'000), At(Start + (2 * Period) + 10'001)), Margin);
  EXPECT_TRUE(rule.OverlapSeen());
}

TEST(FrameWorkRule, TheApplicationsWordForItsFramesInFlightCountsWhereTheTimesShowNothing)
{
  PC::FrameWorkRule rule;
  AddFrames(rule, 3);
  // How long, not when
  rule.AddGpuWork(PC::GpuWorkReport::OfDuration(1, FP::TimeDuration::FromTicks(60'000)), Margin);
  EXPECT_FALSE(rule.OverlapSeen());
  EXPECT_EQ(rule.WorkOf(Span(50'000), 1), Span(110'000));
  EXPECT_EQ(rule.WorkOf(Span(50'000), 2), Span(60'000));
  EXPECT_EQ(rule.WorkOf(Span(70'000), 2), Span(70'000));
}

TEST(FrameWorkRule, AReportOfTheNewestFrameIsOneAfterTheOtherAsTheNextFrameStartsAfterItsEnd)
{
  PC::FrameWorkRule rule;
  AddFrames(rule, 2);
  rule.AddGpuWork(PC::GpuWorkReport::Times(1, At(Start + 70'000), At(Start + 140'000)), Margin);
  ASSERT_TRUE(rule.OverlapSeen());
  rule.AddGpuWork(PC::GpuWorkReport::Times(2, At(Start + 140'000), At(Start + 190'000)), Margin);
  EXPECT_FALSE(rule.OverlapSeen());
  EXPECT_EQ(rule.WorkOf(Span(30'000), 1), Span(80'000));
}

TEST(FrameWorkRule, OnlyAReportOfAFrameThatStartedAndIsNewerThanTheLastIsTaken)
{
  PC::FrameWorkRule rule;
  AddFrames(rule, 4);
  // No frame, and a frame that has not started
  rule.AddGpuWork(PC::GpuWorkReport::OfDuration(0, FP::TimeDuration::FromTicks(60'000)), Margin);
  rule.AddGpuWork(PC::GpuWorkReport::OfDuration(5, FP::TimeDuration::FromTicks(60'000)), Margin);
  EXPECT_FALSE(rule.HasGpuTime());
  rule.AddGpuWork(PC::GpuWorkReport::OfDuration(3, FP::TimeDuration::FromTicks(60'000)), Margin);
  // The same frame again, and an older one
  rule.AddGpuWork(PC::GpuWorkReport::OfDuration(3, FP::TimeDuration::FromTicks(10'000)), Margin);
  rule.AddGpuWork(PC::GpuWorkReport::OfDuration(2, FP::TimeDuration::FromTicks(10'000)), Margin);
  EXPECT_EQ(rule.GpuTime(), FP::TimeDuration::FromTicks(60'000));
}

TEST(FrameWorkRule, AGpuTimeOfAFrameLongAgoIsNeitherTakenNorUsed)
{
  PC::FrameWorkRule rule;
  AddFrames(rule, PC::FrameWorkRule::FrameCapacity + 1);
  // Frame 1 is as many frames back as are kept: too old
  rule.AddGpuWork(PC::GpuWorkReport::OfDuration(1, FP::TimeDuration::FromTicks(60'000)), Margin);
  EXPECT_FALSE(rule.HasGpuTime());
  rule.AddGpuWork(PC::GpuWorkReport::Times(2, At(Start + Period + 70'000), At(Start + Period + 140'000)), Margin);
  EXPECT_TRUE(rule.HasGpuTime());
  EXPECT_TRUE(rule.OverlapSeen());
  // No report since: once it is that old it is no longer what a frame is judged with
  rule.AddFrameStart(PC::FrameWorkRule::FrameCapacity + 2, At(Start + (17 * Period)));
  EXPECT_FALSE(rule.HasGpuTime());
  EXPECT_FALSE(rule.OverlapSeen());
  EXPECT_EQ(rule.GpuTime(), FP::TimeDuration::Zero());
  EXPECT_EQ(rule.WorkOf(Span(30'000), 1), Span(30'000));
}

TEST(FrameWorkRule, AGpuTimeBeyondAllReasonIsKeptShortSoThatSumsStayInRange)
{
  PC::FrameWorkRule rule;
  AddFrames(rule, 2);
  rule.AddGpuWork(PC::GpuWorkReport::OfDuration(1, FP::TimeDuration::MaxValue()), Margin);
  EXPECT_EQ(rule.GpuTime(), FP::TimeDuration::FromTicks(4'294'967'295));
  EXPECT_EQ(rule.WorkOf(Span(30'000), 1), Span(4'294'997'295));
}

TEST(FrameWorkRule, ClearForgetsTheGpusWorkAndTakesNoReportOfAFrameFromBefore)
{
  PC::FrameWorkRule rule;
  AddFrames(rule, 3);
  rule.AddGpuWork(PC::GpuWorkReport::Times(1, At(Start + 70'000), At(Start + 140'000)), Margin);
  rule.Clear();
  EXPECT_FALSE(rule.HasGpuTime());
  EXPECT_FALSE(rule.OverlapSeen());
  rule.AddGpuWork(PC::GpuWorkReport::Times(3, At(Start + 230'000), At(Start + 260'000)), Margin);
  EXPECT_FALSE(rule.HasGpuTime());
  rule.AddFrameStart(4, At(Start + (3 * Period)));
  rule.AddGpuWork(PC::GpuWorkReport::Times(4, At(Start + 330'000), At(Start + 360'000)), Margin);
  EXPECT_EQ(rule.GpuTime(), FP::TimeDuration::FromTicks(30'000));
}
