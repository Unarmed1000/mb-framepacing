// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The pacer of the lowest tier (a timer, and the refresh period only): frame starts on one grid of refresh periods on the
// clock, an animation time that advances by the swap interval alone, and a frame of more than one refresh held by a wait before
// its present. What it is given is a clock's times; what it gives back the application carries out.
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/capability/PacerTier.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/frame/FrameStartPlan.hpp>
#include <mb/framepacing/pacer/frame/GpuWorkReport.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/frame/SystemWaitKind.hpp>
#include <mb/framepacing/pacer/frame/SystemWaitReport.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <mb/framepacing/pacer/tier/TimerPeriodOnlyPacer.hpp>
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

  constexpr FP::NanosecondTickCount At(const int64_t nanoseconds) noexcept
  {
    return FP::NanosecondTickCount(nanoseconds);
  }

  constexpr FP::NanosecondTimeSpan Span(const int64_t nanoseconds) noexcept
  {
    return FP::NanosecondTimeSpan(nanoseconds);
  }

  //! The settings of the tests that are about the aim of low latency: the default aim is smoothness
  PC::PacerSettings LowLatencySettings()
  {
    PC::PacerSettings settings(g_hz100);
    settings.SetAim(PC::PacerAim::LowLatency);
    return settings;
  }

  //! A frame as an application makes it: planned at now, begun at the time it is given (or at once), with CPU work of workNanoseconds.
  //! Returns the frame's start.
  int64_t Frame(PC::TimerPeriodOnlyPacer& rPacer, const int64_t nowNanoseconds, const int64_t workNanoseconds, PC::FrameSchedule* pSchedule = nullptr)
  {
    const PC::FrameStartPlan plan = rPacer.PlanFrame(At(nowNanoseconds));
    const int64_t startNanoseconds = plan.WaitsForStartTime() ? plan.StartTime.Nanoseconds() : nowNanoseconds;
    const PC::FrameSchedule schedule = rPacer.BeginFrame(At(startNanoseconds));
    static_cast<void>(rPacer.EndFrame(At(startNanoseconds + workNanoseconds)));
    if (pSchedule != nullptr)
    {
      *pSchedule = schedule;
    }
    return startNanoseconds;
  }

  //! A frame as Frame makes it, with CPU work of 3 ms and its present reported: taken by the system or not. Returns
  //! the frame's start.
  int64_t PresentedFrame(PC::TimerPeriodOnlyPacer& rPacer, const int64_t nowNanoseconds, PC::FrameSchedule* pSchedule = nullptr,
                         const bool accepted = true)
  {
    const PC::FrameStartPlan plan = rPacer.PlanFrame(At(nowNanoseconds));
    const int64_t startNanoseconds = plan.WaitsForStartTime() ? plan.StartTime.Nanoseconds() : nowNanoseconds;
    const PC::FrameSchedule schedule = rPacer.BeginFrame(At(startNanoseconds));
    const PC::PresentPlan present = rPacer.EndFrame(At(startNanoseconds + 3'000'000));
    PC::PresentReport report;
    report.FrameId = present.FrameId;
    report.CallTime = At(startNanoseconds + 3'000'000);
    report.ReturnTime = At(startNanoseconds + 3'060'000);
    report.Accepted = accepted;
    rPacer.AddPresent(report);
    if (pSchedule != nullptr)
    {
      *pSchedule = schedule;
    }
    return startNanoseconds;
  }

  static_assert(PC::TimerPeriodOnlyPacer::Tier == PC::PacerTier::TimerPeriodOnly);
}

TEST(TimerPeriodOnlyPacer, TheFirstFrameStartsAtOnceAndStartsTheGrid)
{
  PC::TimerPeriodOnlyPacer pacer{LowLatencySettings()};

  EXPECT_FALSE(pacer.PlanFrame(At(Start)).WaitsForStartTime());
  EXPECT_FALSE(pacer.PlanFrame(At(Start)).WaitsForPresent());
  const PC::FrameSchedule schedule = pacer.BeginFrame(At(Start));
  EXPECT_EQ(schedule.FrameId, 1u);
  EXPECT_EQ(schedule.SwapInterval, 1u);
  EXPECT_EQ(schedule.AnimationTime, Span(0));
  EXPECT_EQ(schedule.AnimationStep, Span(0));
  EXPECT_EQ(schedule.NextFrameStartTime, At(Start + Period));
  EXPECT_EQ(schedule.IntendedDisplayTime, At(Start + Period));
  EXPECT_EQ(schedule.TargetFrameTime, FP::NanosecondTimeDuration::FromNanoseconds(Period));
  EXPECT_EQ(schedule.PreferredFrameTime, FP::NanosecondTimeDuration::FromNanoseconds(Period));
  EXPECT_EQ(schedule.Change, PC::SwapIntervalChange::Unchanged);

  const PC::PresentPlan present = pacer.EndFrame(At(Start + 3'000'000));
  EXPECT_EQ(present.FrameId, 1u);
  EXPECT_EQ(present.CpuBusy, FP::NanosecondTimeDuration::FromNanoseconds(3'000'000));
  // A frame of one refresh is presented when it is done, with nothing for a present that takes values
  EXPECT_FALSE(present.WaitsForPresentTime());
  EXPECT_EQ(present.SwapInterval, 1u);
  EXPECT_EQ(present.NotBeforeTime, FP::NanosecondTickCount());
  EXPECT_EQ(present.MinimumDuration, FP::NanosecondTimeDuration::Zero());
}

