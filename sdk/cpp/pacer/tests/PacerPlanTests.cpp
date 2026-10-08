// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The values that go between the pacer and the application each frame (sdk/doc/pacer-design.md "Per frame"): the two plans the
// application carries out, and the reports it gives back. Plain values: what is absent in one is a time of none, a frame id of 0
// or a duration of zero.
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/frame/FrameStartPlan.hpp>
#include <mb/framepacing/pacer/frame/GpuWorkReport.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/frame/VBlankReading.hpp>
#include <gtest/gtest.h>
#include <type_traits>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

namespace
{
  constexpr FP::NanosecondTickCount At(const int64_t nanoseconds) noexcept
  {
    return FP::NanosecondTickCount(nanoseconds);
  }

  // Values an application copies around freely
  static_assert(std::is_trivially_copyable_v<PC::FrameStartPlan>);
  static_assert(std::is_trivially_copyable_v<PC::PresentPlan>);
  static_assert(std::is_trivially_copyable_v<PC::VBlankReading>);
  static_assert(std::is_trivially_copyable_v<PC::GpuWorkReport>);
  static_assert(std::is_trivially_copyable_v<PC::PresentReport>);
}

TEST(PacerPlan, AFrameStartPlanWithNothingInItAsksForNoWait)
{
  const PC::FrameStartPlan plan;

  EXPECT_FALSE(plan.WaitsForPresent());
  EXPECT_FALSE(plan.WaitsForStartTime());
  EXPECT_EQ(plan.WaitForPresentFrameId, 0u);
  EXPECT_EQ(plan.WaitForPresentTimeout, FP::NanosecondTimeDuration::Zero());
}

TEST(PacerPlan, AFrameStartPlanSaysEachOfItsTwoWaits)
{
  PC::FrameStartPlan plan;
  plan.WaitForPresentFrameId = 41;
  plan.WaitForPresentTimeout = FP::NanosecondTimeDuration::FromNanoseconds(250'000'000);
  EXPECT_TRUE(plan.WaitsForPresent());
  EXPECT_FALSE(plan.WaitsForStartTime());

  PC::FrameStartPlan timed;
  timed.StartTime = At(1'004'166'667);
  EXPECT_FALSE(timed.WaitsForPresent());
  EXPECT_TRUE(timed.WaitsForStartTime());
}

TEST(PacerPlan, APresentPlanWithNothingInItPresentsAtOnceWithASwapIntervalOfOne)
{
  const PC::PresentPlan plan;

  EXPECT_FALSE(plan.WaitsForPresentTime());
  EXPECT_EQ(plan.FrameId, 0u);
  EXPECT_EQ(plan.SwapInterval, 1u);
  EXPECT_EQ(plan.NotBeforeTime, FP::NanosecondTickCount());
  EXPECT_EQ(plan.MinimumDuration, FP::NanosecondTimeDuration::Zero());
  EXPECT_EQ(plan.CpuBusy, FP::NanosecondTimeDuration());

  PC::PresentPlan held;
  held.PresentTime = At(1'002'700'000);
  EXPECT_TRUE(held.WaitsForPresentTime());
}

TEST(PacerPlan, AGpuWorkReportHasTheTimesOfTheWorkOrItsDurationOnly)
{
  const PC::GpuWorkReport times = PC::GpuWorkReport::Times(7, At(1'000'100'000), At(1'003'100'000));
  EXPECT_EQ(times.FrameId, 7u);
  EXPECT_TRUE(times.HasTimes());
  EXPECT_EQ(times.BeginTime, At(1'000'100'000));
  EXPECT_EQ(times.EndTime, At(1'003'100'000));
  EXPECT_EQ(times.Duration, FP::NanosecondTimeDuration::FromNanoseconds(3'000'000));

  const PC::GpuWorkReport duration = PC::GpuWorkReport::OfDuration(8, FP::NanosecondTimeDuration::FromNanoseconds(2'950'000));
  EXPECT_EQ(duration.FrameId, 8u);
  EXPECT_FALSE(duration.HasTimes());
  EXPECT_EQ(duration.Duration, FP::NanosecondTimeDuration::FromNanoseconds(2'950'000));

  // Where a platform gives the end and an elapsed time: the end is known, the begin is not, and it is not worked out
  const PC::GpuWorkReport ended = PC::GpuWorkReport::EndAndDuration(10, At(1'003'100'000), FP::NanosecondTimeDuration::FromNanoseconds(2'900'000));
  EXPECT_EQ(ended.FrameId, 10u);
  EXPECT_FALSE(ended.HasTimes());
  EXPECT_TRUE(ended.HasEndTime());
  EXPECT_EQ(ended.BeginTime, FP::NanosecondTickCount());
  EXPECT_EQ(ended.EndTime, At(1'003'100'000));
  EXPECT_EQ(ended.Duration, FP::NanosecondTimeDuration::FromNanoseconds(2'900'000));
  EXPECT_TRUE(times.HasEndTime());
  EXPECT_FALSE(duration.HasEndTime());

  // An end before the begin is no work
  EXPECT_EQ(PC::GpuWorkReport::Times(9, At(1'003'100'000), At(1'000'100'000)).Duration, FP::NanosecondTimeDuration::Zero());
  // One of the two times alone is not the times of the work
  PC::GpuWorkReport half;
  half.EndTime = At(1'003'100'000);
  EXPECT_FALSE(half.HasTimes());
  half.EndTime = FP::NanosecondTickCount();
  half.BeginTime = At(1'000'100'000);
  EXPECT_FALSE(half.HasTimes());
  EXPECT_FALSE(PC::GpuWorkReport().HasTimes());
}

TEST(PacerPlan, APresentReportSaysHowLongThePresentHeldTheLoop)
{
  PC::PresentReport report;
  report.FrameId = 3;
  report.CallTime = At(1'000'060'000);
  report.ReturnTime = At(1'000'060'600);
  EXPECT_EQ(report.Blocked(), FP::NanosecondTimeDuration::FromNanoseconds(600));

  // A present that waited for the display
  report.ReturnTime = At(1'004'166'667);
  EXPECT_EQ(report.Blocked(), FP::NanosecondTimeDuration::FromNanoseconds(4'106'667));
  // A return before the call is no wait
  report.ReturnTime = At(1'000'000'000);
  EXPECT_EQ(report.Blocked(), FP::NanosecondTimeDuration::Zero());
  EXPECT_EQ(PC::PresentReport().Blocked(), FP::NanosecondTimeDuration::Zero());
  // A present is taken by the system unless the report says it was not
  EXPECT_TRUE(PC::PresentReport().Accepted);
  report.Accepted = false;
  EXPECT_FALSE(report.Accepted);
}

TEST(PacerPlan, AVBlankReadingWithoutAPeriodKeepsThePeriodThePacerHas)
{
  const PC::VBlankReading none;
  EXPECT_EQ(none.VBlankTime, FP::NanosecondTickCount());
  EXPECT_EQ(none.Period, FP::NanosecondTimeDuration::Zero());
  EXPECT_EQ(none.ReadTime, FP::NanosecondTickCount());

  const PC::VBlankReading reading{At(1'004'166'400), FP::NanosecondTimeDuration::FromNanoseconds(4'166'400), At(1'005'000'000)};
  EXPECT_EQ(reading.VBlankTime, At(1'004'166'400));
  EXPECT_EQ(reading.Period.Nanoseconds(), 4'166'400);
  EXPECT_EQ(reading.ReadTime, At(1'005'000'000));
}
