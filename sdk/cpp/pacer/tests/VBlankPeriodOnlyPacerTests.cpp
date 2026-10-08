// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The pacer of vertical blank times: every frame is for one vertical blank of the display, made early with its present held
// (smoothness) or started so that it is ready just in time (low latency). What it is given is a clock's times; what it gives back
// the application carries out.
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
#include <mb/framepacing/pacer/frame/VBlankReading.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <mb/framepacing/pacer/tier/VBlankPeriodOnlyPacer.hpp>
#include <gtest/gtest.h>
#include <cstdint>
#include <cstdlib>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

namespace
{
  // 100 Hz: a refresh period of exactly 10,000,000 ns, a frame margin of 1,000,000 (1 ms), and a frame is to be ready 5,000,000 into
  // a refresh. Vertical blank n is at Blank(n)
  constexpr int64_t Period = 10'000'000;
  constexpr int64_t Margin = 1'000'000;
  constexpr int64_t Place = 5'000'000;
  constexpr int64_t Start = 1'000'000'000;
  constexpr int64_t Work = 3'000'000;

  const PC::RefreshPeriod g_hz100 = PC::RefreshPeriod::FromRate(100);

  constexpr FP::NanosecondTickCount At(const int64_t nanoseconds) noexcept
  {
    return FP::NanosecondTickCount(nanoseconds);
  }

  constexpr FP::NanosecondTimeSpan Span(const int64_t nanoseconds) noexcept
  {
    return FP::NanosecondTimeSpan(nanoseconds);
  }

  constexpr int64_t Blank(const int64_t number) noexcept
  {
    return Start + (number * Period);
  }

  PC::PacerSettings Settings(const PC::PacerAim aim, const uint32_t waitingPresents = 2)
  {
    PC::PacerSettings settings(g_hz100);
    settings.SetAim(aim);
    settings.SetWaitingPresents(waitingPresents);
    // The pause after start-up has tests of its own
    settings.SetStartupPauseRefreshes(0);
    return settings;
  }

  //! A vertical blank of the display, read when it was
  void AddBlank(PC::VBlankPeriodOnlyPacer& rPacer, const int64_t blankNanoseconds)
  {
    PC::VBlankReading reading;
    reading.VBlankTime = At(blankNanoseconds);
    reading.ReadTime = At(blankNanoseconds);
    rPacer.AddVBlank(reading);
  }

  //! What a frame's calls gave
  struct FrameResult
  {
    int64_t StartNanoseconds{0};
    int64_t PresentNanoseconds{0};
    PC::FrameSchedule Schedule;
  };