TEST(TimerPeriodOnlyPacer, EveryFrameIsDueAWholeNumberOfPeriodsAfterTheFirst)
{
  PC::TimerPeriodOnlyPacer pacer{LowLatencySettings()};
  static_cast<void>(Frame(pacer, Start, 3'000'000));

  for (int64_t frame = 1; frame < 500; ++frame)
  {
    // The loop is back a fifth of a period after the frame before it started
    const int64_t now = Start + ((frame - 1) * Period) + 2'000'000;
    const PC::FrameStartPlan plan = pacer.PlanFrame(At(now));
    ASSERT_EQ(plan.StartTime, At(Start + (frame * Period))) << frame;
    // Planned again, it is the same plan
    ASSERT_EQ(pacer.PlanFrame(At(now + 500'000)).StartTime, plan.StartTime) << frame;
    // The wait wakes a little late
    const PC::FrameSchedule schedule = pacer.BeginFrame(At(plan.StartTime.Nanoseconds() + 30'000));
    ASSERT_EQ(schedule.FrameId, static_cast<uint64_t>(frame + 1));
    ASSERT_EQ(schedule.AnimationTime, Span(frame * Period)) << frame;
    ASSERT_EQ(schedule.AnimationStep, Span(Period)) << frame;
    ASSERT_EQ(schedule.NextFrameStartTime, At(Start + ((frame + 1) * Period))) << frame;
    static_cast<void>(pacer.EndFrame(At(plan.StartTime.Nanoseconds() + 3'030'000)));
  }
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  EXPECT_EQ(pacer.SwapInterval(), 1u);
}

TEST(TimerPeriodOnlyPacer, AStartThatIsLateCostsThatFrameAndNoFrameAfterIt)
{
  PC::TimerPeriodOnlyPacer pacer{LowLatencySettings()};
  static_cast<void>(Frame(pacer, Start, 3'000'000));

  // The loop comes back 0.4 of a period after the second frame was due: it starts at once and keeps its step
  const int64_t late = Start + Period + 4'000'000;
  EXPECT_FALSE(pacer.PlanFrame(At(late)).WaitsForStartTime());
  const PC::FrameSchedule second = pacer.BeginFrame(At(late));
  EXPECT_EQ(second.AnimationStep, Span(Period));
  // The frame after it is due where it would have been, not a period after the late start
  EXPECT_EQ(second.NextFrameStartTime, At(Start + (2 * Period)));
  static_cast<void>(pacer.EndFrame(At(late + 3'000'000)));
  EXPECT_EQ(pacer.PlanFrame(At(late + 3'060'000)).StartTime, At(Start + (2 * Period)));
  static_cast<void>(Frame(pacer, late + 3'060'000, 3'000'000));

  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  // It shows as a frame that began after its time
  EXPECT_EQ(pacer.FrameWindow().StartsAhead, Span(-4'000'000));
}

TEST(TimerPeriodOnlyPacer, AFrameThatRanLongCostsWholeStepsAndTheLoopIsBackOnTheGrid)
{
  PC::TimerPeriodOnlyPacer pacer{LowLatencySettings()};
  static_cast<void>(Frame(pacer, Start, 3'000'000));

  // The second frame works for 2.25 periods: it is done 3.25 periods after the grid's start, no later in its step than the
  // frame before it was (0.3 of a period)
  const int64_t secondStart = Frame(pacer, Start + 3'060'000, 22'500'000);
  ASSERT_EQ(secondStart, Start + Period);
  const int64_t now = secondStart + 22'560'000;
  // The next frame takes the next step, the fourth, and waits for it: not the moment the work was done
  const PC::FrameStartPlan plan = pacer.PlanFrame(At(now));
  EXPECT_EQ(plan.StartTime, At(Start + (4 * Period)));

  const PC::FrameSchedule third = pacer.BeginFrame(plan.StartTime);
  // Two steps of the grid were lost. The animation time is not moved over them: its step is the frame's swap interval
  EXPECT_EQ(pacer.RefreshesBehindClock(), 2u);
  EXPECT_EQ(third.AnimationStep, Span(Period));
  EXPECT_EQ(third.AnimationTime, Span(2 * Period));
  EXPECT_EQ(third.NextFrameStartTime, At(Start + (5 * Period)));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
  static_cast<void>(pacer.EndFrame(At(Start + (4 * Period) + 3'000'000)));

  // And every frame after it is on the grid it was on before
  for (int64_t frame = 5; frame < 50; ++frame)
  {
    ASSERT_EQ(Frame(pacer, Start + ((frame - 1) * Period) + 3'060'000, 3'000'000), Start + (frame * Period)) << frame;
  }
  EXPECT_EQ(pacer.RefreshesBehindClock(), 2u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
}

TEST(TimerPeriodOnlyPacer, AfterALatePresentTheNextPresentComesAWholePeriodLater)
{
  // Frames are presented 0.3 of a period into their step. A frame of 2.6 periods on step 1 is done 3.6 periods after the
  // grid's start: later in its step than a present is
  PC::TimerPeriodOnlyPacer pacer{LowLatencySettings()};
  static_cast<void>(Frame(pacer, Start, 3'000'000));
  static_cast<void>(Frame(pacer, Start + 3'060'000, 3'000'000));
  static_cast<void>(Frame(pacer, Start + Period + 3'060'000, 26'000'000));
  // The next step would put the next present 0.7 of a period after this one, and the two could reach the display between
  // the same two refreshes: the step after it, the sixth
  const int64_t now = Start + (2 * Period) + 26'060'000;
  const PC::FrameStartPlan plan = pacer.PlanFrame(At(now));
  EXPECT_EQ(plan.StartTime, At(Start + (6 * Period)));
  // Begun without the wait, it is on that step all the same
  const PC::FrameSchedule next = pacer.BeginFrame(At(now));
  EXPECT_EQ(next.NextFrameStartTime, At(Start + (7 * Period)));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 3u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
}

TEST(TimerPeriodOnlyPacer, WhenThePresentWasMadeIsThePresentReportsWordAndWithoutItEndFrames)
{
  PC::TimerPeriodOnlyPacer pacer{LowLatencySettings()};
  static_cast<void>(Frame(pacer, Start, 3'000'000));
  static_cast<void>(Frame(pacer, Start + 3'060'000, 3'000'000));

  // Work of 1.3 periods on step 2, and no present report: made when the work was done, 0.3 into step 3, where a present is.
  // The next step
  static_cast<void>(pacer.BeginFrame(At(Start + (2 * Period))));
  const PC::PresentPlan present = pacer.EndFrame(At(Start + (2 * Period) + 13'000'000));
  EXPECT_EQ(pacer.PlanFrame(At(Start + (2 * Period) + 13'600'000)).StartTime, At(Start + (4 * Period)));

  // A report of another frame changes nothing
  PC::PresentReport report;
  report.FrameId = present.FrameId - 1u;
  report.CallTime = At(Start + (2 * Period) + 18'000'000);
  report.ReturnTime = report.CallTime;
  pacer.AddPresent(report);
  EXPECT_EQ(pacer.PlanFrame(At(Start + (2 * Period) + 13'600'000)).StartTime, At(Start + (4 * Period)));
  // The report of this frame says the present was called 0.8 into step 3 (the application did something in between): the
  // step after the next
  report.FrameId = present.FrameId;
  pacer.AddPresent(report);
  EXPECT_EQ(pacer.PlanFrame(At(Start + (2 * Period) + 18'100'000)).StartTime, At(Start + (5 * Period)));

  // A report before the frame's work is done is of no frame
  static_cast<void>(pacer.BeginFrame(At(Start + (5 * Period))));
  report.FrameId = present.FrameId + 1u;
  report.CallTime = At(Start + (9 * Period));
  pacer.AddPresent(report);
  static_cast<void>(pacer.EndFrame(At(Start + (5 * Period) + 3'000'000)));
  EXPECT_EQ(pacer.PlanFrame(At(Start + (5 * Period) + 3'100'000)).StartTime, At(Start + (6 * Period)));
}

TEST(TimerPeriodOnlyPacer, ALoopThatComesBackLateAfterAPresentOnTimeKeepsItsStep)
{
  PC::TimerPeriodOnlyPacer pacer{LowLatencySettings()};
  static_cast<void>(Frame(pacer, Start, 3'000'000));
  static_cast<void>(Frame(pacer, Start + 3'060'000, 3'000'000));

  // The frame was presented on time and the loop is back 0.3 of a period after the next frame was due: it starts at once, on
  // its step, and the frame after it is due a period after that step
  const int64_t now = Start + (2 * Period) + 3'000'000;
  EXPECT_FALSE(pacer.PlanFrame(At(now)).WaitsForStartTime());
  const PC::FrameSchedule schedule = pacer.BeginFrame(At(now));
  EXPECT_EQ(schedule.NextFrameStartTime, At(Start + (3 * Period)));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
}

TEST(TimerPeriodOnlyPacer, AFrameWhoseWorkIsOverItsTimeIsLateAndCostsAStep)
{
  PC::TimerPeriodOnlyPacer pacer{LowLatencySettings()};
  static_cast<void>(Frame(pacer, Start, 3'000'000));

  // Work of 1.3 periods: its present is made in the step the next frame was due at, and the next frame takes the step after
  static_cast<void>(Frame(pacer, Start + 3'060'000, 13'000'000));
  const int64_t now = Start + Period + 13'060'000;
  EXPECT_EQ(pacer.PlanFrame(At(now)).StartTime, At(Start + (3 * Period)));
  static_cast<void>(pacer.BeginFrame(At(Start + (3 * Period))));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 1u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
}

TEST(TimerPeriodOnlyPacer, FramesThatKeepRunningLongMakeTheRuleSlowDownAndTheAnimationFallBehindUntilItDoes)
{
  PC::TimerPeriodOnlyPacer pacer{LowLatencySettings()};
  int64_t now = Start;
  int64_t frames = 0;
  // Work of 1.3 periods every frame, at one refresh per frame
  while (pacer.SwapInterval() == 1 && frames < 2'000)
  {
    now = Frame(pacer, now, 13'000'000) + 13'060'000;
    ++frames;
  }
  ASSERT_EQ(pacer.SwapInterval(), 2u);
  EXPECT_LT(frames, 400);
  // Until then the animation time ran slower than the clock by the steps that were lost
  const uint64_t behind = pacer.RefreshesBehindClock();
  EXPECT_GT(behind, 0u);

  // At two refreshes per frame the work fits: on the grid, nothing lost, nothing late
  PC::FrameSchedule schedule;
  for (int32_t frame = 0; frame < 100; ++frame)
  {
    now = Frame(pacer, now, 13'000'000, &schedule) + 13'060'000;
    ASSERT_EQ(schedule.SwapInterval, 2u);
  }
  EXPECT_EQ(schedule.AnimationStep, Span(2 * Period));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  // The first frame at the new swap interval is the last that starts off its step
  EXPECT_LE(pacer.RefreshesBehindClock(), behind + 1u);
}

TEST(TimerPeriodOnlyPacer, AFrameOfMoreThanOneRefreshIsHeldByAWaitBeforeItsPresent)
{
  PC::PacerSettings settings = LowLatencySettings();
  settings.SetPreferredFrameRate(25);
  PC::TimerPeriodOnlyPacer pacer(settings);

  const PC::FrameSchedule first = pacer.BeginFrame(At(Start));
  EXPECT_EQ(first.SwapInterval, 4u);
  EXPECT_EQ(first.NextFrameStartTime, At(Start + (4 * Period)));
  EXPECT_EQ(first.TargetFrameTime, FP::NanosecondTimeDuration::FromNanoseconds(4 * Period));
  // Presented in the period before the step the next frame is due at, the frame margin (1 ms here) into it
  const PC::PresentPlan present = pacer.EndFrame(At(Start + 3'000'000));
  EXPECT_EQ(settings.FrameMargin(), Span(1'000'000));
  EXPECT_EQ(present.PresentTime, At(Start + (3 * Period) + 1'000'000));
  // The present has no swap interval at this tier: the loop holds the frame
  EXPECT_EQ(present.SwapInterval, 1u);

  // The next frame is due four periods after the first, and its animation time is four periods on
  const PC::FrameStartPlan plan = pacer.PlanFrame(At(Start + (3 * Period) + 1'060'000));
  EXPECT_EQ(plan.StartTime, At(Start + (4 * Period)));
  const PC::FrameSchedule second = pacer.BeginFrame(plan.StartTime);
  EXPECT_EQ(second.AnimationStep, Span(4 * Period));
  // Work that is done after the time to present at is presented at once
  EXPECT_FALSE(pacer.EndFrame(At(Start + (7 * Period) + 2'000'000)).WaitsForPresentTime());
  EXPECT_FALSE(pacer.EndFrame(At(Start + (7 * Period) + 1'000'000)).WaitsForPresentTime());
  EXPECT_TRUE(pacer.EndFrame(At(Start + (7 * Period) + 999'999)).WaitsForPresentTime());
}

TEST(TimerPeriodOnlyPacer, APauseOrAClockThatWentBackStartsTheGridAgain)
{
  PC::TimerPeriodOnlyPacer pacer{LowLatencySettings()};
  int64_t now = Start;
  for (int32_t frame = 0; frame < 20; ++frame)
  {
    now = Frame(pacer, now, 3'000'000) + 3'060'000;
  }
  ASSERT_EQ(pacer.FrameWindow().Frames, 19u);

  // A minute later: no wait, a new grid from this start, an empty frame window, and no refresh counted as lost
  const int64_t later = now + (60 * FP::NanosecondTimeSpan::NanosecondsPerSecond);
  EXPECT_FALSE(pacer.PlanFrame(At(later)).WaitsForStartTime());
  const PC::FrameSchedule resumed = pacer.BeginFrame(At(later));
  EXPECT_EQ(resumed.NextFrameStartTime, At(later + Period));
  // The animation time goes on by the frame's swap interval
  EXPECT_EQ(resumed.AnimationStep, Span(Period));
  EXPECT_EQ(pacer.FrameWindow().Frames, 0u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  static_cast<void>(pacer.EndFrame(At(later + 3'000'000)));

  // A start before the frame before it: the same
  EXPECT_FALSE(pacer.PlanFrame(At(later - 1)).WaitsForStartTime());
  const PC::FrameSchedule back = pacer.BeginFrame(At(later - 1));
  EXPECT_EQ(back.NextFrameStartTime, At(later - 1 + Period));
  EXPECT_EQ(pacer.FrameWindow().Frames, 0u);
}

TEST(TimerPeriodOnlyPacer, AFrameWithoutAnEndIsNotJudgedByItsWork)
{
  PC::TimerPeriodOnlyPacer pacer{LowLatencySettings()};
  static_cast<void>(pacer.BeginFrame(At(Start)));
  // No EndFrame, and the next frame starts 1.4 periods later: a step, not late by work, and its work is the time between
  static_cast<void>(pacer.BeginFrame(At(Start + 14'000'000)));
  EXPECT_EQ(pacer.FrameWindow().Frames, 1u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  EXPECT_EQ(pacer.FrameWindow().AverageWork, Span(14'000'000));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
}

TEST(TimerPeriodOnlyPacer, EndFrameWithoutAFrameIsNothingAndAPresentReportIsKept)
{
  PC::TimerPeriodOnlyPacer pacer{LowLatencySettings()};

  const PC::PresentPlan nothing = pacer.EndFrame(At(Start));
  EXPECT_EQ(nothing.FrameId, 0u);
  EXPECT_FALSE(nothing.WaitsForPresentTime());
  EXPECT_EQ(pacer.LastPresentBlocked(), FP::NanosecondTimeDuration::Zero());

  static_cast<void>(pacer.BeginFrame(At(Start)));
  static_cast<void>(pacer.EndFrame(At(Start + 3'000'000)));
  PC::PresentReport report;
  report.FrameId = 1;
  report.CallTime = At(Start + 3'000'000);
  report.ReturnTime = At(Start + 3'060'000);
  pacer.AddPresent(report);
  EXPECT_EQ(pacer.LastPresentBlocked(), FP::NanosecondTimeDuration::FromNanoseconds(60'000));
}

TEST(TimerPeriodOnlyPacer, TheCpuBusyTimeCanBeAskedForWhileTheFrameIsOpen)
{
  PC::TimerPeriodOnlyPacer pacer{LowLatencySettings()};
  EXPECT_EQ(pacer.CpuBusyAt(At(Start)), FP::NanosecondTimeDuration());
  static_cast<void>(pacer.BeginFrame(At(Start)));
  // Where a marker is drawn before the frame's work is done
  EXPECT_EQ(pacer.CpuBusyAt(At(Start + 1'200'000)), FP::NanosecondTimeDuration::FromNanoseconds(1'200'000));
  EXPECT_EQ(pacer.CpuBusyAt(At(Start - 1)), FP::NanosecondTimeDuration());
  pacer.Reset();
  EXPECT_EQ(pacer.CpuBusyAt(At(Start + 1'200'000)), FP::NanosecondTimeDuration());
}

TEST(TimerPeriodOnlyPacer, AnotherRefreshPeriodOrOtherSettingsStartTheGridAgainAndTheAnimationTimeGoesOn)
{
  PC::PacerSettings settings = LowLatencySettings();
  PC::TimerPeriodOnlyPacer pacer(settings);
  int64_t now = Start;
  for (int32_t frame = 0; frame < 10; ++frame)
  {
    now = Frame(pacer, now, 3'000'000) + 3'060'000;
  }
  EXPECT_EQ(pacer.Refresh(), g_hz100);
  EXPECT_EQ(pacer.Settings(), settings);

  // The same period and the same settings change nothing: the next frame is due on the grid
  pacer.SetRefreshPeriod(g_hz100);
  pacer.SetSettings(settings);
  EXPECT_EQ(pacer.PlanFrame(At(now)).StartTime, At(Start + (10 * Period)));

  // 50 Hz: a new grid from the next start, an empty frame window, and a step of the new period
  const PC::RefreshPeriod hz50 = PC::RefreshPeriod::FromRate(50);
  pacer.SetRefreshPeriod(hz50);
  EXPECT_EQ(pacer.Refresh(), hz50);
  EXPECT_FALSE(pacer.PlanFrame(At(now)).WaitsForStartTime());
  PC::FrameSchedule schedule = pacer.BeginFrame(At(now));
  EXPECT_EQ(schedule.AnimationTime, Span((9 * Period) + (2 * Period)));
  EXPECT_EQ(schedule.AnimationStep, Span(2 * Period));
  EXPECT_EQ(schedule.NextFrameStartTime, At(now + (2 * Period)));
  EXPECT_EQ(pacer.FrameWindow().Frames, 0u);
  static_cast<void>(pacer.EndFrame(At(now + 3'000'000)));

  // Other settings: the same, at the swap interval they prefer
  settings.SetRefresh(hz50);
  settings.SetPreferredFrameRate(25);
  pacer.SetSettings(settings);
  EXPECT_FALSE(pacer.PlanFrame(At(now + 3'060'000)).WaitsForStartTime());
  schedule = pacer.BeginFrame(At(now + 3'060'000));
  EXPECT_EQ(schedule.SwapInterval, 2u);
  EXPECT_EQ(schedule.NextFrameStartTime, At(now + 3'060'000 + (4 * Period)));
  static_cast<void>(pacer.EndFrame(At(now + 6'060'000)));

  // Reset: the next frame starts at once on a grid of its own, and a frame that was open is not ended afterwards
  pacer.Reset();
  EXPECT_FALSE(pacer.PlanFrame(At(now + 6'100'000)).WaitsForStartTime());
  EXPECT_EQ(pacer.EndFrame(At(now + 6'100'000)).FrameId, 0u);
  schedule = pacer.BeginFrame(At(now + 6'100'000));
  EXPECT_EQ(schedule.NextFrameStartTime, At(now + 6'100'000 + (4 * Period)));
}

TEST(TimerPeriodOnlyPacer, HalfASecondAfterStartUpTheLoopPausesOnceAndTheFrameOnScreenStays)
{
  PC::TimerPeriodOnlyPacer pacer{LowLatencySettings()};
  PC::FrameSchedule schedule;
  int64_t now = Start;
  for (int64_t frame = 0; frame < 50; ++frame)
  {
    const int64_t start = PresentedFrame(pacer, now, &schedule);
    ASSERT_EQ(start, Start + (frame * Period));
    ASSERT_EQ(schedule.NextFrameStartTime, At(start + Period));
    now = start + 3'060'000;
  }
  EXPECT_EQ(pacer.StartupPauses(), 0u);

  // The frame that starts half a second after the first is presented as any other, and the frame after it is due four
  // refreshes later than it would be
  int64_t start = PresentedFrame(pacer, now, &schedule);
  EXPECT_EQ(start, Start + (50 * Period));
  EXPECT_EQ(schedule.AnimationStep, Span(Period));
  EXPECT_EQ(schedule.IntendedDisplayTime, At(Start + (51 * Period)));
  EXPECT_EQ(schedule.NextFrameStartTime, At(Start + (55 * Period)));
  EXPECT_EQ(schedule.TargetFrameTime, FP::NanosecondTimeDuration::FromNanoseconds(Period));
  EXPECT_EQ(pacer.StartupPauses(), 1u);
  EXPECT_EQ(pacer.PlanFrame(At(start + 3'060'000)).StartTime, At(Start + (55 * Period)));

  // The frame after the pause is on time, its animation time a swap interval on: the pause is not caught up with
  start = PresentedFrame(pacer, start + 3'060'000, &schedule);
  EXPECT_EQ(start, Start + (55 * Period));
  EXPECT_EQ(schedule.AnimationStep, Span(Period));
  EXPECT_EQ(schedule.AnimationTime, Span(51 * Period));
  EXPECT_EQ(schedule.NextFrameStartTime, At(Start + (56 * Period)));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 4u);

  // Once: never again in this run
  for (int64_t frame = 0; frame < 300; ++frame)
  {
    const int64_t next = PresentedFrame(pacer, start + 3'060'000, &schedule);
    ASSERT_EQ(next, start + Period);
    start = next;
  }
  EXPECT_EQ(pacer.StartupPauses(), 1u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 4u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
}

TEST(TimerPeriodOnlyPacer, ThereIsNoPauseBeforeAPresentWasTaken)
{
  PC::PacerSettings settings = LowLatencySettings();
  settings.SetStartupPauseDelay(Span(0));
  PC::TimerPeriodOnlyPacer pacer(settings);

  // No present is reported: nothing is on its way to the screen, however long it takes
  int64_t now = Start;
  for (int32_t frame = 0; frame < 80; ++frame)
  {
    now = Frame(pacer, now, 3'000'000) + 3'060'000;
  }
  EXPECT_EQ(pacer.StartupPauses(), 0u);

  // A frame whose present is taken: the pause is made at the frame after it, also with no delay at all
  PC::FrameSchedule schedule;
  int64_t start = PresentedFrame(pacer, now, &schedule);
  EXPECT_EQ(schedule.NextFrameStartTime, At(start + Period));
  EXPECT_EQ(pacer.StartupPauses(), 0u);
  start = PresentedFrame(pacer, start + 3'060'000, &schedule);
  EXPECT_EQ(schedule.NextFrameStartTime, At(start + (5 * Period)));
  EXPECT_EQ(pacer.StartupPauses(), 1u);
}

TEST(TimerPeriodOnlyPacer, ASwapChainMadeAnewAndAResetGetThePauseOfAStart)
{
  PC::PacerSettings settings = LowLatencySettings();
  settings.SetStartupPauseDelay(Span(2 * Period));
  settings.SetStartupPauseRefreshes(3);
  PC::TimerPeriodOnlyPacer pacer(settings);
  PC::FrameSchedule schedule;

  // Frames at 0, 1 and 2 periods: the third is two periods after the first
  int64_t start = PresentedFrame(pacer, Start);
  start = PresentedFrame(pacer, start + 3'060'000);
  start = PresentedFrame(pacer, start + 3'060'000, &schedule);
  EXPECT_EQ(schedule.NextFrameStartTime, At(Start + (6 * Period)));
  EXPECT_EQ(pacer.StartupPauses(), 1u);
  start = PresentedFrame(pacer, start + 3'060'000, &schedule);
  ASSERT_EQ(start, Start + (6 * Period));

  // The application made its swap chain anew: counted from the next frame, with a present of the new one taken
  pacer.ForgetPresents();
  start = PresentedFrame(pacer, start + 3'060'000);
  start = PresentedFrame(pacer, start + 3'060'000);
  EXPECT_EQ(pacer.StartupPauses(), 1u);
  start = PresentedFrame(pacer, start + 3'060'000, &schedule);
  ASSERT_EQ(start, Start + (9 * Period));
  EXPECT_EQ(schedule.NextFrameStartTime, At(Start + (13 * Period)));
  EXPECT_EQ(pacer.StartupPauses(), 2u);

  // A present the system did not take says the same
  start = PresentedFrame(pacer, start + 3'060'000, &schedule, false);
  ASSERT_EQ(start, Start + (13 * Period));
  start = PresentedFrame(pacer, start + 3'060'000);
  start = PresentedFrame(pacer, start + 3'060'000);
  EXPECT_EQ(pacer.StartupPauses(), 2u);
  start = PresentedFrame(pacer, start + 3'060'000, &schedule);
  ASSERT_EQ(start, Start + (16 * Period));
  EXPECT_EQ(schedule.NextFrameStartTime, At(Start + (20 * Period)));
  EXPECT_EQ(pacer.StartupPauses(), 3u);

  // And so does a reset
  pacer.Reset();
  start = PresentedFrame(pacer, Start + (30 * Period));
  start = PresentedFrame(pacer, start + 3'060'000);
  start = PresentedFrame(pacer, start + 3'060'000, &schedule);
  ASSERT_EQ(start, Start + (32 * Period));
  EXPECT_EQ(schedule.NextFrameStartTime, At(Start + (36 * Period)));
  EXPECT_EQ(pacer.StartupPauses(), 4u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 12u);
}

TEST(TimerPeriodOnlyPacer, AtTwoRefreshesPerFrameOrWithAPauseOfNoRefreshesThereIsNone)
{
  // Two refreshes per frame when the pause is due: the display took what waited, and the pause is not made later either
  PC::PacerSettings settings = LowLatencySettings();
  settings.SetStartupPauseDelay(Span(2 * Period));
  settings.SetPreferredSwapInterval(2);
  PC::TimerPeriodOnlyPacer slow(settings);
  PC::FrameSchedule schedule;
  int64_t start = Start;
  for (int64_t frame = 0; frame < 20; ++frame)
  {
    start = PresentedFrame(slow, start + (frame == 0 ? 0 : Period + 1'060'000), &schedule);
    ASSERT_EQ(start, Start + (frame * 2 * Period));
    ASSERT_EQ(schedule.NextFrameStartTime, At(start + (2 * Period)));
  }
  EXPECT_EQ(slow.StartupPauses(), 0u);
  EXPECT_EQ(slow.RefreshesBehindClock(), 0u);

  // A pause of no refreshes is no pause
  PC::PacerSettings none = LowLatencySettings();
  none.SetStartupPauseDelay(Span(0));
  none.SetStartupPauseRefreshes(0);
  PC::TimerPeriodOnlyPacer pacer(none);
  start = Start;
  for (int64_t frame = 0; frame < 20; ++frame)
  {
    start = PresentedFrame(pacer, start + (frame == 0 ? 0 : 3'060'000), &schedule);
    ASSERT_EQ(schedule.NextFrameStartTime, At(Start + ((frame + 1) * Period)));
  }
  EXPECT_EQ(pacer.StartupPauses(), 0u);
}

TEST(TimerPeriodOnlyPacer, ThePauseIsNoGapThatStartsTheGridAgain)
{
  // The shortest frame window there is: a gap of more than two frames starts the grid again, and the pause is five
  PC::PacerSettings settings = LowLatencySettings();
  settings.SetFrameWindowLength(PC::PacerSettings::MinFrameWindowLength);
  settings.SetStartupPauseDelay(Span(0));
  PC::TimerPeriodOnlyPacer pacer(settings);
  PC::FrameSchedule schedule;
  int64_t start = PresentedFrame(pacer, Start);
  start = PresentedFrame(pacer, start + 3'060'000, &schedule);
  ASSERT_EQ(schedule.NextFrameStartTime, At(Start + (6 * Period)));
  const PC::FrameStartPlan plan = pacer.PlanFrame(At(start + 3'060'000));
  EXPECT_EQ(plan.StartTime, At(Start + (6 * Period)));
  start = PresentedFrame(pacer, start + 3'060'000, &schedule);
  EXPECT_EQ(start, Start + (6 * Period));
  // The frame before it was judged, on time: a grid that started again would have an empty frame window
  EXPECT_GT(pacer.FrameWindow().Frames, 0u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  // Without the pause the same gap does start it again
  static_cast<void>(pacer.BeginFrame(At(start + (5 * Period))));
  EXPECT_EQ(pacer.FrameWindow().Frames, 0u);
}

TEST(TimerPeriodOnlyPacer, WithGpuWorkReportsAFramesWorkIsTheCpusAndTheGpus)
{
  PC::TimerPeriodOnlyPacer pacer{LowLatencySettings()};
  EXPECT_EQ(pacer.GpuTime(), FP::NanosecondTimeDuration::Zero());
  static_cast<void>(Frame(pacer, Start, 3'000'000));
  static_cast<void>(Frame(pacer, Start + 3'060'000, 3'000'000));
  EXPECT_EQ(pacer.FrameWindow().AverageWork, Span(3'000'000));

  // The GPU was done with frame 1 before frame 2 began: one after the other, the two added, and they fit
  pacer.AddGpuWork(PC::GpuWorkReport::Times(1, At(Start + 3'000'000), At(Start + 9'500'000)));
  EXPECT_EQ(pacer.GpuTime(), FP::NanosecondTimeDuration::FromNanoseconds(6'500'000));
  static_cast<void>(Frame(pacer, Start + Period + 3'060'000, 3'000'000));
  EXPECT_EQ(pacer.FrameWindow().AverageWork, Span((3'000'000 + 9'500'000) / 2));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);

  // GPU work of 0.8 periods that ends within the margin of the next frame's start: added to the CPU's it is over the frame's
  // time, and the frame is late although it kept its step
  pacer.AddGpuWork(PC::GpuWorkReport::Times(2, At(Start + Period + 3'000'000), At(Start + (2 * Period) + 1'000'000)));
  static_cast<void>(Frame(pacer, Start + (2 * Period) + 3'060'000, 3'000'000));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);

  // The same GPU work beside the CPU's work on the frame after it: the longer of the two, and it fits
  pacer.AddGpuWork(PC::GpuWorkReport::Times(3, At(Start + (2 * Period) + 5'000'000), At(Start + (3 * Period) + 3'000'000)));
  static_cast<void>(Frame(pacer, Start + (3 * Period) + 3'060'000, 3'000'000));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
  EXPECT_EQ(pacer.FrameWindow().AverageWork, Span((3'000'000 + 9'500'000 + 11'000'000 + 8'000'000) / 4));

  // A reset forgets the GPU's work
  pacer.Reset();
  EXPECT_EQ(pacer.GpuTime(), FP::NanosecondTimeDuration::Zero());
}

TEST(TimerPeriodOnlyPacer, AnApplicationThatSaysItHasTwoFramesInFlightIsJudgedByTheLongerOfTheTwo)
{
  PC::PacerSettings settings = LowLatencySettings();
  settings.SetMaxFramesInFlight(2);
  PC::TimerPeriodOnlyPacer pacer(settings);
  static_cast<void>(Frame(pacer, Start, 6'000'000));
  // How long, not when: 0.7 periods beside CPU work of 0.6
  pacer.AddGpuWork(PC::GpuWorkReport::OfDuration(1, FP::NanosecondTimeDuration::FromNanoseconds(7'000'000)));
  static_cast<void>(Frame(pacer, Start + 6'060'000, 6'000'000));
  static_cast<void>(Frame(pacer, Start + Period + 6'060'000, 6'000'000));
  EXPECT_EQ(pacer.FrameWindow().Frames, 2u);
  EXPECT_EQ(pacer.FrameWindow().AverageWork, Span(7'000'000));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
}

TEST(TimerPeriodOnlyPacer, ALossThatRepeatsIsInTheAnimationStepAndALossThatDoesNotIsNot)
{
  PC::PacerSettings settings = LowLatencySettings();
  settings.SetAutoSwapInterval(false);
  PC::TimerPeriodOnlyPacer pacer(settings);
  static_cast<void>(pacer.BeginFrame(At(Start)));

  // Every frame takes two steps of the grid at one refresh per frame. The first loss is not in the step
  PC::FrameSchedule schedule = pacer.BeginFrame(At(Start + (2 * Period)));
  EXPECT_EQ(schedule.AnimationStep, Span(Period));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 1u);
  // The second in a row is: the display shows every frame for two refreshes
  schedule = pacer.BeginFrame(At(Start + (4 * Period)));
  EXPECT_EQ(schedule.AnimationStep, Span(2 * Period));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 1u);
  schedule = pacer.BeginFrame(At(Start + (6 * Period)));
  EXPECT_EQ(schedule.AnimationStep, Span(2 * Period));
  EXPECT_EQ(schedule.TargetFrameTime, FP::NanosecondTimeDuration::FromNanoseconds(Period));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 1u);

  // A frame on time ends it at once
  schedule = pacer.BeginFrame(At(Start + (7 * Period)));
  EXPECT_EQ(schedule.AnimationStep, Span(Period));
  // One loss after a frame on time is one loss
  schedule = pacer.BeginFrame(At(Start + (9 * Period)));
  EXPECT_EQ(schedule.AnimationStep, Span(Period));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 2u);
  // Two refreshes lost after one lost: the fewer of the two is in the step
  schedule = pacer.BeginFrame(At(Start + (12 * Period)));
  EXPECT_EQ(schedule.AnimationStep, Span(2 * Period));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 3u);
  EXPECT_EQ(schedule.AnimationTime, Span(9 * Period));
}

TEST(TimerPeriodOnlyPacer, ASwapIntervalTheRuleChangesIsItsAnswerToTheLossesBeforeIt)
{
  PC::TimerPeriodOnlyPacer pacer{LowLatencySettings()};
  PC::FrameSchedule schedule = pacer.BeginFrame(At(Start));
  int64_t start = Start;
  // Every frame takes two steps of the grid until the rule slows down
  while (schedule.Change != PC::SwapIntervalChange::Slower && start < Start + (1'000 * Period))
  {
    static_cast<void>(pacer.EndFrame(At(start + 3'000'000)));
    start += 2 * Period;
    schedule = pacer.BeginFrame(At(start));
  }
  ASSERT_EQ(schedule.Change, PC::SwapIntervalChange::Slower);
  ASSERT_EQ(schedule.SwapInterval, 2u);
  // The step is the new swap interval, with nothing of the losses in it, and the frame after it is on time
  EXPECT_EQ(schedule.AnimationStep, Span(2 * Period));
  static_cast<void>(pacer.EndFrame(At(start + 3'000'000)));
  schedule = pacer.BeginFrame(At(start + (2 * Period)));
  EXPECT_EQ(schedule.AnimationStep, Span(2 * Period));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
}

// The aim of smoothness, which is the default: frames are made ahead of the display and wait to be shown, as a reserve.

namespace
{
  //! Frames with their presents reported until the loop is in its steady state with one frame made ahead: the last one started
  //! at Start + (frames - 2) periods. Returns that start.
  int64_t SteadyFrames(PC::TimerPeriodOnlyPacer& rPacer, const int64_t frames)
  {
    int64_t start = PresentedFrame(rPacer, Start);
    for (int64_t frame = 1; frame < frames; ++frame)
    {
      start = PresentedFrame(rPacer, start + 3'060'000);
    }
    return start;
  }

  //! A frame begun at startNanoseconds whose work takes workNanoseconds, with its present reported
  void LongFrame(PC::TimerPeriodOnlyPacer& rPacer, const int64_t startNanoseconds, const int64_t workNanoseconds)
  {
    static_cast<void>(rPacer.BeginFrame(At(startNanoseconds)));
    const PC::PresentPlan present = rPacer.EndFrame(At(startNanoseconds + workNanoseconds));
    PC::PresentReport report;
    report.FrameId = present.FrameId;
    report.CallTime = At(startNanoseconds + workNanoseconds);
    report.ReturnTime = At(startNanoseconds + workNanoseconds + 60'000);
    rPacer.AddPresent(report);
  }
}

TEST(TimerPeriodOnlyPacer, WithTheAimOfSmoothnessAFrameIsMadeAheadOfTheDisplayAndThereIsNoPause)
{
  // The default: smoothness, and one present that may wait beside the frame that is made
  PC::TimerPeriodOnlyPacer pacer{PC::PacerSettings(g_hz100)};
  ASSERT_EQ(pacer.Settings().Aim(), PC::PacerAim::Smoothness);

  // The first frame starts the grid, and is expected on screen a period later
  PC::FrameSchedule schedule;
  int64_t start = PresentedFrame(pacer, Start, &schedule);
  EXPECT_EQ(schedule.NextFrameStartTime, At(Start));
  EXPECT_EQ(schedule.IntendedDisplayTime, At(Start + Period));

  // The second starts at once: it is the frame made ahead, expected on screen two periods after the grid's start
  EXPECT_FALSE(pacer.PlanFrame(At(start + 3'060'000)).WaitsForStartTime());
  start = PresentedFrame(pacer, start + 3'060'000, &schedule);
  EXPECT_EQ(start, Start + 3'060'000);
  EXPECT_EQ(schedule.AnimationStep, Span(Period));
  EXPECT_EQ(schedule.NextFrameStartTime, At(Start + Period));
  EXPECT_EQ(schedule.IntendedDisplayTime, At(Start + (2 * Period)));

  // From the third on a frame starts a period before the step it is for, one per period, for as long as the loop runs
  for (int64_t frame = 2; frame < 200; ++frame)
  {
    start = PresentedFrame(pacer, start + 3'060'000, &schedule);
    ASSERT_EQ(start, Start + ((frame - 1) * Period)) << frame;
    ASSERT_EQ(schedule.IntendedDisplayTime, At(Start + ((frame + 1) * Period))) << frame;
    ASSERT_EQ(schedule.AnimationTime, Span(frame * Period)) << frame;
  }
  // No pause after start-up: that takes waiting frames away, and belongs to the aim of low latency
  EXPECT_EQ(pacer.StartupPauses(), 0u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
}

TEST(TimerPeriodOnlyPacer, WithTheAimOfSmoothnessAFrameThatRunsLongWithinTheReserveIsMadeUpFor)
{
  PC::TimerPeriodOnlyPacer pacer{PC::PacerSettings(g_hz100)};
  const int64_t start = SteadyFrames(pacer, 6);
  ASSERT_EQ(start, Start + (4 * Period));

  // The next frame, due a period later, works for 1.6 periods: the frame made ahead covers one refresh
  LongFrame(pacer, Start + (5 * Period), 16'000'000);
  // The frame after it starts at once, a step behind its own, and no step of the grid is given up
  const int64_t now = Start + (5 * Period) + 16'060'000;
  EXPECT_FALSE(pacer.PlanFrame(At(now)).WaitsForStartTime());
  PC::FrameSchedule schedule;
  int64_t next = PresentedFrame(pacer, now, &schedule);
  EXPECT_EQ(next, now);
  EXPECT_EQ(schedule.AnimationStep, Span(Period));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  // The long frame is late for the swap interval rule: its work was over its time
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);

  // And the one after that is back where the loop was: a period before its step, with the reserve made again
  EXPECT_EQ(pacer.PlanFrame(At(next + 3'060'000)).StartTime, At(Start + (7 * Period)));
  next = PresentedFrame(pacer, next + 3'060'000, &schedule);
  EXPECT_EQ(next, Start + (7 * Period));
  EXPECT_EQ(schedule.AnimationTime, Span(8 * Period));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
}

TEST(TimerPeriodOnlyPacer, WithTheAimOfSmoothnessWhatIsBeyondTheReserveIsGivenUp)
{
  PC::TimerPeriodOnlyPacer pacer{PC::PacerSettings(g_hz100)};
  static_cast<void>(SteadyFrames(pacer, 6));

  // A frame of 2.4 periods: one refresh more than the frame made ahead covers, so the display showed a frame again, once
  LongFrame(pacer, Start + (5 * Period), 24'000'000);
  const int64_t now = Start + (5 * Period) + 24'060'000;
  EXPECT_FALSE(pacer.PlanFrame(At(now)).WaitsForStartTime());
  PC::FrameSchedule schedule;
  int64_t next = PresentedFrame(pacer, now, &schedule);
  // That one step is given up, and the animation time is not moved over it
  EXPECT_EQ(pacer.RefreshesBehindClock(), 1u);
  EXPECT_EQ(schedule.AnimationStep, Span(Period));
  // The frame after it waits for its time: the loop is a period before its step again
  next = PresentedFrame(pacer, next + 3'060'000, &schedule);
  EXPECT_EQ(next, Start + (8 * Period));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 1u);
}

TEST(TimerPeriodOnlyPacer, WithTheAimOfSmoothnessALoopThatIsHeldBetweenFramesIsForgivenTheReserveToo)
{
  PC::TimerPeriodOnlyPacer pacer{PC::PacerSettings(g_hz100)};
  const int64_t start = SteadyFrames(pacer, 6);

  // The frame was presented on time, and the loop is back 2.3 periods after the next frame was due (the application waited
  // for something): one step is within the reserve, the other is given up
  const int64_t now = start + Period + 23'000'000;
  EXPECT_FALSE(pacer.PlanFrame(At(now)).WaitsForStartTime());
  PC::FrameSchedule schedule;
  int64_t next = PresentedFrame(pacer, now, &schedule);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 1u);
  // The frame after it makes up for the step within the reserve, and the one after that is a period before its step again
  EXPECT_FALSE(pacer.PlanFrame(At(next + 3'060'000)).WaitsForStartTime());
  next = PresentedFrame(pacer, next + 3'060'000, &schedule);
  EXPECT_EQ(pacer.PlanFrame(At(next + 3'060'000)).StartTime, At(start + (4 * Period)));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 1u);
}

TEST(TimerPeriodOnlyPacer, WithTheAimOfSmoothnessThereIsNoReserveAtTwoRefreshesPerFrameOrWithNoPresentThatMayWait)
{
  // Two refreshes per frame: the display takes a frame before the next one is made, so the frames are due on the grid
  // itself, held before their present as with the aim of low latency
  PC::PacerSettings settings(g_hz100);
  settings.SetPreferredSwapInterval(2);
  PC::TimerPeriodOnlyPacer slow(settings);
  PC::FrameSchedule schedule = slow.BeginFrame(At(Start));
  EXPECT_EQ(schedule.NextFrameStartTime, At(Start + (2 * Period)));
  EXPECT_EQ(schedule.IntendedDisplayTime, At(Start + (2 * Period)));
  EXPECT_EQ(slow.EndFrame(At(Start + 3'000'000)).PresentTime, At(Start + Period + 1'000'000));
  EXPECT_EQ(slow.PlanFrame(At(Start + Period + 1'060'000)).StartTime, At(Start + (2 * Period)));

  // No present may wait beside the frame that is made: no reserve, and a frame of 1.3 periods costs the step its present took
  PC::PacerSettings none(g_hz100);
  none.SetWaitingPresents(1);
  PC::TimerPeriodOnlyPacer pacer(none);
  int64_t start = PresentedFrame(pacer, Start, &schedule);
  EXPECT_EQ(schedule.NextFrameStartTime, At(Start + Period));
  start = PresentedFrame(pacer, start + 3'060'000);
  ASSERT_EQ(start, Start + Period);
  LongFrame(pacer, Start + (2 * Period), 13'000'000);
  EXPECT_EQ(pacer.PlanFrame(At(Start + (2 * Period) + 13'060'000)).StartTime, At(Start + (4 * Period)));
  static_cast<void>(pacer.BeginFrame(At(Start + (4 * Period))));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 1u);
  // A frame without an end, begun 2.4 periods after the one before it: a step given up, as it is that late for its own. It
  // is the second loss in a row, so it is in the animation step and the animation time is no further behind the clock
  schedule = pacer.BeginFrame(At(Start + (4 * Period) + 24'000'000));
  EXPECT_EQ(schedule.AnimationStep, Span(2 * Period));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 1u);
  EXPECT_EQ(pacer.StartupPauses(), 0u);
}

// Where the system holds the frame loop while its queue of frames is full (PacerSettings::SystemHoldsLoop), the aim of smoothness
// lets it pace the loop on purpose: the application reports its own waits (SystemWaitReport), a frame the display's side held is
// not late, and the grid goes with it.

namespace
{
  //! Smoothness, the system holds the loop, and a swap chain of two images: no frame can wait, so a frame is due at its step
  PC::PacerSettings HeldLoopSettings()
  {
    PC::PacerSettings settings(g_hz100);
    settings.SetSystemHoldsLoop(true);
    settings.SetSwapChainImages(2);
    return settings;
  }

  PC::SystemWaitReport SystemWait(const PC::SystemWaitKind kind, const int64_t beginNanoseconds, const int64_t endNanoseconds)
  {
    return {kind, At(beginNanoseconds), At(endNanoseconds)};
  }

  //! A frame that starts at startNanoseconds, works for 3 ms and is presented: the present returns at once
  PC::FrameSchedule MakeFrame(PC::TimerPeriodOnlyPacer& rPacer, const int64_t startNanoseconds)
  {
    const PC::FrameSchedule schedule = rPacer.BeginFrame(At(startNanoseconds));
    const PC::PresentPlan present = rPacer.EndFrame(At(startNanoseconds + 3'000'000));
    PC::PresentReport report;
    report.FrameId = present.FrameId;
    report.CallTime = At(startNanoseconds + 3'000'000);
    report.ReturnTime = At(startNanoseconds + 3'060'000);
    rPacer.AddPresent(report);
    return schedule;
  }
}

TEST(TimerPeriodOnlyPacer, WhereTheSystemHoldsTheLoopAFrameItLetThroughIsNotLateAndTheGridGoesWithIt)
{
  PC::TimerPeriodOnlyPacer pacer(HeldLoopSettings());
  PC::FrameSchedule schedule = MakeFrame(pacer, Start);
  // The loop is held to a quarter of a period before the frame is due: it is there first, and the system's wait says when the
  // frame starts
  EXPECT_EQ(schedule.NextFrameStartTime, At(Start + Period - (Period / 4)));
  EXPECT_EQ(pacer.PlanFrame(At(Start + 3'100'000)).StartTime, At(Start + Period - (Period / 4)));

  // A display 5 % slower than the pacer was told: every frame the wait for an image lets the loop go 0.05 of a period later.
  // No frame is late, no step is lost, and each frame is due a period after the one before it started
  int64_t start = Start;
  for (int32_t frame = 0; frame < 200; ++frame)
  {
    const int64_t arrived = pacer.PlanFrame(At(start + 3'100'000)).StartTime.Nanoseconds();
    ASSERT_EQ(arrived, start + Period - (Period / 4)) << frame;
    start += Period + (Period / 20);
    pacer.AddSystemWait(SystemWait(PC::SystemWaitKind::FrameSlot, arrived, arrived));
    pacer.AddSystemWait(SystemWait(PC::SystemWaitKind::Acquire, arrived, start));
    schedule = MakeFrame(pacer, start);
    ASSERT_EQ(schedule.IntendedDisplayTime, At(start + Period)) << frame;
    ASSERT_EQ(schedule.AnimationStep, g_hz100.TimeFor(1)) << frame;
  }
  EXPECT_EQ(pacer.SystemHeldFrames(), 200u);
  EXPECT_EQ(pacer.FrameSlotHeldFrames(), 0u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  EXPECT_EQ(pacer.SwapInterval(), 1u);

  // The same loop without the setting: the pacer takes the frames the system held for late ones, and gives steps up
  PC::PacerSettings unaware = HeldLoopSettings();
  unaware.SetSystemHoldsLoop(false);
  PC::TimerPeriodOnlyPacer plain(unaware);
  static_cast<void>(MakeFrame(plain, Start));
  start = Start;
  for (int32_t frame = 0; frame < 200; ++frame)
  {
    start += Period + (Period / 20);
    plain.AddSystemWait(SystemWait(PC::SystemWaitKind::Acquire, start - (Period / 2), start));
    static_cast<void>(MakeFrame(plain, start));
  }
  // The reports are counted and change nothing
  EXPECT_EQ(plain.SystemHeldFrames(), 200u);
  EXPECT_GT(plain.RefreshesBehindClock(), 5u);
}

TEST(TimerPeriodOnlyPacer, APresentThatWaitedHoldsTheLoopAsAWaitForAnImageDoes)
{
  PC::TimerPeriodOnlyPacer pacer(HeldLoopSettings());
  int64_t start = Start;
  static_cast<void>(pacer.BeginFrame(At(start)));
  for (int32_t frame = 0; frame < 50; ++frame)
  {
    // The present waits for the display, 1.6 periods after the frame's start, and the next frame starts when it returns
    PC::PresentReport report;
    report.FrameId = pacer.EndFrame(At(start + 3'000'000)).FrameId;
    report.CallTime = At(start + 3'000'000);
    report.ReturnTime = At(start + Period + (6 * Period / 10));
    pacer.AddPresent(report);
    // Nothing to wait for: the time the loop is held to has passed
    ASSERT_FALSE(pacer.PlanFrame(report.ReturnTime).WaitsForStartTime()) << frame;
    start = report.ReturnTime.Nanoseconds();
    static_cast<void>(pacer.BeginFrame(At(start)));
  }
  EXPECT_EQ(pacer.SystemHeldFrames(), 50u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
}

TEST(TimerPeriodOnlyPacer, AWaitForAFrameSlotIsTheGpusAndExcusesNothing)
{
  PC::TimerPeriodOnlyPacer pacer(HeldLoopSettings());
  static_cast<void>(MakeFrame(pacer, Start));
  // The GPU was not done with the frame before: the loop stood for 0.85 of a period, and the frame starts 0.6 of a period late
  pacer.AddSystemWait(SystemWait(PC::SystemWaitKind::FrameSlot, Start + (3 * Period / 4), Start + Period + (6 * Period / 10)));
  pacer.AddSystemWait(SystemWait(PC::SystemWaitKind::Acquire, Start + Period + (6 * Period / 10), Start + Period + (6 * Period / 10)));
  static_cast<void>(MakeFrame(pacer, Start + Period + (6 * Period / 10)));
  EXPECT_EQ(pacer.FrameSlotHeldFrames(), 1u);
  EXPECT_EQ(pacer.SystemHeldFrames(), 0u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 1u);

  // Waits add up over a frame, and one that ends before it began is none: two short ones for an image and one backwards
  const int64_t start = Start + (3 * Period);
  pacer.AddSystemWait(SystemWait(PC::SystemWaitKind::Acquire, start - 800'000, start));
  pacer.AddSystemWait(SystemWait(PC::SystemWaitKind::Acquire, start, start - (5 * Period)));
  static_cast<void>(MakeFrame(pacer, start));
  EXPECT_EQ(pacer.SystemHeldFrames(), 0u);
  pacer.AddSystemWait(SystemWait(PC::SystemWaitKind::Acquire, start + Period - 700'000, start + Period));
  pacer.AddSystemWait(SystemWait(PC::SystemWaitKind::Acquire, start + Period, start + Period + 700'000));
  static_cast<void>(MakeFrame(pacer, start + Period + 700'000));
  EXPECT_EQ(pacer.SystemHeldFrames(), 1u);
  // A wait of a day is held as the longest refresh period
  pacer.AddSystemWait(SystemWait(PC::SystemWaitKind::FrameSlot, 0, 86'400 * FP::NanosecondTimeSpan::NanosecondsPerSecond));
  pacer.AddSystemWait(SystemWait(PC::SystemWaitKind::Acquire, 0, 86'400 * FP::NanosecondTimeSpan::NanosecondsPerSecond));
  static_cast<void>(MakeFrame(pacer, start + (2 * Period) + 700'000));
  EXPECT_EQ(pacer.SystemHeldFrames(), 2u);
  EXPECT_EQ(pacer.FrameSlotHeldFrames(), 2u);
}

TEST(TimerPeriodOnlyPacer, WhereTheSystemDoesNotHoldTheLoopAfterAllTheTimerKeepsItToTheDisplaysRate)
{
  PC::TimerPeriodOnlyPacer pacer(HeldLoopSettings());
  static_cast<void>(MakeFrame(pacer, Start));
  // Every wait for an image returns at once: the frames start where the loop is held to, a quarter of a period early and a
  // period apart, and the grid stays where it is
  int64_t start = Start;
  for (int32_t frame = 1; frame <= 300; ++frame)
  {
    const PC::FrameStartPlan plan = pacer.PlanFrame(At(start + 3'100'000));
    ASSERT_EQ(plan.StartTime, At(Start + (frame * Period) - (Period / 4))) << frame;
    start = plan.StartTime.Nanoseconds();
    pacer.AddSystemWait(SystemWait(PC::SystemWaitKind::Acquire, start, start + 2'000));
    static_cast<void>(MakeFrame(pacer, start + 2'000));
  }
  EXPECT_EQ(pacer.SystemHeldFrames(), 0u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  // The frames began where they were held to, to the 2 us of the wait: the loop is in step
  EXPECT_LE(pacer.FrameWindow().StartsAhead.Duration(), Span(2'000 * int64_t{pacer.FrameWindow().Frames}));
}

TEST(TimerPeriodOnlyPacer, APresentThatWaitsForAShareOfARefreshDoesNotPaceTheLoop)
{
  // Measured on a system whose present waits for about a third of a refresh period in every frame. That wait is over before the
  // time the loop is held to, so it paces nothing: the timer does, a period apart, and the grid stays where it is
  PC::TimerPeriodOnlyPacer pacer(HeldLoopSettings());
  const auto present = [&pacer](const int64_t startNanoseconds, const int64_t returnNanoseconds)
  {
    PC::PresentReport report;
    report.FrameId = pacer.EndFrame(At(startNanoseconds + 3'000'000)).FrameId;
    report.CallTime = At(startNanoseconds + 3'000'000);
    report.ReturnTime = At(returnNanoseconds);
    pacer.AddPresent(report);
  };
  // A wait before the first frame: there is no grid yet, and nothing to hold it against
  pacer.AddSystemWait(SystemWait(PC::SystemWaitKind::Acquire, Start - Period, Start));
  int64_t start = Start;
  static_cast<void>(pacer.BeginFrame(At(start)));
  for (int32_t frame = 1; frame <= 300; ++frame)
  {
    const int64_t returned = start + 3'000'000 + (36 * Period / 100);
    present(start, returned);
    const PC::FrameStartPlan plan = pacer.PlanFrame(At(returned));
    ASSERT_EQ(plan.StartTime, At(Start + (frame * Period) - (Period / 4))) << frame;
    // The wait for an image returns at once, after the loop was held
    start = plan.StartTime.Nanoseconds() + 7'000;
    pacer.AddSystemWait(SystemWait(PC::SystemWaitKind::Acquire, start - 7'000, start));
    static_cast<void>(pacer.BeginFrame(At(start)));
  }
  // The reports say that the display's side held the loop in every frame, and it is counted; it let none of them through
  EXPECT_EQ(pacer.SystemHeldFrames(), 301u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  EXPECT_EQ(pacer.SwapInterval(), 1u);

  // A present that returns an eighth of a period after the time the loop is held to, less a nanosecond: still the timer's frame
  int64_t heldTo = Start + (301 * Period) - (Period / 4);
  present(start, heldTo + (Period / 8) - 1);
  EXPECT_FALSE(pacer.PlanFrame(At(heldTo + (Period / 8) - 1)).WaitsForStartTime());
  start = heldTo + (Period / 8) - 1;
  EXPECT_EQ(pacer.BeginFrame(At(start)).NextFrameStartTime, At(heldTo + Period));
  // An eighth of a period: the display's side let the loop through, and the grid goes to where the frame starts
  heldTo += Period;
  present(start, heldTo + (Period / 8));
  EXPECT_FALSE(pacer.PlanFrame(At(heldTo + (Period / 8))).WaitsForStartTime());
  start = heldTo + (Period / 8);
  EXPECT_EQ(pacer.BeginFrame(At(start)).NextFrameStartTime, At(start + Period - (Period / 4)));
  // A wait for an image that begins when the loop was held and takes that long is the same
  heldTo = start + Period - (Period / 4);
  present(start, start + 3'060'000);
  EXPECT_EQ(pacer.PlanFrame(At(start + 3'100'000)).StartTime, At(heldTo));
  pacer.AddSystemWait(SystemWait(PC::SystemWaitKind::Acquire, heldTo, heldTo + (3 * Period / 10)));
  start = heldTo + (3 * Period / 10);
  EXPECT_EQ(pacer.BeginFrame(At(start)).NextFrameStartTime, At(start + Period - (Period / 4)));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
}

TEST(TimerPeriodOnlyPacer, TheSystemPacesTheLoopOnlyWithTheAimOfSmoothnessAtOneRefreshPerFrame)
{
  // Low latency keeps no frames waiting, so the system has nothing to hold the loop with: the loop is held to the time the frame
  // is due, and a frame the system held is late
  PC::PacerSettings settings = HeldLoopSettings();
  settings.SetAim(PC::PacerAim::LowLatency);
  settings.SetStartupPauseRefreshes(0);
  PC::TimerPeriodOnlyPacer lowLatency(settings);
  EXPECT_EQ(MakeFrame(lowLatency, Start).NextFrameStartTime, At(Start + Period));
  lowLatency.AddSystemWait(SystemWait(PC::SystemWaitKind::Acquire, Start + Period, Start + Period + (6 * Period / 10)));
  static_cast<void>(MakeFrame(lowLatency, Start + Period + (6 * Period / 10)));
  EXPECT_EQ(lowLatency.SystemHeldFrames(), 1u);
  EXPECT_EQ(lowLatency.FrameWindow().LateFrames, 1u);

  // At two refreshes per frame the display takes a frame before the next one is made: the same
  settings = HeldLoopSettings();
  settings.SetPreferredSwapInterval(2);
  PC::TimerPeriodOnlyPacer halfRate(settings);
  EXPECT_EQ(MakeFrame(halfRate, Start).NextFrameStartTime, At(Start + (2 * Period)));
  halfRate.AddSystemWait(SystemWait(PC::SystemWaitKind::Acquire, Start + (2 * Period), Start + (3 * Period)));
  static_cast<void>(MakeFrame(halfRate, Start + (3 * Period)));
  EXPECT_EQ(halfRate.FrameWindow().LateFrames, 1u);
}

TEST(PacerSettings, TheReserveIsWhatWasAskedForAndNoMoreThanTheSwapChainHolds)
{
  PC::PacerSettings settings(g_hz100);
  // Two presents may wait by default, the frame itself counted: one frame of reserve. The swap chain's images are not known
  EXPECT_EQ(settings.SwapChainImages(), 0u);
  EXPECT_FALSE(settings.SystemHoldsLoop());
  EXPECT_EQ(settings.ReserveFrames(), 1u);
  settings.SetWaitingPresents(4);
  EXPECT_EQ(settings.ReserveFrames(), 3u);

  // One image is on screen and one is drawn into: the rest can wait, and no more is asked for
  settings.SetSwapChainImages(3);
  EXPECT_EQ(settings.SwapChainImages(), 3u);
  EXPECT_EQ(settings.ReserveFrames(), 1u);
  settings.SetSwapChainImages(8);
  EXPECT_EQ(settings.ReserveFrames(), 3u);
  settings.SetSwapChainImages(2);
  EXPECT_EQ(settings.ReserveFrames(), 0u);
  settings.SetSwapChainImages(1);
  EXPECT_EQ(settings.ReserveFrames(), 0u);

  // Where the system holds the loop the reserve is what the swap chain holds, whatever was asked for; without the images known
  // it is what was asked for
  settings.SetSystemHoldsLoop(true);
  EXPECT_TRUE(settings.SystemHoldsLoop());
  settings.SetSwapChainImages(8);
  EXPECT_EQ(settings.ReserveFrames(), 6u);
  settings.SetSwapChainImages(3);
  EXPECT_EQ(settings.ReserveFrames(), 1u);
  settings.SetSwapChainImages(0);
  EXPECT_EQ(settings.ReserveFrames(), 3u);
  EXPECT_NE(settings, PC::PacerSettings(g_hz100));

  settings.SetSwapChainImages(PC::PacerSettings::MaxSwapChainImages);
  EXPECT_EQ(settings.SwapChainImages(), 64u);
#ifdef NDEBUG
  settings.SetSwapChainImages(65);
  EXPECT_EQ(settings.SwapChainImages(), 64u);
#elif GTEST_HAS_DEATH_TEST
  EXPECT_DEATH(settings.SetSwapChainImages(65), "");
#endif
}
