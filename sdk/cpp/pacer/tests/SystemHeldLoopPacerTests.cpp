// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The third major tier, "3": the system holds the loop while the swap chain's queue is full (decision 24 of
// sdk/doc/pacer-design.md). No time to wait for while it holds, a fixed step, the hold checked by rate against the frames' starts,
// and the pacer's own limit where it does not hold, all inside the one tier.
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/capability/PacerCapabilities.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <mb/framepacing/pacer/capability/PacerTier.hpp>
#include <mb/framepacing/pacer/capability/PacerTierUtil.hpp>
#include <mb/framepacing/pacer/display/DisplayReport.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/frame/FrameStartPlan.hpp>
#include <mb/framepacing/pacer/frame/GpuWaitReport.hpp>
#include <mb/framepacing/pacer/frame/GpuWorkReport.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/frame/PresentWaitReport.hpp>
#include <mb/framepacing/pacer/frame/SystemWaitKind.hpp>
#include <mb/framepacing/pacer/frame/SystemWaitReport.hpp>
#include <mb/framepacing/pacer/tier/SystemHeldLoopPacer.hpp>
#include <mb/framepacing/pacer/tier/SystemHoldState.hpp>
#include <mb/framepacing/pacer/tier/TierPacer.hpp>
#include <gtest/gtest.h>
#include <algorithm>
#include <cstdint>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;
using PC::PacerCapabilities;
using PC::PacerCapability;
using PC::SystemHoldState;

namespace
{
  // 100 Hz: a refresh period of exactly 10,000,000 ns
  constexpr int64_t Period = 10'000'000;
  // The limit while the system does not hold the loop: an eighth of a refresh faster than the display
  constexpr int64_t Probe = Period - (Period / PC::SystemHeldLoopPacer::ProbeDivisor);
  constexpr int64_t Start = 1'000'000'000;
  constexpr int64_t Work = 3'000'000;
  constexpr PacerCapability Held = PacerCapability::AcquireWaits;

  const PC::RefreshPeriod g_hz100 = PC::RefreshPeriod::FromRate(100);

  constexpr FP::NanosecondTickCount At(const int64_t nanoseconds) noexcept
  {
    return FP::NanosecondTickCount(nanoseconds);
  }

  constexpr FP::NanosecondTimeSpan Span(const int64_t nanoseconds) noexcept
  {
    return FP::NanosecondTimeSpan(nanoseconds);
  }

  //! Three images and one frame in flight: the queue holds four frames
  PC::PacerSettings Settings(const PC::PacerAim aim = PC::PacerAim::Smoothness)
  {
    PC::PacerSettings settings(g_hz100);
    settings.SetAim(aim);
    settings.SetSwapChainImages(3);
    return settings;
  }

  //! An application's frame loop on the pacer. Frame(start) begins a frame where the application got through the system's hold
  //! (or at the time the plan gives, where that is later), works, and presents; the present returns at once
  struct Loop
  {
    PC::TierPacer Pacer;
    int64_t Now{Start};
    PC::FrameStartPlan Plan;
    PC::PresentPlan Present;
    int64_t StartNanoseconds{0};

    Loop(const PC::PacerSettings& settings, const PacerCapabilities& capabilities)
      : Pacer(settings, capabilities)
    {
    }