  //! A frame as an application makes it: planned at now, begun at the time it is given (or at once), with CPU work of workNanoseconds,
  //! presented at the time it is given (or when the work is done), and the present reported
  FrameResult Frame(PC::VBlankPeriodOnlyPacer& rPacer, const int64_t nowNanoseconds, const int64_t workNanoseconds = Work, const bool accepted = true)
  {
    FrameResult result;
    const PC::FrameStartPlan plan = rPacer.PlanFrame(At(nowNanoseconds));
    result.StartNanoseconds = plan.WaitsForStartTime() ? plan.StartTime.Nanoseconds() : nowNanoseconds;
    result.Schedule = rPacer.BeginFrame(At(result.StartNanoseconds));
    const PC::PresentPlan present = rPacer.EndFrame(At(result.StartNanoseconds + workNanoseconds));
    result.PresentNanoseconds = present.WaitsForPresentTime() ? present.PresentTime.Nanoseconds() : result.StartNanoseconds + workNanoseconds;
    PC::PresentReport report;
    report.FrameId = present.FrameId;
    report.CallTime = At(result.PresentNanoseconds);
    report.ReturnTime = At(result.PresentNanoseconds + 60'000);
    report.Accepted = accepted;
    rPacer.AddPresent(report);
    return result;
  }

  static_assert(PC::VBlankPeriodOnlyPacer::Tier == PC::PacerTier::VBlankPeriodOnly);
}

TEST(VBlankPeriodOnlyPacer, UntilAReadingTheFirstFramesStartIsTakenAsAVerticalBlank)
{
  PC::VBlankPeriodOnlyPacer pacer(Settings(PC::PacerAim::LowLatency));
  EXPECT_FALSE(pacer.HasVBlankReading());
  EXPECT_FALSE(pacer.PlanFrame(At(Start)).WaitsForStartTime());
  EXPECT_FALSE(pacer.PlanFrame(At(Start)).WaitsForPresent());

  // The first frame is for the first vertical blank it can be ready for, the frame margin before it: the one a period on
  const PC::FrameSchedule schedule = pacer.BeginFrame(At(Start));
  EXPECT_EQ(schedule.FrameId, 1u);
  EXPECT_EQ(schedule.SwapInterval, 1u);
  EXPECT_EQ(schedule.AnimationTime, Span(0));
  EXPECT_EQ(schedule.AnimationStep, Span(0));
  EXPECT_EQ(schedule.IntendedDisplayTime, At(Blank(1)));
  EXPECT_EQ(schedule.TargetFrameTime, FP::NanosecondTimeDuration::FromNanoseconds(Period));
  EXPECT_EQ(schedule.PreferredFrameTime, FP::NanosecondTimeDuration::FromNanoseconds(Period));
  EXPECT_EQ(schedule.Change, PC::SwapIntervalChange::Unchanged);
  EXPECT_EQ(pacer.Refresh(), g_hz100);
}

TEST(VBlankPeriodOnlyPacer, WithLowLatencyAFramesStartIsHeldSoThatItIsReadyAtItsPlaceAndNoSooner)
{
  PC::VBlankPeriodOnlyPacer pacer(Settings(PC::PacerAim::LowLatency));
  AddBlank(pacer, Blank(0));
  EXPECT_TRUE(pacer.HasVBlankReading());

  // The first frame starts at once, for the vertical blank it can make, and is presented when it is done
  FrameResult frame = Frame(pacer, Blank(0) + 100'000);
  EXPECT_EQ(frame.StartNanoseconds, Blank(0) + 100'000);
  EXPECT_EQ(frame.PresentNanoseconds, Blank(0) + 100'000 + Work);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(1)));
  // When a frame starts, how long it takes is not known yet: the schedule's time for the next frame is what it would be if
  // it took no time at all, and the plan for that frame has it right
  EXPECT_EQ(frame.Schedule.NextFrameStartTime, At(Blank(1) + Place - Margin));

  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  EXPECT_EQ(frame.StartNanoseconds, Blank(1) + Place - Margin - Work);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(2)));
  EXPECT_EQ(frame.Schedule.AnimationStep, Span(Period));
  // From now on a frame's work is known, and the start is that much sooner
  EXPECT_EQ(frame.Schedule.NextFrameStartTime, At(Blank(2) + Place - Margin - Work));

  for (int64_t number = 3; number < 100; ++number)
  {
    frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
    ASSERT_EQ(frame.StartNanoseconds, Blank(number - 1) + Place - Margin - Work) << number;
    // Ready the margin before its place, and presented at once
    ASSERT_EQ(frame.PresentNanoseconds, Blank(number - 1) + Place - Margin) << number;
    ASSERT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(number))) << number;
    ASSERT_EQ(frame.Schedule.AnimationTime, Span((number - 1) * Period)) << number;
  }
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  // The loop is in step with the times it is given
  EXPECT_EQ(pacer.FrameWindow().StartsAhead, Span(0));
}

TEST(VBlankPeriodOnlyPacer, WithSmoothnessAFrameStartsAtOnceAndItsPresentIsHeld)
{
  // No present may wait beside the frame that is made: no reserve
  PC::VBlankPeriodOnlyPacer pacer(Settings(PC::PacerAim::Smoothness, 1));
  AddBlank(pacer, Blank(0));

  FrameResult frame = Frame(pacer, Blank(0) + 100'000);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(1)));
  // Done at 0.31 of the refresh, and presented at its place
  EXPECT_EQ(frame.PresentNanoseconds, Blank(0) + Place);
  // The frame after it starts when this one's present is made
  EXPECT_EQ(frame.Schedule.NextFrameStartTime, At(Blank(0) + Place));

  for (int64_t number = 2; number < 100; ++number)
  {
    // No time to wait for before the frame
    ASSERT_FALSE(pacer.PlanFrame(At(frame.PresentNanoseconds + 60'000)).WaitsForStartTime()) << number;
    frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
    ASSERT_EQ(frame.StartNanoseconds, Blank(number - 2) + Place + 60'000) << number;
    ASSERT_EQ(frame.PresentNanoseconds, Blank(number - 1) + Place) << number;
    ASSERT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(number))) << number;
    ASSERT_EQ(frame.Schedule.AnimationStep, Span(Period)) << number;
  }
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  EXPECT_EQ(pacer.StartupPauses(), 0u);
}

TEST(VBlankPeriodOnlyPacer, WithSmoothnessTheReserveIsMadeAtTheStartAndCoversAFrameThatRunsLong)
{
  // One present may wait beside the frame that is made: every frame is ready a refresh before the one it is shown in
  PC::VBlankPeriodOnlyPacer pacer(Settings(PC::PacerAim::Smoothness, 2));
  AddBlank(pacer, Blank(0));

  // The first two frames are presented when they are done: the second is the one made ahead
  FrameResult frame = Frame(pacer, Blank(0) + 100'000);
  EXPECT_EQ(frame.PresentNanoseconds, Blank(0) + 100'000 + Work);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(1)));
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  EXPECT_EQ(frame.PresentNanoseconds, frame.StartNanoseconds + Work);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(2)));
  // From the third on a frame is presented at its place, two refreshes before the vertical blank it is for
  for (int64_t number = 3; number < 10; ++number)
  {
    frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
    ASSERT_EQ(frame.PresentNanoseconds, Blank(number - 2) + Place) << number;
    ASSERT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(number))) << number;
  }

  // Frame 10 works for 1.8 periods: it is ready 0.3 into the refresh before its vertical blank, and still shown at it
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000, 18'000'000);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(10)));
  EXPECT_EQ(frame.PresentNanoseconds, Blank(9) + 3'060'000);
  // The frame after it is made at once and presented when it is done: the reserve is being made again
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(11)));
  EXPECT_EQ(frame.Schedule.AnimationStep, Span(Period));
  EXPECT_EQ(frame.PresentNanoseconds, frame.StartNanoseconds + Work);
  // And the one after that waits for its place again
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  EXPECT_EQ(frame.PresentNanoseconds, Blank(10) + Place);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(12)));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  // The long frame is late for the swap interval rule: its work was over its time
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
}

