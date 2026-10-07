// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// A frame's work as two stretches of time: the CPU's and the GPU's, the longer of the two where they lie side by side and the two
// added where one follows the other.
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/frame/GpuWorkReport.hpp>
#include <mb/framepacing/pacer/rule/FrameWorkRule.hpp>
#include <gtest/gtest.h>
#include <cstdint>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

namespace
{
  constexpr int64_t Period = 10'000'000;
  constexpr int64_t Start = 1'000'000'000;
  constexpr FP::NanosecondTimeSpan Margin(1'000'000);

  constexpr FP::NanosecondTickCount At(const int64_t nanoseconds) noexcept
  {
    return FP::NanosecondTickCount(nanoseconds);
  }

  constexpr FP::NanosecondTimeSpan Span(const int64_t nanoseconds) noexcept
  {
    return FP::NanosecondTimeSpan(nanoseconds);
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
  EXPECT_EQ(rule.GpuTime(), FP::NanosecondTimeDuration::Zero());
  EXPECT_EQ(rule.WorkOf(Span(3'000'000), 1), Span(3'000'000));
  EXPECT_EQ(rule.WorkOf(Span(3'000'000), 2), Span(3'000'000));
}

TEST(FrameWorkRule, OneAfterTheOtherTheTwoAreAdded)
{
  PC::FrameWorkRule rule;
  AddFrames(rule, 3);
  // The GPU was done with frame 1 before frame 2 began
  rule.AddGpuWork(PC::GpuWorkReport::Times(1, At(Start + 3'000'000), At(Start + 9'000'000)), Margin);
  EXPECT_TRUE(rule.HasGpuTime());
  EXPECT_FALSE(rule.OverlapSeen());
  EXPECT_EQ(rule.GpuTime(), FP::NanosecondTimeDuration::FromNanoseconds(6'000'000));
  EXPECT_EQ(rule.WorkOf(Span(3'000'000), 1), Span(9'000'000));
}

TEST(FrameWorkRule, SideBySideItIsTheLongerOfTheTwo)
{
  PC::FrameWorkRule rule;
  AddFrames(rule, 3);
  // The GPU was on frame 1 until 0.4 periods after frame 2 began
  rule.AddGpuWork(PC::GpuWorkReport::Times(1, At(Start + 7'000'000), At(Start + 14'000'000)), Margin);
  EXPECT_TRUE(rule.OverlapSeen());
  EXPECT_EQ(rule.WorkOf(Span(3'000'000), 1), Span(7'000'000));
  EXPECT_EQ(rule.WorkOf(Span(8'000'000), 1), Span(8'000'000));
  // An end and a duration say as much
  rule.AddGpuWork(PC::GpuWorkReport::EndAndDuration(2, At(Start + 25'000'000), FP::NanosecondTimeDuration::FromNanoseconds(7'500'000)), Margin);
  EXPECT_TRUE(rule.OverlapSeen());
  EXPECT_EQ(rule.WorkOf(Span(3'000'000), 1), Span(7'500'000));
}

TEST(FrameWorkRule, AFrameThatBeganWithinTheMarginOfTheEndDidNotBeginBeforeIt)
{
  PC::FrameWorkRule rule;
  AddFrames(rule, 3);
  rule.AddGpuWork(PC::GpuWorkReport::Times(1, At(Start + 3'000'000), At(Start + Period + 1'000'000)), Margin);
  EXPECT_FALSE(rule.OverlapSeen());
  EXPECT_EQ(rule.WorkOf(Span(3'000'000), 1), Span(11'000'000));
  rule.AddGpuWork(PC::GpuWorkReport::Times(2, At(Start + Period + 3'000'000), At(Start + (2 * Period) + 1'000'100)), Margin);
  EXPECT_TRUE(rule.OverlapSeen());
}

TEST(FrameWorkRule, TheApplicationsWordForItsFramesInFlightCountsWhereTheTimesShowNothing)
{
  PC::FrameWorkRule rule;
  AddFrames(rule, 3);
  // How long, not when
  rule.AddGpuWork(PC::GpuWorkReport::OfDuration(1, FP::NanosecondTimeDuration::FromNanoseconds(6'000'000)), Margin);
  EXPECT_FALSE(rule.OverlapSeen());
  EXPECT_EQ(rule.WorkOf(Span(5'000'000), 1), Span(11'000'000));
  EXPECT_EQ(rule.WorkOf(Span(5'000'000), 2), Span(6'000'000));
  EXPECT_EQ(rule.WorkOf(Span(7'000'000), 2), Span(7'000'000));
}

TEST(FrameWorkRule, AReportOfTheNewestFrameIsOneAfterTheOtherAsTheNextFrameStartsAfterItsEnd)
{
  PC::FrameWorkRule rule;
  AddFrames(rule, 2);
  rule.AddGpuWork(PC::GpuWorkReport::Times(1, At(Start + 7'000'000), At(Start + 14'000'000)), Margin);
  ASSERT_TRUE(rule.OverlapSeen());
  rule.AddGpuWork(PC::GpuWorkReport::Times(2, At(Start + 14'000'000), At(Start + 19'000'000)), Margin);
  EXPECT_FALSE(rule.OverlapSeen());
  EXPECT_EQ(rule.WorkOf(Span(3'000'000), 1), Span(8'000'000));
}

TEST(FrameWorkRule, OnlyAReportOfAFrameThatStartedAndIsNotOlderThanTheLastIsTaken)
{
  PC::FrameWorkRule rule;
  AddFrames(rule, 4);
  // No frame, and a frame that has not started
  rule.AddGpuWork(PC::GpuWorkReport::OfDuration(0, FP::NanosecondTimeDuration::FromNanoseconds(6'000'000)), Margin);
  rule.AddGpuWork(PC::GpuWorkReport::OfDuration(5, FP::NanosecondTimeDuration::FromNanoseconds(6'000'000)), Margin);
  EXPECT_FALSE(rule.HasGpuTime());
  rule.AddGpuWork(PC::GpuWorkReport::OfDuration(3, FP::NanosecondTimeDuration::FromNanoseconds(6'000'000)), Margin);
  // An older frame
  rule.AddGpuWork(PC::GpuWorkReport::OfDuration(2, FP::NanosecondTimeDuration::FromNanoseconds(1'000'000)), Margin);
  EXPECT_EQ(rule.GpuTime(), FP::NanosecondTimeDuration::FromNanoseconds(6'000'000));
}

TEST(FrameWorkRule, ALaterReportForTheSameFrameTakesThePlaceOfTheFirst)
{
  // An application that learns how long the GPU worked before it learns when
  PC::FrameWorkRule rule;
  AddFrames(rule, 3);
  rule.AddGpuWork(PC::GpuWorkReport::OfDuration(1, FP::NanosecondTimeDuration::FromNanoseconds(7'000'000)), Margin);
  EXPECT_FALSE(rule.OverlapSeen());
  EXPECT_EQ(rule.WorkOf(Span(3'000'000), 1), Span(10'000'000));
  rule.AddGpuWork(PC::GpuWorkReport::Times(1, At(Start + 7'000'000), At(Start + 14'000'000)), Margin);
  EXPECT_TRUE(rule.OverlapSeen());
  EXPECT_EQ(rule.WorkOf(Span(3'000'000), 1), Span(7'000'000));
}

TEST(FrameWorkRule, AGpuTimeOfAFrameLongAgoIsNeitherTakenNorUsed)
{
  PC::FrameWorkRule rule;
  AddFrames(rule, PC::FrameWorkRule::FrameCapacity + 1);
  // Frame 1 is as many frames back as are kept: too old
  rule.AddGpuWork(PC::GpuWorkReport::OfDuration(1, FP::NanosecondTimeDuration::FromNanoseconds(6'000'000)), Margin);
  EXPECT_FALSE(rule.HasGpuTime());
  rule.AddGpuWork(PC::GpuWorkReport::Times(2, At(Start + Period + 7'000'000), At(Start + Period + 14'000'000)), Margin);
  EXPECT_TRUE(rule.HasGpuTime());
  EXPECT_TRUE(rule.OverlapSeen());
  // No report since: once it is that old it is no longer what a frame is judged with
  rule.AddFrameStart(PC::FrameWorkRule::FrameCapacity + 2, At(Start + (17 * Period)));
  EXPECT_FALSE(rule.HasGpuTime());
  EXPECT_FALSE(rule.OverlapSeen());
  EXPECT_EQ(rule.GpuTime(), FP::NanosecondTimeDuration::Zero());
  EXPECT_EQ(rule.WorkOf(Span(3'000'000), 1), Span(3'000'000));
}

TEST(FrameWorkRule, AGpuTimeBeyondAllReasonIsKeptShortSoThatSumsStayInRange)
{
  PC::FrameWorkRule rule;
  AddFrames(rule, 2);
  rule.AddGpuWork(PC::GpuWorkReport::OfDuration(1, FP::NanosecondTimeDuration::MaxValue()), Margin);
  EXPECT_EQ(rule.GpuTime(), FP::NanosecondTimeDuration::FromNanoseconds(4'294'967'295));
  EXPECT_EQ(rule.WorkOf(Span(3'000'000), 1), Span(4'297'967'295));
}

TEST(FrameWorkRule, ClearForgetsTheGpusWorkAndTakesNoReportOfAFrameFromBefore)
{
  PC::FrameWorkRule rule;
  AddFrames(rule, 3);
  rule.AddGpuWork(PC::GpuWorkReport::Times(1, At(Start + 7'000'000), At(Start + 14'000'000)), Margin);
  rule.Clear();
  EXPECT_FALSE(rule.HasGpuTime());
  EXPECT_FALSE(rule.OverlapSeen());
  rule.AddGpuWork(PC::GpuWorkReport::Times(3, At(Start + 23'000'000), At(Start + 26'000'000)), Margin);
  EXPECT_FALSE(rule.HasGpuTime());
  rule.AddFrameStart(4, At(Start + (3 * Period)));
  rule.AddGpuWork(PC::GpuWorkReport::Times(4, At(Start + 33'000'000), At(Start + 36'000'000)), Margin);
  EXPECT_EQ(rule.GpuTime(), FP::NanosecondTimeDuration::FromNanoseconds(3'000'000));
}
