// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The wait for the GPU's work on an earlier frame (sdk/doc/pacer-design.md, "How a pacer is put together"): which
// frame a plan asks for with each aim, how long the wait may take, and what a wait that ran out is.

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/frame/FrameStartPlan.hpp>
#include <mb/framepacing/pacer/frame/GpuWaitReport.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/hold/GpuWaitRule.hpp>
#include <gtest/gtest.h>
#include <cstdint>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

namespace
{
  // 100 Hz: a refresh period of exactly 10,000,000 ns
  constexpr int64_t Period = 10'000'000;
  constexpr int64_t Start = 1'000'000'000;

  const PC::RefreshPeriod g_hz100 = PC::RefreshPeriod::FromRate(100);

  PC::PacerSettings Settings(const PC::PacerAim aim, const uint32_t maxFramesInFlight)
  {
    PC::PacerSettings settings(g_hz100);
    settings.SetAim(aim);
    settings.SetMaxFramesInFlight(maxFramesInFlight);
    // The swap intervals alone say how long a wait may take: these tests count in them (the least time has its own tests)
    settings.SetMinWaitTimeout(FP::NanosecondTimeDuration::Zero());
    return settings;
  }

  void Present(PC::GpuWaitRule& rRule, const uint64_t frameId, const bool accepted = true)
  {
    PC::PresentReport report;
    report.FrameId = frameId;
    report.Accepted = accepted;
    rRule.AddPresent(report);
  }

  PC::GpuWaitReport Waited(const uint64_t frameId, const int64_t blockedNanoseconds, const bool done = true)
  {
    PC::GpuWaitReport report;
    report.FrameId = frameId;
    report.BeginTime = FP::NanosecondTickCount(Start);
    report.EndTime = FP::NanosecondTickCount(Start + blockedNanoseconds);
    report.Done = done;
    return report;
  }
}

TEST(GpuWaitRule, TheFrameBeforeWithLowLatencyAndTheOneBeforeThatWithSmoothness)
{
  // One frame in flight with the aim of low latency, whatever the application lets be in flight
  EXPECT_EQ(PC::GpuWaitRule::FramesInFlight(Settings(PC::PacerAim::LowLatency, 1)), 1u);
  EXPECT_EQ(PC::GpuWaitRule::FramesInFlight(Settings(PC::PacerAim::LowLatency, 3)), 1u);
  // Two with smoothness where the application lets two be in flight, and no more than two
  EXPECT_EQ(PC::GpuWaitRule::FramesInFlight(Settings(PC::PacerAim::Smoothness, 1)), 1u);
  EXPECT_EQ(PC::GpuWaitRule::FramesInFlight(Settings(PC::PacerAim::Smoothness, 2)), 2u);
  EXPECT_EQ(PC::GpuWaitRule::FramesInFlight(Settings(PC::PacerAim::Smoothness, 4)), 2u);

  PC::GpuWaitRule lowLatency;
  PC::GpuWaitRule smooth;
  const PC::PacerSettings lowLatencySettings = Settings(PC::PacerAim::LowLatency, 2);
  const PC::PacerSettings smoothSettings = Settings(PC::PacerAim::Smoothness, 2);
  // Before a frame was presented there is nothing to wait for
  EXPECT_FALSE(lowLatency.Plan(lowLatencySettings, g_hz100, 1).WaitsForGpuWork());
  EXPECT_FALSE(smooth.Plan(smoothSettings, g_hz100, 1).WaitsForGpuWork());
  for (uint64_t frameId = 1; frameId <= 10; ++frameId)
  {
    Present(lowLatency, frameId);
    Present(smooth, frameId);
    // The frame that is about to be made is frameId + 1
    const PC::FrameStartPlan plan = lowLatency.Plan(lowLatencySettings, g_hz100, 1);
    ASSERT_EQ(plan.WaitForGpuWorkFrameId, frameId);
    ASSERT_FALSE(plan.WaitsForPresent());
    ASSERT_FALSE(plan.WaitsForStartTime());
    const PC::FrameStartPlan smoothPlan = smooth.Plan(smoothSettings, g_hz100, 1);
    ASSERT_EQ(smoothPlan.WaitForGpuWorkFrameId, frameId - 1u) << frameId;
    lowLatency.BeginFrame();
    smooth.BeginFrame();
  }
}