TEST(VBlankPeriodOnlyPacer, AFrameThatMissedItsVerticalBlankIsNotCaughtUpWith)
{
  PC::VBlankPeriodOnlyPacer pacer(Settings(PC::PacerAim::LowLatency));
  AddBlank(pacer, Blank(0));
  FrameResult frame = Frame(pacer, Blank(0) + 100'000);
  for (int64_t number = 2; number <= 5; ++number)
  {
    frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  }
  ASSERT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(5)));
  ASSERT_EQ(frame.StartNanoseconds, Blank(4) + Place - Margin - Work);

  // Frame 6, for vertical blank 6, works for 2.3 periods: done 0.4 into the refresh after that blank, no later in it than a frame
  // is meant to be ready, so it is shown at the next one, 8
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000, 23'000'000);
  ASSERT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(6)));
  ASSERT_EQ(frame.PresentNanoseconds, Blank(7) + 4'000'000);

  // The frame after it starts at once: the pacer now expects a frame to take a period at most, and its place is that near
  const PC::FrameStartPlan plan = pacer.PlanFrame(At(frame.PresentNanoseconds + 60'000));
  EXPECT_FALSE(plan.WaitsForStartTime());
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  // It is for vertical blank 9, its swap interval after the one the long frame was shown at, and its animation step is its
  // swap interval: the two refreshes that were lost are not stepped over
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(9)));
  EXPECT_EQ(frame.Schedule.AnimationStep, Span(Period));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 2u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
  // Done before the refresh it is to be ready in begins, its present waits for that refresh
  EXPECT_EQ(frame.PresentNanoseconds, Blank(8));

  // Eight frames later the long frame is forgotten, and a frame starts as late as before it
  for (int32_t count = 0; count < 9; ++count)
  {
    frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  }
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(18)));
  EXPECT_EQ(frame.StartNanoseconds, Blank(17) + Place - Margin - Work);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 2u);
}

