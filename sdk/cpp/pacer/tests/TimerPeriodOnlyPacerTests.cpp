// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The pacer of the lowest pair of tiers (a timer, and the refresh period only): frame starts on one grid of refresh periods on the
// clock, an animation time that advances by the swap interval alone, and a frame of more than one refresh held by a wait before
// its present. What it is given is a clock's times; what it gives back the application carries out.
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeDuration.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/capability/HoldTier.hpp>
#include <mb/framepacing/pacer/capability/QueueTier.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/frame/FrameStartPlan.hpp>
#include <mb/framepacing/pacer/frame/GpuWorkReport.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <mb/framepacing/pacer/tier/TimerPeriodOnlyPacer.hpp>
#include <gtest/gtest.h>
#include <cstdint>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

namespace
{
  // 100 Hz: a refresh period of exactly 100,000 ticks
  constexpr int64_t Period = 100'000;
  constexpr int64_t Start = 10'000'000;

  const PC::RefreshPeriod g_hz100 = PC::RefreshPeriod::FromRate(100);

  constexpr FP::TickCount64 At(const int64_t ticks) noexcept
  {
    return FP::TickCount64(ticks);
  }

  constexpr FP::TimeSpan Span(const int64_t ticks) noexcept
  {
    return FP::TimeSpan(ticks);
  }

  //! A frame as an application makes it: planned at now, begun at the time it is given (or at once), with CPU work of workTicks.
  //! Returns the frame's start.
  int64_t Frame(PC::TimerPeriodOnlyPacer& rPacer, const int64_t nowTicks, const int64_t workTicks, PC::FrameSchedule* pSchedule = nullptr)
  {
    const PC::FrameStartPlan plan = rPacer.PlanFrame(At(nowTicks));
    const int64_t startTicks = plan.WaitsForStartTime() ? plan.StartTime.Ticks() : nowTicks;
    const PC::FrameSchedule schedule = rPacer.BeginFrame(At(startTicks));
    static_cast<void>(rPacer.EndFrame(At(startTicks + workTicks)));
    if (pSchedule != nullptr)
    {
      *pSchedule = schedule;
    }
    return startTicks;
  }

