// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The pacer of a timer with a wait for a present: before a frame it asks for a wait until the present a set number back was shown,
// it is told what became of the wait, and its grid on the clock follows the display through the ends of the waits that held the
// loop. The rest is TimerPeriodOnlyPacer's, which TimerPeriodOnlyPacerTests has; here is what differs, and that the rest still
// holds.
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
#include <mb/framepacing/pacer/frame/PresentWaitReport.hpp>
#include <mb/framepacing/pacer/tier/TimerWaitForPresentPacer.hpp>
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

  PC::PacerSettings Settings(const uint32_t waitingPresents)
  {
    PC::PacerSettings settings(g_hz100);
    settings.SetWaitingPresents(waitingPresents);
    return settings;
  }

  //! A frame begun at startTicks with CPU work of 30,000 ticks, its present taken by the system or not. Returns its frame id.
  uint64_t Frame(PC::TimerWaitForPresentPacer& rPacer, const int64_t startTicks, const bool accepted = true)
  {
    const PC::FrameSchedule schedule = rPacer.BeginFrame(At(startTicks));
    const PC::PresentPlan present = rPacer.EndFrame(At(startTicks + 30'000));
    PC::PresentReport report;
    report.FrameId = present.FrameId;
    report.CallTime = At(startTicks + 30'000);
    report.ReturnTime = At(startTicks + 30'600);
    report.Accepted = accepted;
    rPacer.AddPresent(report);
    return schedule.FrameId;
  }

  PC::PresentWaitReport Wait(const uint64_t frameId, const int64_t beginTicks, const int64_t endTicks, const bool shown = true)
  {
    PC::PresentWaitReport report;
    report.FrameId = frameId;
    report.BeginTime = At(beginTicks);
    report.EndTime = At(endTicks);
    report.Shown = shown;
    return report;
  }

  static_assert(PC::TimerWaitForPresentPacer::Hold == PC::HoldTier::Timer);
  static_assert(PC::TimerWaitForPresentPacer::Queue == PC::QueueTier::WaitForPresent);
}

TEST(TimerWaitForPresentPacer, BeforeAFrameItAsksForTheLastPresentWhenNoneMayWait)
{
  PC::PacerSettings settings = Settings(1);
  settings.SetPresentWaitSwapIntervals(3);
  PC::TimerWaitForPresentPacer pacer(settings);

  // Nothing was presented yet
  EXPECT_FALSE(pacer.PlanFrame(At(Start)).WaitsForPresent());
  EXPECT_EQ(Frame(pacer, Start), 1u);
  // The present of frame 1, for at most the settings' three swap intervals, and after it the step the frame is due at
  PC::FrameStartPlan plan = pacer.PlanFrame(At(Start + 31'000));
  EXPECT_TRUE(plan.WaitsForPresent());
  EXPECT_EQ(plan.WaitForPresentFrameId, 1u);
  EXPECT_EQ(plan.WaitForPresentTimeout, FP::TimeDuration::FromTicks(3 * Period));
  EXPECT_EQ(plan.StartTime, At(Start + Period));
  // Planned again it is the same
  EXPECT_EQ(pacer.PlanFrame(At(Start + 32'000)).WaitForPresentFrameId, 1u);

  EXPECT_EQ(Frame(pacer, Start + Period), 2u);
  plan = pacer.PlanFrame(At(Start + Period + 31'000));
  EXPECT_EQ(plan.WaitForPresentFrameId, 2u);
}

TEST(TimerWaitForPresentPacer, WithOnePresentAllowedToWaitItAsksForThePresentBeforeTheLast)
{
  PC::TimerWaitForPresentPacer pacer(Settings(2));

  static_cast<void>(Frame(pacer, Start));
  // One present so far: the one before it does not exist
  EXPECT_FALSE(pacer.PlanFrame(At(Start + 31'000)).WaitsForPresent());
  static_cast<void>(Frame(pacer, Start + Period));
  EXPECT_EQ(pacer.PlanFrame(At(Start + Period + 31'000)).WaitForPresentFrameId, 1u);
  static_cast<void>(Frame(pacer, Start + (2 * Period)));
  EXPECT_EQ(pacer.PlanFrame(At(Start + (2 * Period) + 31'000)).WaitForPresentFrameId, 2u);
  // The longest the wait may take is the default: four of the frame's swap intervals
  EXPECT_EQ(pacer.PlanFrame(At(Start + (2 * Period) + 31'000)).WaitForPresentTimeout, FP::TimeDuration::FromTicks(4 * Period));
}

TEST(TimerWaitForPresentPacer, APresentTheSystemDidNotTakeIsNotWaitedForNorAnyBeforeIt)
{
  PC::TimerWaitForPresentPacer pacer(Settings(1));
  static_cast<void>(Frame(pacer, Start));
  static_cast<void>(Frame(pacer, Start + Period));
  ASSERT_EQ(pacer.PlanFrame(At(Start + Period + 31'000)).WaitForPresentFrameId, 2u);

  // Frame 3's present is refused (a swap chain out of date): nothing to wait for, not frame 3 and not frame 2
  static_cast<void>(Frame(pacer, Start + (2 * Period), false));
  EXPECT_FALSE(pacer.PlanFrame(At(Start + (2 * Period) + 31'000)).WaitsForPresent());
  // The next present that is taken is waited for again
  static_cast<void>(Frame(pacer, Start + (3 * Period)));
  EXPECT_EQ(pacer.PlanFrame(At(Start + (3 * Period) + 31'000)).WaitForPresentFrameId, 4u);

  // With one present allowed to wait, the one before the refused one is never asked for
  PC::TimerWaitForPresentPacer slack(Settings(2));
  static_cast<void>(Frame(slack, Start));
  static_cast<void>(Frame(slack, Start + Period, false));
  static_cast<void>(Frame(slack, Start + (2 * Period)));
  EXPECT_FALSE(slack.PlanFrame(At(Start + (2 * Period) + 31'000)).WaitsForPresent());
  static_cast<void>(Frame(slack, Start + (3 * Period)));
  EXPECT_EQ(slack.PlanFrame(At(Start + (3 * Period) + 31'000)).WaitForPresentFrameId, 3u);
}

TEST(TimerWaitForPresentPacer, AfterAResetNoPresentFromBeforeIsWaitedFor)
{
  PC::TimerWaitForPresentPacer pacer(Settings(1));
  static_cast<void>(Frame(pacer, Start));
  static_cast<void>(Frame(pacer, Start + Period));
  ASSERT_TRUE(pacer.PlanFrame(At(Start + Period + 31'000)).WaitsForPresent());

  pacer.Reset();
  const PC::FrameStartPlan plan = pacer.PlanFrame(At(Start + Period + 31'000));
  EXPECT_FALSE(plan.WaitsForPresent());
  EXPECT_FALSE(plan.WaitsForStartTime());
  // The frames after it are waited for as before
  EXPECT_EQ(Frame(pacer, Start + (2 * Period)), 3u);
  EXPECT_EQ(pacer.PlanFrame(At(Start + (2 * Period) + 31'000)).WaitForPresentFrameId, 3u);
}

TEST(TimerWaitForPresentPacer, AfterTheWaitTheFrameIsPlannedAgainAndThePresentIsNotAskedForTwice)
{
  PC::TimerWaitForPresentPacer pacer(Settings(1));
  static_cast<void>(Frame(pacer, Start));
  const PC::FrameStartPlan first = pacer.PlanFrame(At(Start + 31'000));
  ASSERT_EQ(first.WaitForPresentFrameId, 1u);
  ASSERT_EQ(first.StartTime, At(Start + Period));

  // The wait held the loop until 0.7 of a period after the step the frame was due at
  const int64_t end = Start + Period + 70'000;
  pacer.AddPresentWait(Wait(1, Start + 31'000, end));
  // Planned again: no present to wait for, and the time is the next step's, where the grid is now, not the one that has passed
  const PC::FrameStartPlan again = pacer.PlanFrame(At(end));
  EXPECT_FALSE(again.WaitsForPresent());
  EXPECT_TRUE(again.WaitsForStartTime());
  EXPECT_GT(again.StartTime, At(end));
  EXPECT_LT(again.StartTime, At(Start + (2 * Period) + 1));
  // The frame after it is waited for as usual
  static_cast<void>(Frame(pacer, again.StartTime.Ticks()));
  EXPECT_EQ(pacer.PlanFrame(At(again.StartTime.Ticks() + 31'000)).WaitForPresentFrameId, 2u);
}

TEST(TimerWaitForPresentPacer, AFramePresentedAgainOnANewSwapChainCanBeWaitedFor)
{
  PC::TimerWaitForPresentPacer pacer(Settings(1));
  static_cast<void>(Frame(pacer, Start));
  // Frame 2's present is not taken, the swap chain is made anew, and the same frame is ended and presented again
  ASSERT_EQ(Frame(pacer, Start + Period, false), 2u);
  EXPECT_FALSE(pacer.PlanFrame(At(Start + Period + 31'000)).WaitsForPresent());
  const PC::PresentPlan again = pacer.EndFrame(At(Start + Period + 50'000));
  EXPECT_EQ(again.FrameId, 2u);
  EXPECT_EQ(again.CpuBusy, FP::TimeSpan32(50'000));
  PC::PresentReport report;
  report.FrameId = 2;
  report.CallTime = At(Start + Period + 50'000);
  report.ReturnTime = At(Start + Period + 50'600);
  pacer.AddPresent(report);
  EXPECT_EQ(pacer.PlanFrame(At(Start + Period + 51'000)).WaitForPresentFrameId, 2u);
}

TEST(TimerWaitForPresentPacer, ForgettingThePresentsLeavesTheGridTheFrameWindowAndTheSwapIntervalAsTheyAre)
{
  PC::TimerWaitForPresentPacer pacer(Settings(1));
  for (int64_t frame = 0; frame < 10; ++frame)
  {
    static_cast<void>(Frame(pacer, Start + (frame * Period)));
  }
  const int64_t now = Start + (9 * Period) + 31'000;
  ASSERT_EQ(pacer.PlanFrame(At(now)).WaitForPresentFrameId, 10u);
  ASSERT_EQ(pacer.FrameWindow().Frames, 9u);

  // A swap chain made anew: nothing from before to wait for, and the frame is still due on the grid
  pacer.ForgetPresents();
  const PC::FrameStartPlan plan = pacer.PlanFrame(At(now));
  EXPECT_FALSE(plan.WaitsForPresent());
  EXPECT_EQ(plan.StartTime, At(Start + (10 * Period)));
  EXPECT_EQ(pacer.FrameWindow().Frames, 9u);
  // The first present on the new swap chain is waited for
  EXPECT_EQ(Frame(pacer, Start + (10 * Period)), 11u);
  EXPECT_EQ(pacer.PlanFrame(At(Start + (10 * Period) + 31'000)).WaitForPresentFrameId, 11u);
}

TEST(TimerWaitForPresentPacer, TheCpuBusyTimeCanBeAskedForWhileTheFrameIsOpen)
{
  PC::TimerWaitForPresentPacer pacer(Settings(1));
  EXPECT_EQ(pacer.CpuBusyAt(At(Start)), FP::TimeSpan32());
  static_cast<void>(pacer.BeginFrame(At(Start)));
  // Where a marker is drawn before the frame's work is done
  EXPECT_EQ(pacer.CpuBusyAt(At(Start + 12'000)), FP::TimeSpan32(12'000));
  EXPECT_EQ(pacer.CpuBusyAt(At(Start - 1)), FP::TimeSpan32());
  pacer.Reset();
  EXPECT_EQ(pacer.CpuBusyAt(At(Start + 12'000)), FP::TimeSpan32());
}

TEST(TimerWaitForPresentPacer, AWaitThatHeldTheLoopMovesTheGridAQuarterOfTheWayToItsEnd)
{
  PC::TimerWaitForPresentPacer pacer(Settings(1));
  static_cast<void>(Frame(pacer, Start));
  ASSERT_EQ(pacer.PlanFrame(At(Start + 31'000)).StartTime, At(Start + Period));

  // The wait for frame 1 held the loop from 0.31 of a period until 0.2 of a period after the step the next frame is due at: the
  // display took the frame then, so the step moves a quarter of that, 5,000 ticks, towards it
  pacer.AddPresentWait(Wait(1, Start + 31'000, Start + Period + 20'000));
  EXPECT_EQ(pacer.PlanFrame(At(Start + 31'000)).StartTime, At(Start + Period + 5'000));
  // An end before a step moves it back
  pacer.AddPresentWait(Wait(1, Start + 31'000, Start + Period + 5'000 - 40'000));
  EXPECT_EQ(pacer.PlanFrame(At(Start + 31'000)).StartTime, At(Start + Period - 5'000));

  // Waits that end at the same place bring the grid there
  for (int32_t repeat = 0; repeat < 40; ++repeat)
  {
    pacer.AddPresentWait(Wait(1, Start + 31'000, Start + Period + 30'000));
  }
  EXPECT_NEAR(static_cast<double>(pacer.PlanFrame(At(Start + 31'000)).StartTime.Ticks()), static_cast<double>(Start + Period + 30'000), 4.0);
}

TEST(TimerWaitForPresentPacer, AWaitThatReturnedAtOnceOrRanOutMovesNothing)
{
  PC::TimerWaitForPresentPacer pacer(Settings(1));
  // Before there is a grid a wait says nothing
  pacer.AddPresentWait(Wait(1, Start - 50'000, Start - 10'000));
  static_cast<void>(Frame(pacer, Start));
  const FP::TickCount64 due = pacer.PlanFrame(At(Start + 31'000)).StartTime;
  ASSERT_EQ(due, At(Start + Period));

  // The present was shown some time before the wait began: it returned at once (under an eighth of a period)
  pacer.AddPresentWait(Wait(1, Start + Period + 20'000, Start + Period + 20'500));
  pacer.AddPresentWait(Wait(1, Start + Period + 20'000, Start + Period + 32'499));
  EXPECT_EQ(pacer.PlanFrame(At(Start + 31'000)).StartTime, due);
  EXPECT_EQ(pacer.PresentWaitTimeouts(), 0u);

  // The wait ran out: counted, and the grid stays
  pacer.AddPresentWait(Wait(1, Start + 31'000, Start + 2'531'000, false));
  EXPECT_EQ(pacer.PlanFrame(At(Start + 31'000)).StartTime, due);
  EXPECT_EQ(pacer.PresentWaitTimeouts(), 1u);

  // A wait that ended longer after the last frame than the pacer measures across (a pause) says nothing either
  pacer.AddPresentWait(Wait(1, Start + 31'000, Start + (600 * Period) + 20'000));
  EXPECT_EQ(pacer.PlanFrame(At(Start + 31'000)).StartTime, due);
}

TEST(TimerWaitForPresentPacer, AWaitThatHeldTheLoopPastTheFramesStepShowsAsARefreshTheDisplayLost)
{
  PC::TimerWaitForPresentPacer pacer(Settings(1));
  static_cast<void>(Frame(pacer, Start));
  static_cast<void>(Frame(pacer, Start + Period));

  // The display took frame 2 a refresh late: the wait ends a period after the step frame 3 was due at
  const int64_t end = Start + (3 * Period) + 4'000;
  pacer.AddPresentWait(Wait(2, Start + Period + 31'000, end));
  const PC::FrameSchedule third = pacer.BeginFrame(At(end));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 1u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
  // The animation time is not moved over it
  EXPECT_EQ(third.AnimationStep, Span(Period));
  EXPECT_EQ(third.AnimationTime, Span(2 * Period));
}

TEST(TimerWaitForPresentPacer, TheRestIsTheLowestPairsPacer)
{
  PC::PacerSettings settings = Settings(2);
  settings.SetPreferredFrameRate(25);
  PC::TimerWaitForPresentPacer pacer(settings);

  // A frame of four refreshes, held by a wait before its present
  const PC::FrameSchedule first = pacer.BeginFrame(At(Start));
  EXPECT_EQ(first.FrameId, 1u);
  EXPECT_EQ(first.SwapInterval, 4u);
  EXPECT_EQ(first.AnimationTime, Span(0));
  EXPECT_EQ(first.NextFrameStartTime, At(Start + (4 * Period)));
  const PC::PresentPlan present = pacer.EndFrame(At(Start + 30'000));
  EXPECT_EQ(present.PresentTime, At(Start + (3 * Period) + 10'000));
  EXPECT_EQ(present.CpuBusy, FP::TimeSpan32(30'000));
  EXPECT_EQ(pacer.LastPresentBlocked(), FP::TimeDuration::Zero());
  EXPECT_EQ(pacer.SwapInterval(), 4u);
  EXPECT_EQ(pacer.Refresh(), g_hz100);
  EXPECT_EQ(pacer.Settings(), settings);

  // A frame that ran long costs whole steps, and the loop is back on the grid
  static_cast<void>(pacer.BeginFrame(At(Start + (4 * Period))));
  static_cast<void>(pacer.EndFrame(At(Start + (4 * Period) + 660'000)));
  EXPECT_EQ(pacer.PlanFrame(At(Start + (4 * Period) + 660'600)).StartTime, At(Start + (11 * Period)));
  const PC::FrameSchedule third = pacer.BeginFrame(At(Start + (11 * Period)));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 3u);
  EXPECT_EQ(third.AnimationStep, Span(4 * Period));
  // EndFrame without a frame is nothing
  pacer.Reset();
  EXPECT_EQ(pacer.EndFrame(At(Start + (11 * Period))).FrameId, 0u);

  // Another refresh period and other settings start the grid again; the same ones change nothing
  static_cast<void>(pacer.BeginFrame(At(Start + (12 * Period))));
  pacer.SetRefreshPeriod(g_hz100);
  pacer.SetSettings(settings);
  EXPECT_TRUE(pacer.PlanFrame(At(Start + (12 * Period) + 1'000)).WaitsForStartTime());
  pacer.SetRefreshPeriod(PC::RefreshPeriod::FromRate(50));
  EXPECT_FALSE(pacer.PlanFrame(At(Start + (12 * Period) + 1'000)).WaitsForStartTime());
  static_cast<void>(pacer.BeginFrame(At(Start + (12 * Period) + 1'000)));
  settings.SetWaitingPresents(1);
  pacer.SetSettings(settings);
  EXPECT_FALSE(pacer.PlanFrame(At(Start + (12 * Period) + 2'000)).WaitsForStartTime());
}

TEST(TimerWaitForPresentPacer, AFrameWithoutAnEndIsNotJudgedByItsWorkAndAStartThatIsLateKeepsItsStep)
{
  PC::TimerWaitForPresentPacer pacer(Settings(2));
  static_cast<void>(pacer.BeginFrame(At(Start)));
  static_cast<void>(pacer.BeginFrame(At(Start + 140'000)));
  EXPECT_EQ(pacer.FrameWindow().Frames, 1u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  EXPECT_EQ(pacer.FrameWindow().AverageWork, Span(140'000));
  // Work over the frame's time is late although the frame after it keeps its step
  static_cast<void>(pacer.EndFrame(At(Start + 140'000 + 105'000)));
  EXPECT_FALSE(pacer.PlanFrame(At(Start + 246'000)).WaitsForStartTime());
  static_cast<void>(pacer.BeginFrame(At(Start + 246'000)));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  // A clock that went back starts the grid again
  static_cast<void>(pacer.BeginFrame(At(Start)));
  EXPECT_EQ(pacer.FrameWindow().Frames, 0u);
}

TEST(PacerSettings, TheSettingsOfTheTierPacersKeepToTheirRange)
{
  PC::PacerSettings settings(g_hz100);
  EXPECT_EQ(settings.WaitingPresents(), 2u);
  EXPECT_EQ(settings.PresentWaitSwapIntervals(), 4u);
  EXPECT_EQ(settings.MaxFramesInFlight(), 1u);
  EXPECT_EQ(settings.StartupPauseRefreshes(), 4u);
  EXPECT_EQ(settings.StartupPauseDelay(), Span(5'000'000));
  settings.SetWaitingPresents(1);
  settings.SetPresentWaitSwapIntervals(1);
  settings.SetMaxFramesInFlight(2);
  settings.SetStartupPauseRefreshes(0);
  settings.SetStartupPauseDelay(Span(0));
  EXPECT_EQ(settings.WaitingPresents(), 1u);
  EXPECT_EQ(settings.PresentWaitSwapIntervals(), 1u);
  EXPECT_EQ(settings.MaxFramesInFlight(), 2u);
  EXPECT_EQ(settings.StartupPauseRefreshes(), 0u);
  EXPECT_EQ(settings.StartupPauseDelay(), Span(0));
  settings.SetWaitingPresents(PC::PacerSettings::MaxWaitingPresents);
  settings.SetPresentWaitSwapIntervals(PC::PacerSettings::MaxPresentWaitSwapIntervals);
  settings.SetMaxFramesInFlight(PC::PacerSettings::MaxMaxFramesInFlight);
  settings.SetStartupPauseRefreshes(PC::PacerSettings::MaxStartupPauseRefreshes);
  settings.SetStartupPauseDelay(PC::PacerSettings::MaxStartupPauseDelay);
  EXPECT_EQ(settings.WaitingPresents(), 8u);
  EXPECT_EQ(settings.PresentWaitSwapIntervals(), 64u);
  EXPECT_EQ(settings.MaxFramesInFlight(), 8u);
  EXPECT_EQ(settings.StartupPauseRefreshes(), 64u);
  EXPECT_EQ(settings.StartupPauseDelay(), Span(100'000'000));
  EXPECT_NE(settings, PC::PacerSettings(g_hz100));
#ifdef NDEBUG
  settings.SetWaitingPresents(0);
  EXPECT_EQ(settings.WaitingPresents(), 1u);
  settings.SetWaitingPresents(9);
  EXPECT_EQ(settings.WaitingPresents(), 8u);
  settings.SetPresentWaitSwapIntervals(0);
  EXPECT_EQ(settings.PresentWaitSwapIntervals(), 1u);
  settings.SetPresentWaitSwapIntervals(65);
  EXPECT_EQ(settings.PresentWaitSwapIntervals(), 64u);
  settings.SetMaxFramesInFlight(0);
  EXPECT_EQ(settings.MaxFramesInFlight(), 1u);
  settings.SetMaxFramesInFlight(9);
  EXPECT_EQ(settings.MaxFramesInFlight(), 8u);
  settings.SetStartupPauseRefreshes(65);
  EXPECT_EQ(settings.StartupPauseRefreshes(), 64u);
  settings.SetStartupPauseDelay(Span(-1));
  EXPECT_EQ(settings.StartupPauseDelay(), Span(0));
  settings.SetStartupPauseDelay(Span(200'000'000));
  EXPECT_EQ(settings.StartupPauseDelay(), PC::PacerSettings::MaxStartupPauseDelay);
#elif GTEST_HAS_DEATH_TEST
  EXPECT_DEATH(settings.SetWaitingPresents(0), "");
  EXPECT_DEATH(settings.SetWaitingPresents(9), "");
  EXPECT_DEATH(settings.SetPresentWaitSwapIntervals(0), "");
  EXPECT_DEATH(settings.SetPresentWaitSwapIntervals(65), "");
  EXPECT_DEATH(settings.SetMaxFramesInFlight(0), "");
  EXPECT_DEATH(settings.SetMaxFramesInFlight(9), "");
  EXPECT_DEATH(settings.SetStartupPauseRefreshes(65), "");
  EXPECT_DEATH(settings.SetStartupPauseDelay(Span(-1)), "");
  EXPECT_DEATH(settings.SetStartupPauseDelay(Span(200'000'000)), "");
#else
  GTEST_SKIP() << "asserts are on and death tests are not available";
#endif
}

TEST(TimerWaitForPresentPacer, TheLongestAWaitMayTakeIsCountedInTheFramesOwnSwapIntervals)
{
  // 25 frames a second at 100 Hz: four refreshes per frame, and four of those
  PC::PacerSettings settings = Settings(1);
  settings.SetPreferredFrameRate(25);
  PC::TimerWaitForPresentPacer pacer(settings);
  static_cast<void>(Frame(pacer, Start));
  const PC::FrameStartPlan plan = pacer.PlanFrame(At(Start + 31'000));
  EXPECT_EQ(plan.WaitForPresentFrameId, 1u);
  EXPECT_EQ(plan.WaitForPresentTimeout, FP::TimeDuration::FromTicks(16 * Period));
}

TEST(TimerWaitForPresentPacer, WithGpuWorkReportsAFramesWorkIsTheCpusAndTheGpus)
{
  PC::TimerWaitForPresentPacer pacer(Settings(2));
  EXPECT_EQ(pacer.GpuTime(), FP::TimeDuration::Zero());
  static_cast<void>(Frame(pacer, Start));
  static_cast<void>(Frame(pacer, Start + Period));
  // GPU work of 0.8 periods that ends within the margin of the next frame's start: one after the other, the two added
  pacer.AddGpuWork(PC::GpuWorkReport::Times(1, At(Start + 30'000), At(Start + Period + 10'000)));
  EXPECT_EQ(pacer.GpuTime(), FP::TimeDuration::FromTicks(80'000));
  static_cast<void>(Frame(pacer, Start + (2 * Period)));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
  EXPECT_EQ(pacer.FrameWindow().AverageWork, Span((30'000 + 110'000) / 2));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);

  // Beside the CPU's work on the frame after it: the longer of the two
  pacer.AddGpuWork(PC::GpuWorkReport::Times(2, At(Start + Period + 50'000), At(Start + (2 * Period) + 30'000)));
  static_cast<void>(Frame(pacer, Start + (3 * Period)));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
  EXPECT_EQ(pacer.FrameWindow().AverageWork, Span((30'000 + 110'000 + 80'000) / 3));

  pacer.Reset();
  EXPECT_EQ(pacer.GpuTime(), FP::TimeDuration::Zero());
}

TEST(TimerWaitForPresentPacer, ALossThatRepeatsIsInTheAnimationStepAndALossThatDoesNotIsNot)
{
  PC::PacerSettings settings = Settings(1);
  settings.SetAutoSwapInterval(false);
  PC::TimerWaitForPresentPacer pacer(settings);
  static_cast<void>(pacer.BeginFrame(At(Start)));

  // Every frame takes two steps of the grid at one refresh per frame: from the second loss in a row on it is in the step
  PC::FrameSchedule schedule = pacer.BeginFrame(At(Start + (2 * Period)));
  EXPECT_EQ(schedule.AnimationStep, Span(Period));
  schedule = pacer.BeginFrame(At(Start + (4 * Period)));
  EXPECT_EQ(schedule.AnimationStep, Span(2 * Period));
  schedule = pacer.BeginFrame(At(Start + (6 * Period)));
  EXPECT_EQ(schedule.AnimationStep, Span(2 * Period));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 1u);
  // A frame on time ends it, and one loss after it is one loss
  schedule = pacer.BeginFrame(At(Start + (7 * Period)));
  EXPECT_EQ(schedule.AnimationStep, Span(Period));
  schedule = pacer.BeginFrame(At(Start + (9 * Period)));
  EXPECT_EQ(schedule.AnimationStep, Span(Period));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 2u);
}

TEST(TimerWaitForPresentPacer, ASwapIntervalTheRuleChangesIsItsAnswerToTheLossesBeforeIt)
{
  PC::TimerWaitForPresentPacer pacer(Settings(1));
  PC::FrameSchedule schedule = pacer.BeginFrame(At(Start));
  int64_t start = Start;
  while (schedule.Change != PC::SwapIntervalChange::Slower && start < Start + (1'000 * Period))
  {
    static_cast<void>(pacer.EndFrame(At(start + 30'000)));
    start += 2 * Period;
    schedule = pacer.BeginFrame(At(start));
  }
  ASSERT_EQ(schedule.Change, PC::SwapIntervalChange::Slower);
  ASSERT_EQ(schedule.SwapInterval, 2u);
  EXPECT_EQ(schedule.AnimationStep, Span(2 * Period));
  static_cast<void>(pacer.EndFrame(At(start + 30'000)));
  schedule = pacer.BeginFrame(At(start + (2 * Period)));
  EXPECT_EQ(schedule.AnimationStep, Span(2 * Period));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
}