    //! The frame starts at startNanoseconds, or at the plan's time if that is later. holdNanoseconds: the system's wait that
    //! ended at the start, reported as a wait for an image
    PC::FrameSchedule Frame(const int64_t startNanoseconds, const int64_t holdNanoseconds = 0, const int64_t workNanoseconds = Work)
    {
      Plan = Pacer.PlanFrame(At(Now));
      StartNanoseconds = std::max(startNanoseconds, Plan.WaitsForStartTime() ? Plan.StartTime.Nanoseconds() : Now);
      if (holdNanoseconds > 0)
      {
        Pacer.AddSystemWait({PC::SystemWaitKind::Acquire, At(StartNanoseconds - holdNanoseconds), At(StartNanoseconds)});
      }
      const PC::FrameSchedule schedule = Pacer.BeginFrame(At(StartNanoseconds));
      Present = Pacer.EndFrame(At(StartNanoseconds + workNanoseconds));
      PC::PresentReport report;
      report.FrameId = Present.FrameId;
      report.CallTime = At(StartNanoseconds + workNanoseconds);
      report.ReturnTime = At(StartNanoseconds + workNanoseconds + 60'000);
      Pacer.AddPresent(report);
      Now = StartNanoseconds + workNanoseconds + 100'000;
      return schedule;
    }

    //! frames frames a refresh period apart from the last start, as the system's hold lets them through
    void HeldFrames(const int32_t frames, const int64_t apart = Period)
    {
      for (int32_t frame = 0; frame < frames; ++frame)
      {
        static_cast<void>(Frame(StartNanoseconds + apart, apart - Work - 100'000));
      }
    }
  };
}

TEST(SystemHeldLoopPacer, WhileTheSystemHoldsTheLoopThereIsNothingToWaitForAndEveryStepIsTheSwapInterval)
{
  Loop loop(Settings(), PacerCapabilities(Held));
  EXPECT_EQ(loop.Pacer.Rating().Tier, PC::PacerTier::SystemHoldsLoop);
  EXPECT_EQ(loop.Pacer.WorkingTier(), PC::PacerTier::SystemHoldsLoop);
  static_cast<void>(loop.Frame(Start));
  for (int32_t frame = 0; frame < 200; ++frame)
  {
    const int64_t start = loop.StartNanoseconds + Period;
    const PC::FrameSchedule schedule = loop.Frame(start, Period - Work - 100'000);
    ASSERT_FALSE(loop.Plan.WaitsForStartTime()) << frame;
    ASSERT_FALSE(loop.Plan.WaitsForPresent()) << frame;
    ASSERT_EQ(loop.StartNanoseconds, start) << frame;
    ASSERT_EQ(schedule.AnimationStep, Span(Period)) << frame;
    ASSERT_EQ(schedule.SwapInterval, 1u) << frame;
    ASSERT_TRUE(schedule.Paced) << frame;
    // When the next frame starts and when this one is shown are the system's
    ASSERT_EQ(schedule.NextFrameStartTime, FP::NanosecondTickCount()) << frame;
    ASSERT_EQ(schedule.IntendedDisplayTime, FP::NanosecondTickCount()) << frame;
    ASSERT_EQ(schedule.TargetFrameTime.Nanoseconds(), Period) << frame;
    ASSERT_FALSE(loop.Present.WaitsForPresentTime()) << frame;
  }
  EXPECT_EQ(loop.Pacer.SystemHold(), SystemHoldState::Holding);
  EXPECT_EQ(loop.Pacer.SystemHoldLosses(), 0u);
  EXPECT_EQ(loop.Pacer.SystemLimitedFrames(), 0u);
  EXPECT_EQ(loop.Pacer.FrameWindow().LateFrames, 0u);
  EXPECT_EQ(loop.Pacer.RefreshesBehindClock(), 0u);
  EXPECT_FALSE(loop.Pacer.PresentWaitsStopped());
}

TEST(SystemHeldLoopPacer, ALongFrameTheQueueCoversCostsNothing)
{
  Loop loop(Settings(), PacerCapabilities(Held));
  static_cast<void>(loop.Frame(Start));
  loop.HeldFrames(50);
  // A frame of 2.5 refreshes of work: the frames that wait cover it, and the ones after it start at once to fill the queue again
  const int64_t longStart = loop.StartNanoseconds + Period;
  static_cast<void>(loop.Frame(longStart, Period - Work - 100'000, 25'000'000));
  const PC::FrameSchedule after = loop.Frame(longStart + 25'200'000);
  EXPECT_EQ(after.AnimationStep, Span(Period));
  static_cast<void>(loop.Frame(loop.StartNanoseconds + 3'200'000));
  loop.HeldFrames(50);
  EXPECT_EQ(loop.Pacer.SystemHold(), SystemHoldState::Holding);
  EXPECT_EQ(loop.Pacer.SystemHoldLosses(), 0u);
  // The frame was late by its work, which the rule counts
  EXPECT_EQ(loop.Pacer.FrameWindow().LateFrames, 1u);
}

TEST(SystemHeldLoopPacer, FramesThatStartFasterThanTheDisplayCanShowThemSayTheSystemDoesNotHold)
{
  // The queue holds four frames (three images, one in flight). With it full, the fourth frame that starts back to back after a
  // frame the system held is more than it can take
  Loop loop(Settings(), PacerCapabilities(Held));
  static_cast<void>(loop.Frame(Start));
  loop.HeldFrames(100);
  ASSERT_EQ(loop.Pacer.SystemHold(), SystemHoldState::Holding);
  // A window that is covered: the acquire and the present return at once
  int32_t started = 0;
  while (loop.Pacer.SystemHold() == SystemHoldState::Holding && started < 20)
  {
    static_cast<void>(loop.Frame(loop.StartNanoseconds + 200'000, 0, 50'000));
    ++started;
  }
  EXPECT_EQ(started, 4);
  EXPECT_EQ(loop.Pacer.SystemHold(), SystemHoldState::NotHolding);
  EXPECT_EQ(loop.Pacer.SystemHoldLosses(), 1u);
  // The tier stays the tier
  EXPECT_EQ(loop.Pacer.WorkingTier(), PC::PacerTier::SystemHoldsLoop);

  // The pacer limits the frame time, counted from the frame where the hold was found missing, an eighth of a refresh faster
  // than the display; the schedule says when the next frame starts
  for (int32_t frame = 0; frame < 30; ++frame)
  {
    const int64_t before = loop.StartNanoseconds;
    const PC::FrameSchedule schedule = loop.Frame(loop.Now, 0, 50'000);
    ASSERT_TRUE(loop.Plan.WaitsForStartTime()) << frame;
    ASSERT_EQ(loop.StartNanoseconds, before + Probe) << frame;
    ASSERT_EQ(schedule.NextFrameStartTime, At(loop.StartNanoseconds + Probe)) << frame;
    ASSERT_EQ(schedule.AnimationStep, Span(Period)) << frame;
  }
  EXPECT_EQ(loop.Pacer.SystemLimitedFrames(), 30u);
  EXPECT_EQ(loop.Pacer.SystemHold(), SystemHoldState::NotHolding);

  // Frames in a row that start no sooner than the display can show them, though the limit would let them start sooner, say
  // that the system holds the loop again; one that starts at the limit breaks the run
  for (int32_t frame = 0; frame < 7; ++frame)
  {
    static_cast<void>(loop.Frame(loop.StartNanoseconds + Period, Period / 2));
    ASSERT_EQ(loop.Pacer.SystemHold(), SystemHoldState::NotHolding) << frame;
  }
  static_cast<void>(loop.Frame(loop.Now, Period / 2));
  for (int32_t frame = 0; frame < 7; ++frame)
  {
    static_cast<void>(loop.Frame(loop.StartNanoseconds + Period - (Period / 100), Period / 2));
  }
  EXPECT_EQ(loop.Pacer.SystemHold(), SystemHoldState::NotHolding);
  static_cast<void>(loop.Frame(loop.StartNanoseconds + Period, Period / 2));
  EXPECT_EQ(loop.Pacer.SystemHold(), SystemHoldState::Holding);
  // Holding again: nothing to wait for
  static_cast<void>(loop.Frame(loop.StartNanoseconds + Period, Period / 2));
  EXPECT_FALSE(loop.Plan.WaitsForStartTime());
  loop.HeldFrames(100);
  EXPECT_EQ(loop.Pacer.SystemHold(), SystemHoldState::Holding);
  EXPECT_EQ(loop.Pacer.SystemHoldLosses(), 1u);
}

TEST(SystemHeldLoopPacer, AHoldThatLastsAShareOfARefreshStaysLimited)
{
  // Measured on a virtual machine: the present waited about a third of a refresh, and the loop ran at more than twice the rate.
  // The rate gives it away, and the limit holds the frames a refresh apart; the present's wait ends before the limit
  Loop loop(Settings(), PacerCapabilities(Held));
  static_cast<void>(loop.Frame(Start));
  for (int32_t frame = 0; frame < 300; ++frame)
  {
    loop.Plan = loop.Pacer.PlanFrame(At(loop.Now));
    const int64_t start = loop.Plan.WaitsForStartTime() ? std::max(loop.Now, loop.Plan.StartTime.Nanoseconds()) : loop.Now;
    static_cast<void>(loop.Pacer.BeginFrame(At(start)));
    loop.StartNanoseconds = start;
    loop.Present = loop.Pacer.EndFrame(At(start + Work));
    PC::PresentReport report;
    report.FrameId = loop.Present.FrameId;
    report.CallTime = At(start + Work);
    report.ReturnTime = At(start + Work + (36 * Period / 100));
    loop.Pacer.AddPresent(report);
    loop.Now = report.ReturnTime.Nanoseconds() + 100'000;
  }
  EXPECT_EQ(loop.Pacer.SystemHold(), SystemHoldState::NotHolding);
  EXPECT_EQ(loop.Pacer.SystemHoldLosses(), 1u);
  EXPECT_GT(loop.Pacer.SystemLimitedFrames(), 280u);
}

TEST(SystemHeldLoopPacer, ADisplayALittleOffTheToldPeriodNeverTripsIt)
{
  for (const int64_t apart : {Period - (Period / 500), Period + (Period / 500)})
  {
    Loop loop(Settings(), PacerCapabilities(Held));
    static_cast<void>(loop.Frame(Start));
    loop.HeldFrames(5'000, apart);
    EXPECT_EQ(loop.Pacer.SystemHold(), SystemHoldState::Holding) << apart;
    EXPECT_EQ(loop.Pacer.SystemHoldLosses(), 0u) << apart;
  }
  // Without the image count the queue is taken to hold four images and the frames in flight
  PC::PacerSettings unknown = Settings();
  unknown.SetSwapChainImages(0);
  Loop loop(unknown, PacerCapabilities(Held));
  static_cast<void>(loop.Frame(Start));
  loop.HeldFrames(10);
  int32_t started = 0;
  while (loop.Pacer.SystemHold() == SystemHoldState::Holding && started < 20)
  {
    static_cast<void>(loop.Frame(loop.StartNanoseconds + 200'000, 0, 50'000));
    ++started;
  }
  EXPECT_EQ(started, static_cast<int32_t>(PC::SystemHeldLoopPacer::UnknownSwapChainImages) + 1);
}

TEST(SystemHeldLoopPacer, ASwapIntervalThePresentCanNotGiveIsHeldByThePacer)
{
  // Half rate, and a present that takes no swap interval (a Vulkan FIFO swap chain): the system holds a frame for one refresh,
  // and the pacer the rest
  PC::PacerSettings halfRate = Settings();
  halfRate.SetPreferredSwapInterval(2);
  Loop limited(halfRate, PacerCapabilities(Held));
  static_cast<void>(limited.Frame(Start));
  EXPECT_EQ(limited.Pacer.SystemHold(), SystemHoldState::Limited);
  for (int32_t frame = 0; frame < 20; ++frame)
  {
    const int64_t before = limited.StartNanoseconds;
    const PC::FrameSchedule schedule = limited.Frame(limited.Now, 0);
    ASSERT_EQ(limited.StartNanoseconds - before, 2 * Period) << frame;
    ASSERT_EQ(schedule.AnimationStep, Span(2 * Period)) << frame;
    ASSERT_EQ(limited.Present.SwapInterval, 1u) << frame;
  }
  EXPECT_EQ(limited.Pacer.SystemLimitedFrames(), 20u);
  EXPECT_EQ(limited.Pacer.SystemHoldLosses(), 0u);

  // A present that takes swap intervals up to four holds it: nothing to wait for, and the present is given two
  Loop swapInterval(halfRate, PacerCapabilities(Held | PacerCapability::PresentSwapInterval, 4));
  static_cast<void>(swapInterval.Frame(Start));
  EXPECT_EQ(swapInterval.Pacer.SystemHold(), SystemHoldState::Holding);
  for (int32_t frame = 0; frame < 20; ++frame)
  {
    static_cast<void>(swapInterval.Frame(swapInterval.StartNanoseconds + (2 * Period), (2 * Period) - Work - 100'000));
    ASSERT_FALSE(swapInterval.Plan.WaitsForStartTime()) << frame;
    ASSERT_EQ(swapInterval.Present.SwapInterval, 2u) << frame;
  }

  // A present whose shortest swap interval is 2: the pacer runs at it, though every refresh is preferred
  Loop floor(Settings(), PacerCapabilities(Held | PacerCapability::PresentSwapInterval, 2, 2));
  const PC::FrameSchedule first = floor.Frame(Start);
  EXPECT_EQ(first.SwapInterval, 2u);
  EXPECT_EQ(first.PreferredFrameTime.Nanoseconds(), 2 * Period);
  EXPECT_EQ(floor.Pacer.SystemHold(), SystemHoldState::Holding);
}

TEST(SystemHeldLoopPacer, TheAimOfLowLatencyIsPacedAsSmoothness)
{
  Loop smooth(Settings(PC::PacerAim::Smoothness), PacerCapabilities(Held));
  Loop low(Settings(PC::PacerAim::LowLatency), PacerCapabilities(Held));
  static_cast<void>(smooth.Frame(Start));
  static_cast<void>(low.Frame(Start));
  for (int32_t frame = 0; frame < 100; ++frame)
  {
    const int64_t start = smooth.StartNanoseconds + ((frame % 10) == 0 ? 2 * Period : Period);
    const PC::FrameSchedule a = smooth.Frame(start, Period / 2);
    const PC::FrameSchedule b = low.Frame(start, Period / 2);
    ASSERT_EQ(a.AnimationTime, b.AnimationTime) << frame;
    ASSERT_EQ(smooth.Plan.WaitsForStartTime(), low.Plan.WaitsForStartTime()) << frame;
  }
  EXPECT_FALSE(PC::PacerTierUtil::HasLowLatencyAim(low.Pacer.WorkingTier()));
}

TEST(SystemHeldLoopPacer, OnlyTheActiveSetChangesTheTier)
{
  Loop loop(Settings(), PacerCapabilities(Held | PacerCapability::VBlankTimes | PacerCapability::WaitForPresent));
  static_cast<void>(loop.Frame(Start));
  loop.HeldFrames(20);
  EXPECT_EQ(loop.Pacer.WorkingTier(), PC::PacerTier::SystemHoldsLoop);
  // No wait for a present is asked for, and one reported is not taken
  EXPECT_FALSE(loop.Plan.WaitsForPresent());
  PC::PresentWaitReport wait;
  wait.FrameId = 1;
  wait.BeginTime = At(loop.Now);
  wait.EndTime = At(loop.Now + 1'000);
  loop.Pacer.AddPresentWait(wait);
  PC::GpuWaitReport gpuWait;
  gpuWait.FrameId = 1;
  loop.Pacer.AddGpuWait(gpuWait);
  // The same while the frames are limited: the tier stays
  for (int32_t frame = 0; frame < 6; ++frame)
  {
    static_cast<void>(loop.Frame(loop.StartNanoseconds + 200'000, 0, 50'000));
  }
  ASSERT_EQ(loop.Pacer.SystemHold(), SystemHoldState::NotHolding);
  EXPECT_EQ(loop.Pacer.WorkingTier(), PC::PacerTier::SystemHoldsLoop);
  // The application takes the system's hold out of its active set: the frame loop's tier, and its first frame at the limit
  const PC::FrameSchedule before = loop.Frame(loop.Now, 0, 50'000);
  loop.Pacer.SetActiveCapabilities(PacerCapabilities());
  EXPECT_EQ(loop.Pacer.ActiveRating().Tier, PC::PacerTier::TimerPeriodOnly);
  EXPECT_EQ(loop.Pacer.SystemHold(), SystemHoldState::Holding);
  const PC::FrameStartPlan plan = loop.Pacer.PlanFrame(At(loop.Now));
  EXPECT_EQ(plan.StartTime, At(loop.StartNanoseconds + Probe));
  // And back: the frames go on, ids and animation time
  const PC::FrameSchedule moved = loop.Frame(loop.StartNanoseconds + Period, Period / 2);
  EXPECT_EQ(moved.FrameId, before.FrameId + 1u);
  loop.Pacer.SetActiveCapabilities(loop.Pacer.Capabilities());
  EXPECT_EQ(loop.Pacer.WorkingTier(), PC::PacerTier::SystemHoldsLoop);
  const PC::FrameSchedule back = loop.Frame(loop.StartNanoseconds + Period, Period / 2);
  EXPECT_EQ(back.FrameId, moved.FrameId + 1u);
  EXPECT_GT(back.AnimationTime.Nanoseconds(), moved.AnimationTime.Nanoseconds());
  EXPECT_FALSE(loop.Plan.WaitsForStartTime());
  loop.HeldFrames(20);
  EXPECT_EQ(loop.Pacer.SystemHold(), SystemHoldState::Holding);
}

TEST(SystemHeldLoopPacer, NotPacingAndStartingAgain)
{
  Loop loop(Settings(), PacerCapabilities(Held | PacerCapability::DisplayTimes));
  static_cast<void>(loop.Frame(Start));
  loop.HeldFrames(20);
  // Not pacing: nothing to wait for, the reports are taken; on again the frames go on
  loop.Pacer.SetPacing(false);
  for (int32_t frame = 0; frame < 10; ++frame)
  {
    static_cast<void>(loop.Frame(loop.StartNanoseconds + 2'000'000));
    loop.Pacer.AddSystemWait({PC::SystemWaitKind::Acquire, At(loop.Now), At(loop.Now)});
  }
  loop.Pacer.SetPacing(true);
  const PC::FrameSchedule again = loop.Frame(loop.StartNanoseconds + Period);
  EXPECT_TRUE(again.Paced);
  loop.HeldFrames(20);
  EXPECT_EQ(loop.Pacer.SystemHold(), SystemHoldState::Holding);
  // Display times are statistics here: taken, and nothing is paced by them
  PC::DisplayReport report;
  report.FrameId = again.FrameId;
  report.DisplayTime = At(loop.StartNanoseconds);
  loop.Pacer.AddDisplayReport(report);

  // A new swap chain, a new refresh period, other settings and a reset each let the queue fill from empty again: four frames
  // back to back are not a broken hold
  const auto burst = [&loop]()
  {
    for (int32_t frame = 0; frame < 4; ++frame)
    {
      static_cast<void>(loop.Frame(loop.StartNanoseconds + 200'000, 0, 50'000));
    }
    return loop.Pacer.SystemHold();
  };
  loop.Pacer.ForgetPresents();
  EXPECT_EQ(burst(), SystemHoldState::Holding);
  loop.HeldFrames(20);
  loop.Pacer.SetRefreshPeriod(PC::RefreshPeriod::FromRate(50));
  loop.Pacer.SetRefreshPeriod(PC::RefreshPeriod::FromRate(50));
  EXPECT_EQ(burst(), SystemHoldState::Holding);
  loop.HeldFrames(20, 2 * Period);
  PC::PacerSettings other = Settings();
  other.SetRefresh(PC::RefreshPeriod::FromRate(50));
  other.SetMaxFramesInFlight(2);
  loop.Pacer.SetSettings(other);
  loop.Pacer.SetSettings(other);
  EXPECT_EQ(burst(), SystemHoldState::Holding);
  loop.HeldFrames(20, 2 * Period);
  loop.Pacer.Reset();
  EXPECT_EQ(burst(), SystemHoldState::Holding);
  // A pause (longer than a frame window) and a clock that went back are not judged either
  static_cast<void>(loop.Frame(loop.StartNanoseconds + (3 * FP::NanosecondTimeSpan::NanosecondsPerSecond)));
  EXPECT_EQ(burst(), SystemHoldState::Holding);
  loop.HeldFrames(20, 2 * Period);
  loop.Now = loop.StartNanoseconds - Period;
  static_cast<void>(loop.Frame(loop.Now));
  EXPECT_EQ(burst(), SystemHoldState::Holding);
}

TEST(SystemHeldLoopPacer, WhatItReports)
{
  Loop loop(Settings(), PacerCapabilities(Held));
  static_cast<void>(loop.Frame(Start));
  // A frame begun without the one before it ended: its work is not known, and it is not late
  static_cast<void>(loop.Pacer.BeginFrame(At(loop.StartNanoseconds + Period)));
  EXPECT_EQ(loop.Pacer.CpuBusyAt(At(loop.StartNanoseconds + Period + 1'000)).Nanoseconds(), 1'000);
  static_cast<void>(loop.Pacer.BeginFrame(At(loop.StartNanoseconds + (2 * Period))));
  EXPECT_EQ(loop.Pacer.FrameWindow().LateFrames, 0u);
  loop.StartNanoseconds += 2 * Period;
  const PC::PresentPlan present = loop.Pacer.EndFrame(At(loop.StartNanoseconds + Work));
  EXPECT_EQ(present.CpuBusy.Nanoseconds(), Work);
  EXPECT_EQ(loop.Pacer.CpuBusyAt(At(loop.StartNanoseconds + Work + 1'000)).Nanoseconds(), Work + 1'000);
  // Before any frame there is nothing to end, and nothing is busy
  Loop fresh(Settings(), PacerCapabilities(Held));
  EXPECT_EQ(fresh.Pacer.EndFrame(At(Start)).FrameId, 0u);
  EXPECT_EQ(fresh.Pacer.CpuBusyAt(At(Start)).Nanoseconds(), 0);
  PC::PresentReport report;
  report.FrameId = present.FrameId;
  report.CallTime = At(loop.StartNanoseconds + Work);
  report.ReturnTime = At(loop.StartNanoseconds + Work + 500'000);
  loop.Pacer.AddPresent(report);
  EXPECT_EQ(loop.Pacer.LastPresentBlocked().Nanoseconds(), 500'000);
  loop.Pacer.AddGpuWork(PC::GpuWorkReport::Times(present.FrameId, At(loop.StartNanoseconds + Work), At(loop.StartNanoseconds + Work + 4'000'000)));
  EXPECT_EQ(loop.Pacer.GpuTime().Nanoseconds(), 4'000'000);
  EXPECT_EQ(loop.Pacer.PresentWaitTimeouts(), 0u);
  EXPECT_EQ(loop.Pacer.GpuWaitTimeouts(), 0u);
  EXPECT_EQ(loop.Pacer.SwapInterval(), 1u);
  EXPECT_EQ(loop.Pacer.Refresh(), g_hz100);
  EXPECT_EQ(loop.Pacer.Settings(), Settings());
  // Another tier knows of no system's hold
  const Loop grid(Settings(), PacerCapabilities());
  EXPECT_EQ(grid.Pacer.SystemHold(), SystemHoldState::Holding);
}