TEST(GpuWaitRule, AFrameHasOneWaitAndTheWaitMayTakeAFewOfItsSwapIntervals)
{
  PC::GpuWaitRule rule;
  PC::PacerSettings settings = Settings(PC::PacerAim::LowLatency, 1);
  Present(rule, 1);
  const PC::FrameStartPlan plan = rule.Plan(settings, g_hz100, 1);
  ASSERT_EQ(plan.WaitForGpuWorkFrameId, 1u);
  EXPECT_EQ(plan.WaitForGpuWorkTimeout.Nanoseconds(), int64_t{settings.PresentWaitSwapIntervals()} * Period);
  // Counted in the frame's own time
  EXPECT_EQ(rule.Plan(settings, g_hz100, 3).WaitForGpuWorkTimeout.Nanoseconds(), int64_t{settings.PresentWaitSwapIntervals()} * 3 * Period);
  settings.SetPresentWaitSwapIntervals(2);
  EXPECT_EQ(rule.Plan(settings, g_hz100, 1).WaitForGpuWorkTimeout.Nanoseconds(), 2 * Period);
  // And never less than the least time a wait is given
  settings.SetMinWaitTimeout(PC::PacerSettings::DefaultMinWaitTimeout);
  EXPECT_EQ(rule.Plan(settings, g_hz100, 1).WaitForGpuWorkTimeout.Nanoseconds(), 50'000'000);
  EXPECT_EQ(rule.Plan(settings, g_hz100, 3).WaitForGpuWorkTimeout.Nanoseconds(), 6 * Period);
  settings.SetMinWaitTimeout(FP::NanosecondTimeDuration::Zero());

  // Once it is reported the frame is planned again, and no second wait is asked for
  rule.AddGpuWait(Waited(1, 2'000'000), g_hz100);
  EXPECT_FALSE(rule.Plan(settings, g_hz100, 1).WaitsForGpuWork());
  EXPECT_TRUE(rule.HeldTheLoop());
  rule.BeginFrame();
  EXPECT_FALSE(rule.HeldTheLoop());
  // Nor for the same frame before the next one: no new present was made
  EXPECT_FALSE(rule.Plan(settings, g_hz100, 1).WaitsForGpuWork());
  Present(rule, 2);
  EXPECT_EQ(rule.Plan(settings, g_hz100, 1).WaitForGpuWorkFrameId, 2u);
}

TEST(GpuWaitRule, AWaitHeldTheLoopFromAnEighthOfARefreshAndOneThatRanOutIsCounted)
{
  PC::GpuWaitRule rule;
  Present(rule, 1);
  rule.AddGpuWait(Waited(1, (Period / 8) - 1), g_hz100);
  EXPECT_FALSE(rule.HeldTheLoop());
  rule.BeginFrame();
  Present(rule, 2);
  rule.AddGpuWait(Waited(2, Period / 8), g_hz100);
  EXPECT_TRUE(rule.HeldTheLoop());
  EXPECT_EQ(rule.Timeouts(), 0u);
  rule.BeginFrame();
  Present(rule, 3);
  rule.AddGpuWait(Waited(3, 4 * Period, false), g_hz100);
  EXPECT_EQ(rule.Timeouts(), 1u);
  EXPECT_TRUE(rule.HeldTheLoop());
}

TEST(GpuWaitRule, AFrameOfASwapChainThatIsGoneIsNotWaitedFor)
{
  const PC::PacerSettings settings = Settings(PC::PacerAim::Smoothness, 2);
  PC::GpuWaitRule rule;
  Present(rule, 1);
  Present(rule, 2);
  ASSERT_EQ(rule.Plan(settings, g_hz100, 1).WaitForGpuWorkFrameId, 1u);
  // A present the system did not take: the swap chain is gone, with the frames before it
  Present(rule, 3, false);
  EXPECT_FALSE(rule.Plan(settings, g_hz100, 1).WaitsForGpuWork());
  Present(rule, 4);
  EXPECT_FALSE(rule.Plan(settings, g_hz100, 1).WaitsForGpuWork());
  Present(rule, 5);
  EXPECT_EQ(rule.Plan(settings, g_hz100, 1).WaitForGpuWorkFrameId, 4u);

  // The application made the swap chain anew: the same
  rule.ForgetPresents();
  EXPECT_FALSE(rule.Plan(settings, g_hz100, 1).WaitsForGpuWork());
  Present(rule, 6);
  EXPECT_FALSE(rule.Plan(settings, g_hz100, 1).WaitsForGpuWork());
  Present(rule, 7);
  EXPECT_EQ(rule.Plan(settings, g_hz100, 1).WaitForGpuWorkFrameId, 6u);
  rule.AddGpuWait(Waited(6, Period), g_hz100);
  rule.Reset();
  EXPECT_FALSE(rule.HeldTheLoop());
  EXPECT_FALSE(rule.Plan(settings, g_hz100, 1).WaitsForGpuWork());
}