TEST(VBlankPeriodOnlyPacer, WithLowLatencyALateFrameThatWasReadyLateInItsRefreshIsNotCountedOnForTheNextVerticalBlank)
{
  PC::VBlankPeriodOnlyPacer pacer(Settings(PC::PacerAim::LowLatency));
  AddBlank(pacer, Blank(0));
  FrameResult frame = Frame(pacer, Blank(0) + 100'000);
  for (int64_t number = 2; number <= 5; ++number)
  {
    frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  }

  // Frame 6 works for 2.7 periods: done 0.8 into the refresh after its vertical blank, later in it than a frame is meant to be
  // ready. It may have made vertical blank 8 or not: it is taken as shown at 9
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000, 27'000'000);
  ASSERT_EQ(frame.PresentNanoseconds, Blank(7) + 8'000'000);
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(10)));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 3u);

  // With the aim of smoothness the same frame is taken as shown at 8, the vertical blank it was ready for
  PC::VBlankPeriodOnlyPacer smooth(Settings(PC::PacerAim::Smoothness, 1));
  AddBlank(smooth, Blank(0));
  frame = Frame(smooth, Blank(0) + 100'000);
  for (int64_t number = 2; number <= 5; ++number)
  {
    frame = Frame(smooth, frame.PresentNanoseconds + 60'000);
  }
  ASSERT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(5)));
  // Started 0.506 into the refresh of vertical blank 4: 3.3 periods of work end 0.806 into the refresh of vertical blank 7
  frame = Frame(smooth, frame.PresentNanoseconds + 60'000, 33'000'000);
  ASSERT_EQ(frame.PresentNanoseconds, Blank(7) + 8'060'000);
  frame = Frame(smooth, frame.PresentNanoseconds + 60'000);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(9)));
  EXPECT_EQ(smooth.RefreshesBehindClock(), 2u);
}

TEST(VBlankPeriodOnlyPacer, AFrameThatStartsTooLateForItsVerticalBlankIsForTheFirstItCanMakeAndItsAnimationTimeIsForThat)
{
  PC::VBlankPeriodOnlyPacer pacer(Settings(PC::PacerAim::LowLatency));
  AddBlank(pacer, Blank(0));
  FrameResult frame = Frame(pacer, Blank(0) + 100'000);
  for (int64_t number = 2; number <= 5; ++number)
  {
    frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  }
  ASSERT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(5)));

  // The application is held for 2.6 periods before the next frame (something of its own). The frame was to be for vertical
  // blank 6; it is back 0.8 into the refresh before 8, too late to be ready for it, so it is for 9, and its start is held
  // so that it is ready for that one just in time
  const int64_t now = Blank(7) + 8'000'000;
  EXPECT_EQ(pacer.PlanFrame(At(now)).StartTime, At(Blank(8) + Place - Margin - Work));
  frame = Frame(pacer, now);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(9)));
  // Known before the frame is made: its animation step is the four refreshes to the vertical blank it is shown at, and the
  // animation time is not behind
  EXPECT_EQ(frame.Schedule.AnimationStep, Span(4 * Period));
  EXPECT_EQ(frame.Schedule.TargetFrameTime, FP::NanosecondTimeDuration::FromNanoseconds(Period));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);

  // It counts as a late frame for the swap interval rule, once the frame is judged
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(10)));
  EXPECT_EQ(frame.Schedule.AnimationStep, Span(Period));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
}

TEST(VBlankPeriodOnlyPacer, TheFramesFollowTheReadingsAndKeepTheVerticalBlanksTheyAreFor)
{
  PC::VBlankPeriodOnlyPacer pacer(Settings(PC::PacerAim::LowLatency));
  AddBlank(pacer, Blank(0));
  FrameResult frame = Frame(pacer, Blank(0) + 100'000);
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  ASSERT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(2)));

  // The display is a little slower than its period: vertical blank 2 was 40 us later than the pacer had it. One reading is
  // not exact, so the vertical blanks are moved a quarter of the way to it
  AddBlank(pacer, Blank(2) + 40'000);
  EXPECT_EQ(pacer.VBlankJumps(), 0u);
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(3) + 10'000));
  EXPECT_EQ(frame.StartNanoseconds, Blank(2) + 10'000 + Place - Margin - Work);
  EXPECT_EQ(frame.Schedule.AnimationStep, Span(Period));
  // The readings after it move them the rest of the way, to a few nanoseconds: a display that is off its period is followed
  for (int64_t number = 3; number < 40; ++number)
  {
    AddBlank(pacer, Blank(number) + 40'000);
  }
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  EXPECT_LE(std::abs(frame.Schedule.IntendedDisplayTime.Nanoseconds() - (Blank(4) + 40'000)), 3);

  // A reading of a vertical blank that is still to come is the same to it, and one that was read before the reading the pacer
  // has is not taken
  AddBlank(pacer, Blank(45) + 40'000);
  PC::VBlankReading old;
  old.VBlankTime = At(Blank(3) + 1'000'000);
  old.ReadTime = At(Blank(3));
  pacer.AddVBlank(old);
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  EXPECT_LE(std::abs(frame.Schedule.IntendedDisplayTime.Nanoseconds() - (Blank(5) + 40'000)), 3);
  EXPECT_EQ(pacer.VBlankJumps(), 0u);
}

TEST(VBlankPeriodOnlyPacer, AReadingThatIsOffTheGridIsNotTakenByItself)
{
  PC::VBlankPeriodOnlyPacer pacer(Settings(PC::PacerAim::LowLatency));
  AddBlank(pacer, Blank(0));
  FrameResult frame = Frame(pacer, Blank(0) + 100'000);
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  ASSERT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(2)));

  // A reading far off where the ones before it put the vertical blanks is counted and not taken: 0.3 of a period early here.
  // The frames go on where they were
  AddBlank(pacer, Blank(3) - 3'000'000);
  EXPECT_EQ(pacer.VBlankJumps(), 1u);
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(3)));
  // Nor are readings that are off every time and on no grid of their own, as many as there are: a source that is no vertical
  // blank time. The frames go on by the refresh period from the last reading that was taken
  for (int64_t number = 4; number < 60; ++number)
  {
    AddBlank(pacer, Blank(number) + ((number % 3) == 0 ? 4'000'000 : ((number % 3) == 1 ? -2'000'000 : 3'500'000)));
  }
  // The same reading again is no second one
  AddBlank(pacer, Blank(59) + 3'500'000);
  EXPECT_EQ(pacer.VBlankJumps(), 58u);
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(4)));
  // A reading on the grid again is taken as before
  AddBlank(pacer, Blank(60) + 40'000);
  EXPECT_EQ(pacer.VBlankJumps(), 58u);
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(5) + 10'000));

  // Eight readings in a row that are off the pacer's grid and on one of their own are the display's, which has changed: the
  // eighth moves the frames to it, whole
  for (int64_t number = 61; number < 68; ++number)
  {
    AddBlank(pacer, Blank(number) - 3'000'000);
  }
  EXPECT_EQ(pacer.VBlankJumps(), 65u);
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(6) + 10'000));
  AddBlank(pacer, Blank(69) - 3'000'000);
  EXPECT_EQ(pacer.VBlankJumps(), 66u);
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  EXPECT_EQ((frame.Schedule.IntendedDisplayTime.Nanoseconds() - (Start - 3'000'000)) % Period, 0);
  AddBlank(pacer, Blank(70) - 3'000'000);
  EXPECT_EQ(pacer.VBlankJumps(), 66u);
}

TEST(VBlankPeriodOnlyPacer, AReadingAfterTheFirstFramesMovesTheGuessOntoTheDisplay)
{
  // No reading at first: the first frame's start is taken as a vertical blank
  PC::VBlankPeriodOnlyPacer pacer(Settings(PC::PacerAim::Smoothness, 1));
  FrameResult frame = Frame(pacer, Start);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(1)));
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  ASSERT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(2)));

  // The display's vertical blanks are 0.3 of a period after where the guess had them. No jump: there was no reading before
  AddBlank(pacer, Blank(1) + 3'000'000);
  EXPECT_TRUE(pacer.HasVBlankReading());
  EXPECT_EQ(pacer.VBlankJumps(), 0u);
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(3) + 3'000'000));
  EXPECT_EQ(frame.PresentNanoseconds, Blank(2) + 3'000'000 + Place);
  EXPECT_EQ(frame.Schedule.AnimationStep, Span(Period));
}