  //! A frame as Frame makes it, with CPU work of 30,000 ticks and its present reported: taken by the system or not. Returns
  //! the frame's start.
  int64_t PresentedFrame(PC::TimerPeriodOnlyPacer& rPacer, const int64_t nowTicks, PC::FrameSchedule* pSchedule = nullptr, const bool accepted = true)
  {
    const PC::FrameStartPlan plan = rPacer.PlanFrame(At(nowTicks));
    const int64_t startTicks = plan.WaitsForStartTime() ? plan.StartTime.Ticks() : nowTicks;
    const PC::FrameSchedule schedule = rPacer.BeginFrame(At(startTicks));
    const PC::PresentPlan present = rPacer.EndFrame(At(startTicks + 30'000));
    PC::PresentReport report;
    report.FrameId = present.FrameId;
    report.CallTime = At(startTicks + 30'000);
    report.ReturnTime = At(startTicks + 30'600);
    report.Accepted = accepted;
    rPacer.AddPresent(report);
    if (pSchedule != nullptr)
    {
      *pSchedule = schedule;
    }
    return startTicks;
  }

  static_assert(PC::TimerPeriodOnlyPacer::Hold == PC::HoldTier::Timer);
  static_assert(PC::TimerPeriodOnlyPacer::Queue == PC::QueueTier::PeriodOnly);
}

TEST(TimerPeriodOnlyPacer, TheFirstFrameStartsAtOnceAndStartsTheGrid)
{
  PC::TimerPeriodOnlyPacer pacer{PC::PacerSettings(g_hz100)};

  EXPECT_FALSE(pacer.PlanFrame(At(Start)).WaitsForStartTime());
  EXPECT_FALSE(pacer.PlanFrame(At(Start)).WaitsForPresent());
  const PC::FrameSchedule schedule = pacer.BeginFrame(At(Start));
  EXPECT_EQ(schedule.FrameId, 1u);
  EXPECT_EQ(schedule.SwapInterval, 1u);
  EXPECT_EQ(schedule.AnimationTime, Span(0));
  EXPECT_EQ(schedule.AnimationStep, Span(0));
  EXPECT_EQ(schedule.NextFrameStartTime, At(Start + Period));
  EXPECT_EQ(schedule.IntendedDisplayTime, At(Start + Period));
  EXPECT_EQ(schedule.TargetFrameTime, FP::TimeSpan32(Period));
  EXPECT_EQ(schedule.PreferredFrameTime, FP::TimeSpan32(Period));
  EXPECT_EQ(schedule.Change, PC::SwapIntervalChange::Unchanged);

  const PC::PresentPlan present = pacer.EndFrame(At(Start + 30'000));
  EXPECT_EQ(present.FrameId, 1u);
  EXPECT_EQ(present.CpuBusy, FP::TimeSpan32(30'000));
  // A frame of one refresh is presented when it is done, with nothing for a present that takes values
  EXPECT_FALSE(present.WaitsForPresentTime());
  EXPECT_EQ(present.SwapInterval, 1u);
  EXPECT_EQ(present.NotBeforeTime, FP::TickCount64());
  EXPECT_EQ(present.MinimumDuration, FP::TimeDuration::Zero());
}

TEST(TimerPeriodOnlyPacer, EveryFrameIsDueAWholeNumberOfPeriodsAfterTheFirst)
{
  PC::TimerPeriodOnlyPacer pacer{PC::PacerSettings(g_hz100)};
  static_cast<void>(Frame(pacer, Start, 30'000));

  for (int64_t frame = 1; frame < 500; ++frame)
  {
    // The loop is back a fifth of a period after the frame before it started
    const int64_t now = Start + ((frame - 1) * Period) + 20'000;
    const PC::FrameStartPlan plan = pacer.PlanFrame(At(now));
    ASSERT_EQ(plan.StartTime, At(Start + (frame * Period))) << frame;
    // Planned again, it is the same plan
    ASSERT_EQ(pacer.PlanFrame(At(now + 5'000)).StartTime, plan.StartTime) << frame;
    // The wait wakes a little late
    const PC::FrameSchedule schedule = pacer.BeginFrame(At(plan.StartTime.Ticks() + 300));
    ASSERT_EQ(schedule.FrameId, static_cast<uint64_t>(frame + 1));
    ASSERT_EQ(schedule.AnimationTime, Span(frame * Period)) << frame;
    ASSERT_EQ(schedule.AnimationStep, Span(Period)) << frame;
    ASSERT_EQ(schedule.NextFrameStartTime, At(Start + ((frame + 1) * Period))) << frame;
    static_cast<void>(pacer.EndFrame(At(plan.StartTime.Ticks() + 30'300)));
  }
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  EXPECT_EQ(pacer.SwapInterval(), 1u);
}

TEST(TimerPeriodOnlyPacer, AStartThatIsLateCostsThatFrameAndNoFrameAfterIt)
{
  PC::TimerPeriodOnlyPacer pacer{PC::PacerSettings(g_hz100)};
  static_cast<void>(Frame(pacer, Start, 30'000));

  // The loop comes back 0.4 of a period after the second frame was due: it starts at once and keeps its step
  const int64_t late = Start + Period + 40'000;
  EXPECT_FALSE(pacer.PlanFrame(At(late)).WaitsForStartTime());
  const PC::FrameSchedule second = pacer.BeginFrame(At(late));
  EXPECT_EQ(second.AnimationStep, Span(Period));
  // The frame after it is due where it would have been, not a period after the late start
  EXPECT_EQ(second.NextFrameStartTime, At(Start + (2 * Period)));
  static_cast<void>(pacer.EndFrame(At(late + 30'000)));
  EXPECT_EQ(pacer.PlanFrame(At(late + 30'600)).StartTime, At(Start + (2 * Period)));
  static_cast<void>(Frame(pacer, late + 30'600, 30'000));

  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  // It shows as a frame that began after its time
  EXPECT_EQ(pacer.FrameWindow().StartsAhead, Span(-40'000));
}

TEST(TimerPeriodOnlyPacer, AFrameThatRanLongCostsWholeStepsAndTheLoopIsBackOnTheGrid)
{
  PC::TimerPeriodOnlyPacer pacer{PC::PacerSettings(g_hz100)};
  static_cast<void>(Frame(pacer, Start, 30'000));

  // The second frame works for 2.25 periods: it is done 3.25 periods after the grid's start, no later in its step than the
  // frame before it was (0.3 of a period)
  const int64_t secondStart = Frame(pacer, Start + 30'600, 225'000);
  ASSERT_EQ(secondStart, Start + Period);
  const int64_t now = secondStart + 225'600;
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
  static_cast<void>(pacer.EndFrame(At(Start + (4 * Period) + 30'000)));

  // And every frame after it is on the grid it was on before
  for (int64_t frame = 5; frame < 50; ++frame)
  {
    ASSERT_EQ(Frame(pacer, Start + ((frame - 1) * Period) + 30'600, 30'000), Start + (frame * Period)) << frame;
  }
  EXPECT_EQ(pacer.RefreshesBehindClock(), 2u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
}

TEST(TimerPeriodOnlyPacer, AfterALatePresentTheNextPresentComesAWholePeriodLater)
{
  // Frames are presented 0.3 of a period into their step. A frame of 2.6 periods on step 1 is done 3.6 periods after the
  // grid's start: later in its step than a present is
  PC::TimerPeriodOnlyPacer pacer{PC::PacerSettings(g_hz100)};
  static_cast<void>(Frame(pacer, Start, 30'000));
  static_cast<void>(Frame(pacer, Start + 30'600, 30'000));
  static_cast<void>(Frame(pacer, Start + Period + 30'600, 260'000));
  // The next step would put the next present 0.7 of a period after this one, and the two could reach the display between
  // the same two refreshes: the step after it, the sixth
  const int64_t now = Start + (2 * Period) + 260'600;
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
  PC::TimerPeriodOnlyPacer pacer{PC::PacerSettings(g_hz100)};
  static_cast<void>(Frame(pacer, Start, 30'000));
  static_cast<void>(Frame(pacer, Start + 30'600, 30'000));

  // Work of 1.3 periods on step 2, and no present report: made when the work was done, 0.3 into step 3, where a present is.
  // The next step
  static_cast<void>(pacer.BeginFrame(At(Start + (2 * Period))));
  const PC::PresentPlan present = pacer.EndFrame(At(Start + (2 * Period) + 130'000));
  EXPECT_EQ(pacer.PlanFrame(At(Start + (2 * Period) + 136'000)).StartTime, At(Start + (4 * Period)));

  // A report of another frame changes nothing
  PC::PresentReport report;
  report.FrameId = present.FrameId - 1u;
  report.CallTime = At(Start + (2 * Period) + 180'000);
  report.ReturnTime = report.CallTime;
  pacer.AddPresent(report);
  EXPECT_EQ(pacer.PlanFrame(At(Start + (2 * Period) + 136'000)).StartTime, At(Start + (4 * Period)));
  // The report of this frame says the present was called 0.8 into step 3 (the application did something in between): the
  // step after the next
  report.FrameId = present.FrameId;
  pacer.AddPresent(report);
  EXPECT_EQ(pacer.PlanFrame(At(Start + (2 * Period) + 181'000)).StartTime, At(Start + (5 * Period)));

  // A report before the frame's work is done is of no frame
  static_cast<void>(pacer.BeginFrame(At(Start + (5 * Period))));
  report.FrameId = present.FrameId + 1u;
  report.CallTime = At(Start + (9 * Period));
  pacer.AddPresent(report);
  static_cast<void>(pacer.EndFrame(At(Start + (5 * Period) + 30'000)));
  EXPECT_EQ(pacer.PlanFrame(At(Start + (5 * Period) + 31'000)).StartTime, At(Start + (6 * Period)));
}

TEST(TimerPeriodOnlyPacer, ALoopThatComesBackLateAfterAPresentOnTimeKeepsItsStep)
{
  PC::TimerPeriodOnlyPacer pacer{PC::PacerSettings(g_hz100)};
  static_cast<void>(Frame(pacer, Start, 30'000));
  static_cast<void>(Frame(pacer, Start + 30'600, 30'000));

  // The frame was presented on time and the loop is back 0.3 of a period after the next frame was due: it starts at once, on
  // its step, and the frame after it is due a period after that step
  const int64_t now = Start + (2 * Period) + 30'000;
  EXPECT_FALSE(pacer.PlanFrame(At(now)).WaitsForStartTime());
  const PC::FrameSchedule schedule = pacer.BeginFrame(At(now));
  EXPECT_EQ(schedule.NextFrameStartTime, At(Start + (3 * Period)));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
}

TEST(TimerPeriodOnlyPacer, AFrameWhoseWorkIsOverItsTimeIsLateAndCostsAStep)
{
  PC::TimerPeriodOnlyPacer pacer{PC::PacerSettings(g_hz100)};
  static_cast<void>(Frame(pacer, Start, 30'000));

  // Work of 1.3 periods: its present is made in the step the next frame was due at, and the next frame takes the step after
  static_cast<void>(Frame(pacer, Start + 30'600, 130'000));
  const int64_t now = Start + Period + 130'600;
  EXPECT_EQ(pacer.PlanFrame(At(now)).StartTime, At(Start + (3 * Period)));
  static_cast<void>(pacer.BeginFrame(At(Start + (3 * Period))));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 1u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
}

TEST(TimerPeriodOnlyPacer, FramesThatKeepRunningLongMakeTheRuleSlowDownAndTheAnimationFallBehindUntilItDoes)
{
  PC::TimerPeriodOnlyPacer pacer{PC::PacerSettings(g_hz100)};
  int64_t now = Start;
  int64_t frames = 0;
  // Work of 1.3 periods every frame, at one refresh per frame
  while (pacer.SwapInterval() == 1 && frames < 2'000)
  {
    now = Frame(pacer, now, 130'000) + 130'600;
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
    now = Frame(pacer, now, 130'000, &schedule) + 130'600;
    ASSERT_EQ(schedule.SwapInterval, 2u);
  }
  EXPECT_EQ(schedule.AnimationStep, Span(2 * Period));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  // The first frame at the new swap interval is the last that starts off its step
  EXPECT_LE(pacer.RefreshesBehindClock(), behind + 1u);
}

TEST(TimerPeriodOnlyPacer, AFrameOfMoreThanOneRefreshIsHeldByAWaitBeforeItsPresent)
{
  PC::PacerSettings settings(g_hz100);
  settings.SetPreferredFrameRate(25);
  PC::TimerPeriodOnlyPacer pacer(settings);

  const PC::FrameSchedule first = pacer.BeginFrame(At(Start));
  EXPECT_EQ(first.SwapInterval, 4u);
  EXPECT_EQ(first.NextFrameStartTime, At(Start + (4 * Period)));
  EXPECT_EQ(first.TargetFrameTime, FP::TimeSpan32(4 * Period));
  // Presented in the period before the step the next frame is due at, the frame margin (1 ms here) into it
  const PC::PresentPlan present = pacer.EndFrame(At(Start + 30'000));
  EXPECT_EQ(settings.FrameMargin(), Span(10'000));
  EXPECT_EQ(present.PresentTime, At(Start + (3 * Period) + 10'000));
  // The present has no swap interval at this tier: the loop holds the frame
  EXPECT_EQ(present.SwapInterval, 1u);

  // The next frame is due four periods after the first, and its animation time is four periods on
  const PC::FrameStartPlan plan = pacer.PlanFrame(At(Start + (3 * Period) + 10'600));
  EXPECT_EQ(plan.StartTime, At(Start + (4 * Period)));
  const PC::FrameSchedule second = pacer.BeginFrame(plan.StartTime);
  EXPECT_EQ(second.AnimationStep, Span(4 * Period));
  // Work that is done after the time to present at is presented at once
  EXPECT_FALSE(pacer.EndFrame(At(Start + (7 * Period) + 20'000)).WaitsForPresentTime());
  EXPECT_FALSE(pacer.EndFrame(At(Start + (7 * Period) + 10'000)).WaitsForPresentTime());
  EXPECT_TRUE(pacer.EndFrame(At(Start + (7 * Period) + 9'999)).WaitsForPresentTime());
}

TEST(TimerPeriodOnlyPacer, APauseOrAClockThatWentBackStartsTheGridAgain)
{
  PC::TimerPeriodOnlyPacer pacer{PC::PacerSettings(g_hz100)};
  int64_t now = Start;
  for (int32_t frame = 0; frame < 20; ++frame)
  {
    now = Frame(pacer, now, 30'000) + 30'600;
  }
  ASSERT_EQ(pacer.FrameWindow().Frames, 19u);

  // A minute later: no wait, a new grid from this start, an empty frame window, and no refresh counted as lost
  const int64_t later = now + (60 * FP::TimeSpan::TicksPerSecond);
  EXPECT_FALSE(pacer.PlanFrame(At(later)).WaitsForStartTime());
  const PC::FrameSchedule resumed = pacer.BeginFrame(At(later));
  EXPECT_EQ(resumed.NextFrameStartTime, At(later + Period));
  // The animation time goes on by the frame's swap interval
  EXPECT_EQ(resumed.AnimationStep, Span(Period));
  EXPECT_EQ(pacer.FrameWindow().Frames, 0u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  static_cast<void>(pacer.EndFrame(At(later + 30'000)));

  // A start before the frame before it: the same
  EXPECT_FALSE(pacer.PlanFrame(At(later - 1)).WaitsForStartTime());
  const PC::FrameSchedule back = pacer.BeginFrame(At(later - 1));
  EXPECT_EQ(back.NextFrameStartTime, At(later - 1 + Period));
  EXPECT_EQ(pacer.FrameWindow().Frames, 0u);
}

TEST(TimerPeriodOnlyPacer, AFrameWithoutAnEndIsNotJudgedByItsWork)
{
  PC::TimerPeriodOnlyPacer pacer{PC::PacerSettings(g_hz100)};
  static_cast<void>(pacer.BeginFrame(At(Start)));
  // No EndFrame, and the next frame starts 1.4 periods later: a step, not late by work, and its work is the time between
  static_cast<void>(pacer.BeginFrame(At(Start + 140'000)));
  EXPECT_EQ(pacer.FrameWindow().Frames, 1u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  EXPECT_EQ(pacer.FrameWindow().AverageWork, Span(140'000));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
}

TEST(TimerPeriodOnlyPacer, EndFrameWithoutAFrameIsNothingAndAPresentReportIsKept)
{
  PC::TimerPeriodOnlyPacer pacer{PC::PacerSettings(g_hz100)};

  const PC::PresentPlan nothing = pacer.EndFrame(At(Start));
  EXPECT_EQ(nothing.FrameId, 0u);
  EXPECT_FALSE(nothing.WaitsForPresentTime());
  EXPECT_EQ(pacer.LastPresentBlocked(), FP::TimeDuration::Zero());

  static_cast<void>(pacer.BeginFrame(At(Start)));
  static_cast<void>(pacer.EndFrame(At(Start + 30'000)));
  PC::PresentReport report;
  report.FrameId = 1;
  report.CallTime = At(Start + 30'000);
  report.ReturnTime = At(Start + 30'600);
  pacer.AddPresent(report);
  EXPECT_EQ(pacer.LastPresentBlocked(), FP::TimeDuration::FromTicks(600));
}

TEST(TimerPeriodOnlyPacer, TheCpuBusyTimeCanBeAskedForWhileTheFrameIsOpen)
{
  PC::TimerPeriodOnlyPacer pacer{PC::PacerSettings(g_hz100)};
  EXPECT_EQ(pacer.CpuBusyAt(At(Start)), FP::TimeSpan32());
  static_cast<void>(pacer.BeginFrame(At(Start)));
  // Where a marker is drawn before the frame's work is done
  EXPECT_EQ(pacer.CpuBusyAt(At(Start + 12'000)), FP::TimeSpan32(12'000));
  EXPECT_EQ(pacer.CpuBusyAt(At(Start - 1)), FP::TimeSpan32());
  pacer.Reset();
  EXPECT_EQ(pacer.CpuBusyAt(At(Start + 12'000)), FP::TimeSpan32());
}

TEST(TimerPeriodOnlyPacer, AnotherRefreshPeriodOrOtherSettingsStartTheGridAgainAndTheAnimationTimeGoesOn)
{
  PC::PacerSettings settings(g_hz100);
  PC::TimerPeriodOnlyPacer pacer(settings);
  int64_t now = Start;
  for (int32_t frame = 0; frame < 10; ++frame)
  {
    now = Frame(pacer, now, 30'000) + 30'600;
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
  static_cast<void>(pacer.EndFrame(At(now + 30'000)));

  // Other settings: the same, at the swap interval they prefer
  settings.SetRefresh(hz50);
  settings.SetPreferredFrameRate(25);
  pacer.SetSettings(settings);
  EXPECT_FALSE(pacer.PlanFrame(At(now + 30'600)).WaitsForStartTime());
  schedule = pacer.BeginFrame(At(now + 30'600));
  EXPECT_EQ(schedule.SwapInterval, 2u);
  EXPECT_EQ(schedule.NextFrameStartTime, At(now + 30'600 + (4 * Period)));
  static_cast<void>(pacer.EndFrame(At(now + 60'600)));

  // Reset: the next frame starts at once on a grid of its own, and a frame that was open is not ended afterwards
  pacer.Reset();
  EXPECT_FALSE(pacer.PlanFrame(At(now + 61'000)).WaitsForStartTime());
  EXPECT_EQ(pacer.EndFrame(At(now + 61'000)).FrameId, 0u);
  schedule = pacer.BeginFrame(At(now + 61'000));
  EXPECT_EQ(schedule.NextFrameStartTime, At(now + 61'000 + (4 * Period)));
}

TEST(TimerPeriodOnlyPacer, HalfASecondAfterStartUpTheLoopPausesOnceAndTheFrameOnScreenStays)
{
  PC::TimerPeriodOnlyPacer pacer{PC::PacerSettings(g_hz100)};
  PC::FrameSchedule schedule;
  int64_t now = Start;
  for (int64_t frame = 0; frame < 50; ++frame)
  {
    const int64_t start = PresentedFrame(pacer, now, &schedule);
    ASSERT_EQ(start, Start + (frame * Period));
    ASSERT_EQ(schedule.NextFrameStartTime, At(start + Period));
    now = start + 30'600;
  }
  EXPECT_EQ(pacer.StartupPauses(), 0u);

  // The frame that starts half a second after the first is presented as any other, and the frame after it is due four
  // refreshes later than it would be
  int64_t start = PresentedFrame(pacer, now, &schedule);
  EXPECT_EQ(start, Start + (50 * Period));
  EXPECT_EQ(schedule.AnimationStep, Span(Period));
  EXPECT_EQ(schedule.IntendedDisplayTime, At(Start + (51 * Period)));
  EXPECT_EQ(schedule.NextFrameStartTime, At(Start + (55 * Period)));
  EXPECT_EQ(schedule.TargetFrameTime, FP::TimeSpan32(Period));
  EXPECT_EQ(pacer.StartupPauses(), 1u);
  EXPECT_EQ(pacer.PlanFrame(At(start + 30'600)).StartTime, At(Start + (55 * Period)));

  // The frame after the pause is on time, its animation time a swap interval on: the pause is not caught up with
  start = PresentedFrame(pacer, start + 30'600, &schedule);
  EXPECT_EQ(start, Start + (55 * Period));
  EXPECT_EQ(schedule.AnimationStep, Span(Period));
  EXPECT_EQ(schedule.AnimationTime, Span(51 * Period));
  EXPECT_EQ(schedule.NextFrameStartTime, At(Start + (56 * Period)));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 4u);

  // Once: never again in this run
  for (int64_t frame = 0; frame < 300; ++frame)
  {
    const int64_t next = PresentedFrame(pacer, start + 30'600, &schedule);
    ASSERT_EQ(next, start + Period);
    start = next;
  }
  EXPECT_EQ(pacer.StartupPauses(), 1u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 4u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
}

TEST(TimerPeriodOnlyPacer, ThereIsNoPauseBeforeAPresentWasTaken)
{
  PC::PacerSettings settings(g_hz100);
  settings.SetStartupPauseDelay(Span(0));
  PC::TimerPeriodOnlyPacer pacer(settings);

  // No present is reported: nothing is on its way to the screen, however long it takes
  int64_t now = Start;
  for (int32_t frame = 0; frame < 80; ++frame)
  {
    now = Frame(pacer, now, 30'000) + 30'600;
  }
  EXPECT_EQ(pacer.StartupPauses(), 0u);

  // A frame whose present is taken: the pause is made at the frame after it, also with no delay at all
  PC::FrameSchedule schedule;
  int64_t start = PresentedFrame(pacer, now, &schedule);
  EXPECT_EQ(schedule.NextFrameStartTime, At(start + Period));
  EXPECT_EQ(pacer.StartupPauses(), 0u);
  start = PresentedFrame(pacer, start + 30'600, &schedule);
  EXPECT_EQ(schedule.NextFrameStartTime, At(start + (5 * Period)));
  EXPECT_EQ(pacer.StartupPauses(), 1u);
}

TEST(TimerPeriodOnlyPacer, ASwapChainMadeAnewAndAResetGetThePauseOfAStart)
{
  PC::PacerSettings settings(g_hz100);
  settings.SetStartupPauseDelay(Span(2 * Period));
  settings.SetStartupPauseRefreshes(3);
  PC::TimerPeriodOnlyPacer pacer(settings);
  PC::FrameSchedule schedule;

  // Frames at 0, 1 and 2 periods: the third is two periods after the first
  int64_t start = PresentedFrame(pacer, Start);
  start = PresentedFrame(pacer, start + 30'600);
  start = PresentedFrame(pacer, start + 30'600, &schedule);
  EXPECT_EQ(schedule.NextFrameStartTime, At(Start + (6 * Period)));
  EXPECT_EQ(pacer.StartupPauses(), 1u);
  start = PresentedFrame(pacer, start + 30'600, &schedule);
  ASSERT_EQ(start, Start + (6 * Period));

  // The application made its swap chain anew: counted from the next frame, with a present of the new one taken
  pacer.ForgetPresents();
  start = PresentedFrame(pacer, start + 30'600);
  start = PresentedFrame(pacer, start + 30'600);
  EXPECT_EQ(pacer.StartupPauses(), 1u);
  start = PresentedFrame(pacer, start + 30'600, &schedule);
  ASSERT_EQ(start, Start + (9 * Period));
  EXPECT_EQ(schedule.NextFrameStartTime, At(Start + (13 * Period)));
  EXPECT_EQ(pacer.StartupPauses(), 2u);

  // A present the system did not take says the same
  start = PresentedFrame(pacer, start + 30'600, &schedule, false);
  ASSERT_EQ(start, Start + (13 * Period));
  start = PresentedFrame(pacer, start + 30'600);
  start = PresentedFrame(pacer, start + 30'600);
  EXPECT_EQ(pacer.StartupPauses(), 2u);
  start = PresentedFrame(pacer, start + 30'600, &schedule);
  ASSERT_EQ(start, Start + (16 * Period));
  EXPECT_EQ(schedule.NextFrameStartTime, At(Start + (20 * Period)));
  EXPECT_EQ(pacer.StartupPauses(), 3u);

  // And so does a reset
  pacer.Reset();
  start = PresentedFrame(pacer, Start + (30 * Period));
  start = PresentedFrame(pacer, start + 30'600);
  start = PresentedFrame(pacer, start + 30'600, &schedule);
  ASSERT_EQ(start, Start + (32 * Period));
  EXPECT_EQ(schedule.NextFrameStartTime, At(Start + (36 * Period)));
  EXPECT_EQ(pacer.StartupPauses(), 4u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 12u);
}

TEST(TimerPeriodOnlyPacer, AtTwoRefreshesPerFrameOrWithAPauseOfNoRefreshesThereIsNone)
{
  // Two refreshes per frame when the pause is due: the display took what waited, and the pause is not made later either
  PC::PacerSettings settings(g_hz100);
  settings.SetStartupPauseDelay(Span(2 * Period));
  settings.SetPreferredSwapInterval(2);
  PC::TimerPeriodOnlyPacer slow(settings);
  PC::FrameSchedule schedule;
  int64_t start = Start;
  for (int64_t frame = 0; frame < 20; ++frame)
  {
    start = PresentedFrame(slow, start + (frame == 0 ? 0 : Period + 10'600), &schedule);
    ASSERT_EQ(start, Start + (frame * 2 * Period));
    ASSERT_EQ(schedule.NextFrameStartTime, At(start + (2 * Period)));
  }
  EXPECT_EQ(slow.StartupPauses(), 0u);
  EXPECT_EQ(slow.RefreshesBehindClock(), 0u);

  // A pause of no refreshes is no pause
  PC::PacerSettings none(g_hz100);
  none.SetStartupPauseDelay(Span(0));
  none.SetStartupPauseRefreshes(0);
  PC::TimerPeriodOnlyPacer pacer(none);
  start = Start;
  for (int64_t frame = 0; frame < 20; ++frame)
  {
    start = PresentedFrame(pacer, start + (frame == 0 ? 0 : 30'600), &schedule);
    ASSERT_EQ(schedule.NextFrameStartTime, At(Start + ((frame + 1) * Period)));
  }
  EXPECT_EQ(pacer.StartupPauses(), 0u);
}

TEST(TimerPeriodOnlyPacer, ThePauseIsNoGapThatStartsTheGridAgain)
{
  // The shortest frame window there is: a gap of more than two frames starts the grid again, and the pause is five
  PC::PacerSettings settings(g_hz100);
  settings.SetFrameWindowLength(PC::PacerSettings::MinFrameWindowLength);
  settings.SetStartupPauseDelay(Span(0));
  PC::TimerPeriodOnlyPacer pacer(settings);
  PC::FrameSchedule schedule;
  int64_t start = PresentedFrame(pacer, Start);
  start = PresentedFrame(pacer, start + 30'600, &schedule);
  ASSERT_EQ(schedule.NextFrameStartTime, At(Start + (6 * Period)));
  const PC::FrameStartPlan plan = pacer.PlanFrame(At(start + 30'600));
  EXPECT_EQ(plan.StartTime, At(Start + (6 * Period)));
  start = PresentedFrame(pacer, start + 30'600, &schedule);
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
  PC::TimerPeriodOnlyPacer pacer{PC::PacerSettings(g_hz100)};
  EXPECT_EQ(pacer.GpuTime(), FP::TimeDuration::Zero());
  static_cast<void>(Frame(pacer, Start, 30'000));
  static_cast<void>(Frame(pacer, Start + 30'600, 30'000));
  EXPECT_EQ(pacer.FrameWindow().AverageWork, Span(30'000));

  // The GPU was done with frame 1 before frame 2 began: one after the other, the two added, and they fit
  pacer.AddGpuWork(PC::GpuWorkReport::Times(1, At(Start + 30'000), At(Start + 95'000)));
  EXPECT_EQ(pacer.GpuTime(), FP::TimeDuration::FromTicks(65'000));
  static_cast<void>(Frame(pacer, Start + Period + 30'600, 30'000));
  EXPECT_EQ(pacer.FrameWindow().AverageWork, Span((30'000 + 95'000) / 2));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);

  // GPU work of 0.8 periods that ends within the margin of the next frame's start: added to the CPU's it is over the frame's
  // time, and the frame is late although it kept its step
  pacer.AddGpuWork(PC::GpuWorkReport::Times(2, At(Start + Period + 30'000), At(Start + (2 * Period) + 10'000)));
  static_cast<void>(Frame(pacer, Start + (2 * Period) + 30'600, 30'000));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);

  // The same GPU work beside the CPU's work on the frame after it: the longer of the two, and it fits
  pacer.AddGpuWork(PC::GpuWorkReport::Times(3, At(Start + (2 * Period) + 50'000), At(Start + (3 * Period) + 30'000)));
  static_cast<void>(Frame(pacer, Start + (3 * Period) + 30'600, 30'000));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
  EXPECT_EQ(pacer.FrameWindow().AverageWork, Span((30'000 + 95'000 + 110'000 + 80'000) / 4));

  // A reset forgets the GPU's work
  pacer.Reset();
  EXPECT_EQ(pacer.GpuTime(), FP::TimeDuration::Zero());
}

TEST(TimerPeriodOnlyPacer, AnApplicationThatSaysItHasTwoFramesInFlightIsJudgedByTheLongerOfTheTwo)
{
  PC::PacerSettings settings(g_hz100);
  settings.SetMaxFramesInFlight(2);
  PC::TimerPeriodOnlyPacer pacer(settings);
  static_cast<void>(Frame(pacer, Start, 60'000));
  // How long, not when: 0.7 periods beside CPU work of 0.6
  pacer.AddGpuWork(PC::GpuWorkReport::OfDuration(1, FP::TimeDuration::FromTicks(70'000)));
  static_cast<void>(Frame(pacer, Start + 60'600, 60'000));
  static_cast<void>(Frame(pacer, Start + Period + 60'600, 60'000));
  EXPECT_EQ(pacer.FrameWindow().Frames, 2u);
  EXPECT_EQ(pacer.FrameWindow().AverageWork, Span(70'000));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
}

TEST(TimerPeriodOnlyPacer, ALossThatRepeatsIsInTheAnimationStepAndALossThatDoesNotIsNot)
{
  PC::PacerSettings settings(g_hz100);
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
  EXPECT_EQ(schedule.TargetFrameTime, FP::TimeSpan32(Period));
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
  PC::TimerPeriodOnlyPacer pacer{PC::PacerSettings(g_hz100)};
  PC::FrameSchedule schedule = pacer.BeginFrame(At(Start));
  int64_t start = Start;
  // Every frame takes two steps of the grid until the rule slows down
  while (schedule.Change != PC::SwapIntervalChange::Slower && start < Start + (1'000 * Period))
  {
    static_cast<void>(pacer.EndFrame(At(start + 30'000)));
    start += 2 * Period;
    schedule = pacer.BeginFrame(At(start));
  }
  ASSERT_EQ(schedule.Change, PC::SwapIntervalChange::Slower);
  ASSERT_EQ(schedule.SwapInterval, 2u);
  // The step is the new swap interval, with nothing of the losses in it, and the frame after it is on time
  EXPECT_EQ(schedule.AnimationStep, Span(2 * Period));
  static_cast<void>(pacer.EndFrame(At(start + 30'000)));
  schedule = pacer.BeginFrame(At(start + (2 * Period)));
  EXPECT_EQ(schedule.AnimationStep, Span(2 * Period));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
}
