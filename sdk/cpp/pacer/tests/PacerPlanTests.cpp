// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The values that go between the pacer and the application each frame (sdk/doc/pacer-design.md "Per frame"): the two plans the
// application carries out, and the reports it gives back. Plain values: what is absent in one is a time of none, a frame id of 0
// or a duration of zero.
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeDuration.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
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
  constexpr FP::TickCount64 At(const int64_t ticks) noexcept
  {
    return FP::TickCount64(ticks);
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
  EXPECT_EQ(plan.WaitForPresentTimeout, FP::TimeDuration::Zero());
}

TEST(PacerPlan, AFrameStartPlanSaysEachOfItsTwoWaits)
{
  PC::FrameStartPlan plan;
  plan.WaitForPresentFrameId = 41;
  plan.WaitForPresentTimeout = FP::TimeDuration::FromTicks(2'500'000);
  EXPECT_TRUE(plan.WaitsForPresent());
  EXPECT_FALSE(plan.WaitsForStartTime());

  PC::FrameStartPlan timed;
  timed.StartTime = At(10'041'667);
  EXPECT_FALSE(timed.WaitsForPresent());
  EXPECT_TRUE(timed.WaitsForStartTime());
}

TEST(PacerPlan, APresentPlanWithNothingInItPresentsAtOnceWithASwapIntervalOfOne)
{
  const PC::PresentPlan plan;

  EXPECT_FALSE(plan.WaitsForPresentTime());
  EXPECT_EQ(plan.FrameId, 0u);
  EXPECT_EQ(plan.SwapInterval, 1u);
  EXPECT_EQ(plan.NotBeforeTime, FP::TickCount64());
  EXPECT_EQ(plan.MinimumDuration, FP::TimeDuration::Zero());
  EXPECT_EQ(plan.CpuBusy, FP::TimeSpan32());

  PC::PresentPlan held;
  held.PresentTime = At(10'027'000);
  EXPECT_TRUE(held.WaitsForPresentTime());
}

TEST(PacerPlan, AGpuWorkReportHasTheTimesOfTheWorkOrItsDurationOnly)
{
  const PC::GpuWorkReport times = PC::GpuWorkReport::Times(7, At(10'001'000), At(10'031'000));
  EXPECT_EQ(times.FrameId, 7u);
  EXPECT_TRUE(times.HasTimes());
  EXPECT_EQ(times.BeginTime, At(10'001'000));
  EXPECT_EQ(times.EndTime, At(10'031'000));
  EXPECT_EQ(times.Duration, FP::TimeDuration::FromTicks(30'000));

  const PC::GpuWorkReport duration = PC::GpuWorkReport::OfDuration(8, FP::TimeDuration::FromTicks(29'500));
  EXPECT_EQ(duration.FrameId, 8u);
  EXPECT_FALSE(duration.HasTimes());
  EXPECT_EQ(duration.Duration, FP::TimeDuration::FromTicks(29'500));

  // An end before the begin is no work
  EXPECT_EQ(PC::GpuWorkReport::Times(9, At(10'031'000), At(10'001'000)).Duration, FP::TimeDuration::Zero());
  // One of the two times alone is not the times of the work
  PC::GpuWorkReport half;
  half.EndTime = At(10'031'000);
  EXPECT_FALSE(half.HasTimes());
  half.EndTime = FP::TickCount64();
  half.BeginTime = At(10'001'000);
  EXPECT_FALSE(half.HasTimes());
  EXPECT_FALSE(PC::GpuWorkReport().HasTimes());
}

TEST(PacerPlan, APresentReportSaysHowLongThePresentHeldTheLoop)
{
  PC::PresentReport report;
  report.FrameId = 3;
  report.CallTime = At(10'000'600);
  report.ReturnTime = At(10'000'606);
  EXPECT_EQ(report.Blocked(), FP::TimeDuration::FromTicks(6));

  // A present that waited for the display
  report.ReturnTime = At(10'041'667);
  EXPECT_EQ(report.Blocked(), FP::TimeDuration::FromTicks(41'067));
  // A return before the call is no wait
  report.ReturnTime = At(10'000'000);
  EXPECT_EQ(report.Blocked(), FP::TimeDuration::Zero());
  EXPECT_EQ(PC::PresentReport().Blocked(), FP::TimeDuration::Zero());
}

TEST(PacerPlan, AVBlankReadingWithoutAPeriodKeepsThePeriodThePacerHas)
{
  const PC::VBlankReading none;
  EXPECT_EQ(none.VBlankTime, FP::TickCount64());
  EXPECT_EQ(none.Period, FP::TimeDuration::Zero());
  EXPECT_EQ(none.ReadTime, FP::TickCount64());

  const PC::VBlankReading reading{At(10'041'664), FP::TimeDuration::FromTicks(41'664), At(10'050'000)};
  EXPECT_EQ(reading.VBlankTime, At(10'041'664));
  EXPECT_EQ(reading.Period.Ticks(), 41'664);
  EXPECT_EQ(reading.ReadTime, At(10'050'000));
}