TEST(VBlankPeriodOnlyPacer, WithLowLatencyThePauseAfterStartUpIsInTheNextFramesAnimationStep)
{
  PC::PacerSettings settings = Settings(PC::PacerAim::LowLatency);
  settings.SetStartupPauseDelay(Span(2 * Period));
  settings.SetStartupPauseRefreshes(3);
  PC::VBlankPeriodOnlyPacer pacer(settings);
  AddBlank(pacer, Blank(0));

  FrameResult frame = Frame(pacer, Blank(0) + 100'000);
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  EXPECT_EQ(pacer.StartupPauses(), 0u);
  // The third frame starts two periods after the first: the frame after it is for a vertical blank three later than it
  // would be
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  ASSERT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(3)));
  EXPECT_EQ(pacer.StartupPauses(), 1u);
  EXPECT_EQ(frame.Schedule.NextFrameStartTime, At(Blank(6) + Place - Margin - Work));
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  EXPECT_EQ(frame.StartNanoseconds, Blank(6) + Place - Margin - Work);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(7)));
  // The pacer knows the frame before it stays on screen for four refreshes: that is the step, and nothing is behind
  EXPECT_EQ(frame.Schedule.AnimationStep, Span(4 * Period));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(8)));
  EXPECT_EQ(frame.Schedule.AnimationStep, Span(Period));

  // A swap chain made anew, a present the system did not take and a reset each get the pause of a start
  pacer.ForgetPresents();
  for (int32_t count = 0; count < 4; ++count)
  {
    frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  }
  EXPECT_EQ(pacer.StartupPauses(), 2u);
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000, Work, false);
  for (int32_t count = 0; count < 4; ++count)
  {
    frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  }
  EXPECT_EQ(pacer.StartupPauses(), 3u);
  pacer.Reset();
  for (int32_t count = 0; count < 4; ++count)
  {
    frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  }
  EXPECT_EQ(pacer.StartupPauses(), 4u);
}

TEST(VBlankPeriodOnlyPacer, ThereIsNoPauseWithSmoothnessNorAtTwoRefreshesPerFrameNorBeforeAPresentWasTaken)
{
  PC::PacerSettings settings = Settings(PC::PacerAim::Smoothness, 2);
  settings.SetStartupPauseDelay(Span(0));
  PC::VBlankPeriodOnlyPacer smooth(settings);
  AddBlank(smooth, Blank(0));
  FrameResult frame = Frame(smooth, Blank(0) + 100'000);
  for (int32_t count = 0; count < 10; ++count)
  {
    frame = Frame(smooth, frame.PresentNanoseconds + 60'000);
  }
  EXPECT_EQ(smooth.StartupPauses(), 0u);

  settings.SetAim(PC::PacerAim::LowLatency);
  settings.SetPreferredSwapInterval(2);
  PC::VBlankPeriodOnlyPacer slow(settings);
  AddBlank(slow, Blank(0));
  frame = Frame(slow, Blank(0) + 100'000);
  for (int32_t count = 0; count < 10; ++count)
  {
    frame = Frame(slow, frame.PresentNanoseconds + 60'000);
  }
  EXPECT_EQ(slow.StartupPauses(), 0u);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(21)));

  // No present reported: nothing is on its way to the screen
  settings.SetPreferredSwapInterval(1);
  PC::VBlankPeriodOnlyPacer unreported(settings);
  AddBlank(unreported, Blank(0));
  for (int64_t number = 0; number < 10; ++number)
  {
    static_cast<void>(unreported.BeginFrame(At(Blank(number) + 100'000)));
    static_cast<void>(unreported.EndFrame(At(Blank(number) + 100'000 + Work)));
  }
  EXPECT_EQ(unreported.StartupPauses(), 0u);
  // A pause of no refreshes is no pause
  settings.SetStartupPauseRefreshes(0);
  PC::VBlankPeriodOnlyPacer none(settings);
  AddBlank(none, Blank(0));
  frame = Frame(none, Blank(0) + 100'000);
  for (int32_t count = 0; count < 5; ++count)
  {
    frame = Frame(none, frame.PresentNanoseconds + 60'000);
  }
  EXPECT_EQ(none.StartupPauses(), 0u);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(6)));
}

TEST(VBlankPeriodOnlyPacer, WithGpuWorkReportsAFrameIsReadyWhenTheGpuIsDoneWithIt)
{
  PC::VBlankPeriodOnlyPacer pacer(Settings(PC::PacerAim::LowLatency));
  AddBlank(pacer, Blank(0));
  EXPECT_EQ(pacer.GpuTime(), FP::NanosecondTimeDuration::Zero());
  FrameResult frame = Frame(pacer, Blank(0) + 100'000);
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  // The GPU worked on the first frame for a fifth of a period
  pacer.AddGpuWork(PC::GpuWorkReport::Times(1, At(Blank(0) + 3'100'000), At(Blank(0) + 5'100'000)));
  EXPECT_EQ(pacer.GpuTime(), FP::NanosecondTimeDuration::FromNanoseconds(2'000'000));
  // A frame starts that much sooner, so that the GPU is done with it the margin before its place
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  EXPECT_EQ(frame.StartNanoseconds, Blank(2) + Place - Margin - Work - 2'000'000);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(3)));

  // With the aim of smoothness the present is that much sooner
  PC::VBlankPeriodOnlyPacer smooth(Settings(PC::PacerAim::Smoothness, 1));
  AddBlank(smooth, Blank(0));
  frame = Frame(smooth, Blank(0) + 100'000);
  smooth.AddGpuWork(PC::GpuWorkReport::Times(1, At(Blank(0) + 5'100'000), At(Blank(0) + 7'100'000)));
  frame = Frame(smooth, frame.PresentNanoseconds + 60'000);
  EXPECT_EQ(frame.PresentNanoseconds, Blank(1) + Place - 2'000'000);

  // A reset forgets the GPU's work
  smooth.Reset();
  EXPECT_EQ(smooth.GpuTime(), FP::NanosecondTimeDuration::Zero());
}

TEST(VBlankPeriodOnlyPacer, FramesOfMoreThanOneRefreshAreForEveryNthVerticalBlank)
{
  // 25 frames a second at 100 Hz: four refreshes per frame
  PC::PacerSettings settings = Settings(PC::PacerAim::LowLatency);
  settings.SetPreferredFrameRate(25);
  PC::VBlankPeriodOnlyPacer pacer(settings);
  AddBlank(pacer, Blank(0));
  FrameResult frame = Frame(pacer, Blank(0) + 100'000);
  EXPECT_EQ(frame.Schedule.SwapInterval, 4u);
  EXPECT_EQ(frame.Schedule.TargetFrameTime, FP::NanosecondTimeDuration::FromNanoseconds(4 * Period));
  for (int64_t number = 1; number < 20; ++number)
  {
    frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
    ASSERT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(1 + (4 * number)))) << number;
    ASSERT_EQ(frame.Schedule.AnimationStep, Span(4 * Period)) << number;
  }
  // Low latency: started in the refresh before its vertical blank and presented at once, not made four refreshes ahead
  EXPECT_EQ(frame.StartNanoseconds, Blank(76) + Place - Margin - Work);
  EXPECT_EQ(frame.PresentNanoseconds, Blank(76) + Place - Margin);

  // Smoothness: made at once, and its present held to its place in the refresh before its vertical blank. No reserve: the
  // display takes a frame before the next one is made
  settings.SetAim(PC::PacerAim::Smoothness);
  PC::VBlankPeriodOnlyPacer smooth(settings);
  AddBlank(smooth, Blank(0));
  frame = Frame(smooth, Blank(0) + 100'000);
  frame = Frame(smooth, frame.PresentNanoseconds + 60'000);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(5)));
  EXPECT_EQ(frame.StartNanoseconds, Blank(0) + Place + 60'000);
  EXPECT_EQ(frame.PresentNanoseconds, Blank(4) + Place);
}

TEST(VBlankPeriodOnlyPacer, FramesThatKeepMissingTheirVerticalBlankMakeTheRuleSlowDown)
{
  PC::VBlankPeriodOnlyPacer pacer(Settings(PC::PacerAim::LowLatency));
  AddBlank(pacer, Blank(0));
  FrameResult frame = Frame(pacer, Blank(0) + 100'000, 13'000'000);
  int32_t frames = 0;
  // Work of 1.3 periods every frame, at one refresh per frame
  while (pacer.SwapInterval() == 1 && frames < 2'000)
  {
    frame = Frame(pacer, frame.PresentNanoseconds + 60'000, 13'000'000);
    ++frames;
  }
  ASSERT_EQ(pacer.SwapInterval(), 2u);
  EXPECT_LT(frames, 400);
  EXPECT_GT(pacer.RefreshesBehindClock(), 0u);

  // At two refreshes per frame the work fits: every frame at its vertical blank
  const uint64_t behind = pacer.RefreshesBehindClock();
  for (int32_t count = 0; count < 100; ++count)
  {
    frame = Frame(pacer, frame.PresentNanoseconds + 60'000, 13'000'000);
    ASSERT_EQ(frame.Schedule.SwapInterval, 2u);
  }
  EXPECT_EQ(frame.Schedule.AnimationStep, Span(2 * Period));
  EXPECT_LE(pacer.RefreshesBehindClock(), behind + 2u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
}

TEST(VBlankPeriodOnlyPacer, APauseOrAClockThatWentBackStartsTheFramesAgainAndTheReadingsAreKept)
{
  PC::VBlankPeriodOnlyPacer pacer(Settings(PC::PacerAim::LowLatency));
  AddBlank(pacer, Blank(0));
  FrameResult frame = Frame(pacer, Blank(0) + 100'000);
  for (int32_t count = 0; count < 20; ++count)
  {
    frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  }
  ASSERT_EQ(pacer.FrameWindow().Frames, 20u);

  // A minute later: no wait, the first vertical blank the frame can make (where the readings put it: a frame takes 0.3 of a
  // period, so not the one 0.2 of a period away), an empty frame window, nothing counted as lost, and the animation time a
  // swap interval on
  const int64_t later = Blank(6'021) + 8'000'000;
  EXPECT_FALSE(pacer.PlanFrame(At(later)).WaitsForStartTime());
  PC::FrameSchedule resumed = pacer.BeginFrame(At(later));
  EXPECT_EQ(resumed.IntendedDisplayTime, At(Blank(6'023)));
  EXPECT_EQ(resumed.AnimationStep, Span(Period));
  EXPECT_EQ(pacer.FrameWindow().Frames, 0u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  EXPECT_TRUE(pacer.HasVBlankReading());

  // A start before the frame before it: the same
  resumed = pacer.BeginFrame(At(later - 10'000'000));
  EXPECT_EQ(resumed.IntendedDisplayTime, At(Blank(6'022)));
  EXPECT_EQ(pacer.FrameWindow().Frames, 0u);
}

TEST(VBlankPeriodOnlyPacer, AFrameWithoutAnEndOrAPresentReportIsJudgedByWhatIsKnown)
{
  PC::VBlankPeriodOnlyPacer pacer(Settings(PC::PacerAim::Smoothness, 1));
  AddBlank(pacer, Blank(0));

  // EndFrame without a frame is nothing
  const PC::PresentPlan nothing = pacer.EndFrame(At(Start));
  EXPECT_EQ(nothing.FrameId, 0u);
  EXPECT_FALSE(nothing.WaitsForPresentTime());
  EXPECT_EQ(pacer.CpuBusyAt(At(Start)), FP::NanosecondTimeDuration());
  EXPECT_EQ(pacer.LastPresentBlocked(), FP::NanosecondTimeDuration::Zero());

  // No EndFrame: the frame is taken as shown at its vertical blank, and its work is the time to the next start
  static_cast<void>(pacer.BeginFrame(At(Blank(0) + 100'000)));
  EXPECT_EQ(pacer.CpuBusyAt(At(Blank(0) + 1'300'000)), FP::NanosecondTimeDuration::FromNanoseconds(1'200'000));
  EXPECT_EQ(pacer.CpuBusyAt(At(Blank(0))), FP::NanosecondTimeDuration());
  PC::FrameSchedule schedule = pacer.BeginFrame(At(Blank(0) + 14'100'000));
  EXPECT_EQ(pacer.FrameWindow().Frames, 1u);
  EXPECT_EQ(pacer.FrameWindow().AverageWork, Span(14'000'000));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  EXPECT_EQ(schedule.IntendedDisplayTime, At(Blank(2)));

  // An end and no present report: presented when the pacer said, or when the work was done
  PC::PresentPlan present = pacer.EndFrame(At(Blank(0) + 14'400'000));
  EXPECT_EQ(present.PresentTime, At(Blank(1) + Place));
  EXPECT_EQ(present.CpuBusy, FP::NanosecondTimeDuration::FromNanoseconds(300'000));
  // A report of another frame says nothing of this one; the report of this frame says when it was
  PC::PresentReport report;
  report.FrameId = present.FrameId + 5u;
  report.CallTime = At(Blank(9));
  report.ReturnTime = At(Blank(9) + 70'000);
  pacer.AddPresent(report);
  EXPECT_EQ(pacer.LastPresentBlocked(), FP::NanosecondTimeDuration::FromNanoseconds(70'000));
  schedule = pacer.BeginFrame(At(Blank(1) + Place + 60'000));
  EXPECT_EQ(schedule.IntendedDisplayTime, At(Blank(3)));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);

  // A report before the frame's work is done is of no frame
  report.FrameId = schedule.FrameId;
  pacer.AddPresent(report);
  present = pacer.EndFrame(At(Blank(1) + Place + 3'060'000));
  schedule = pacer.BeginFrame(At(Blank(2) + Place + 60'000));
  EXPECT_EQ(schedule.IntendedDisplayTime, At(Blank(4)));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
}

TEST(VBlankPeriodOnlyPacer, AnotherRefreshPeriodDropsTheReadingsAndOtherSettingsKeepThemOnTheSameDisplay)
{
  PC::PacerSettings settings = Settings(PC::PacerAim::LowLatency);
  PC::VBlankPeriodOnlyPacer pacer(settings);
  AddBlank(pacer, Blank(0));
  FrameResult frame = Frame(pacer, Blank(0) + 100'000);
  for (int32_t count = 0; count < 5; ++count)
  {
    frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  }
  EXPECT_EQ(pacer.Refresh(), g_hz100);
  EXPECT_EQ(pacer.Settings(), settings);
  EXPECT_EQ(pacer.SwapInterval(), 1u);

  // The same period and the same settings change nothing: the next frame has its time
  pacer.SetRefreshPeriod(g_hz100);
  pacer.SetSettings(settings);
  EXPECT_TRUE(pacer.PlanFrame(At(frame.PresentNanoseconds + 60'000)).WaitsForStartTime());

  // Other settings on the same display: the frames start again, and where its vertical blanks are is kept
  settings.SetReadyPlacePercent(80);
  pacer.SetSettings(settings);
  EXPECT_TRUE(pacer.HasVBlankReading());
  EXPECT_FALSE(pacer.PlanFrame(At(Blank(7) + 2'000'000)).WaitsForStartTime());
  frame = Frame(pacer, Blank(7) + 2'000'000);
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(Blank(8)));
  EXPECT_EQ(frame.Schedule.AnimationStep, Span(Period));
  // Ready 0.8 into the refresh now
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  EXPECT_EQ(frame.StartNanoseconds, Blank(8) + 8'000'000 - Margin - Work);

  // 50 Hz: another display, or another mode. The readings are not of it, and a frame's step is the new period
  const PC::RefreshPeriod hz50 = PC::RefreshPeriod::FromRate(50);
  pacer.SetRefreshPeriod(hz50);
  EXPECT_FALSE(pacer.HasVBlankReading());
  EXPECT_EQ(pacer.Refresh(), hz50);
  frame = Frame(pacer, frame.PresentNanoseconds + 60'000);
  EXPECT_EQ(frame.Schedule.AnimationStep, Span(2 * Period));
  EXPECT_EQ(frame.Schedule.IntendedDisplayTime, At(frame.StartNanoseconds + (2 * Period)));

  // Settings with another period: the same
  AddBlank(pacer, frame.StartNanoseconds);
  ASSERT_TRUE(pacer.HasVBlankReading());
  pacer.SetSettings(Settings(PC::PacerAim::LowLatency));
  EXPECT_FALSE(pacer.HasVBlankReading());
  EXPECT_EQ(pacer.Refresh(), g_hz100);
}
