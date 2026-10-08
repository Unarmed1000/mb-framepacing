// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The pacer of a timer with a wait for a present: before a frame it asks for a wait until the present a set number back was shown,
// it is told what became of the wait, and its grid on the clock follows the display through the ends of the waits that held the
// loop. The rest is TimerPeriodOnlyPacer's, which TimerPeriodOnlyPacerTests has; here is what differs, and that the rest still
// holds.
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
#include <mb/framepacing/pacer/frame/PresentWaitReport.hpp>
#include <mb/framepacing/pacer/tier/TimerWaitForPresentPacer.hpp>
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

  PC::PacerSettings Settings(const uint32_t waitingPresents)
  {
    PC::PacerSettings settings = LowLatencySettings();
    settings.SetWaitingPresents(waitingPresents);
    return settings;
  }

  //! A frame begun at startNanoseconds with CPU work of 3 ms, its present taken by the system or not. Returns its frame id.
  uint64_t Frame(PC::TimerWaitForPresentPacer& rPacer, const int64_t startNanoseconds, const bool accepted = true)
  {
    const PC::FrameSchedule schedule = rPacer.BeginFrame(At(startNanoseconds));
    const PC::PresentPlan present = rPacer.EndFrame(At(startNanoseconds + 3'000'000));
    PC::PresentReport report;
    report.FrameId = present.FrameId;
    report.CallTime = At(startNanoseconds + 3'000'000);
    report.ReturnTime = At(startNanoseconds + 3'060'000);
    report.Accepted = accepted;
    rPacer.AddPresent(report);
    return schedule.FrameId;
  }

  PC::PresentWaitReport Wait(const uint64_t frameId, const int64_t beginNanoseconds, const int64_t endNanoseconds, const bool shown = true)
  {
    PC::PresentWaitReport report;
    report.FrameId = frameId;
    report.BeginTime = At(beginNanoseconds);
    report.EndTime = At(endNanoseconds);
    report.Shown = shown;
    return report;
  }

  static_assert(PC::TimerWaitForPresentPacer::Tier == PC::PacerTier::TimerWaitForPresent);
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
  PC::FrameStartPlan plan = pacer.PlanFrame(At(Start + 3'100'000));
  EXPECT_TRUE(plan.WaitsForPresent());
  EXPECT_EQ(plan.WaitForPresentFrameId, 1u);
  EXPECT_EQ(plan.WaitForPresentTimeout, FP::NanosecondTimeDuration::FromNanoseconds(3 * Period));
  EXPECT_EQ(plan.StartTime, At(Start + Period));
  // Planned again it is the same
  EXPECT_EQ(pacer.PlanFrame(At(Start + 3'200'000)).WaitForPresentFrameId, 1u);

  EXPECT_EQ(Frame(pacer, Start + Period), 2u);
  plan = pacer.PlanFrame(At(Start + Period + 3'100'000));
  EXPECT_EQ(plan.WaitForPresentFrameId, 2u);
}

TEST(TimerWaitForPresentPacer, WithOnePresentAllowedToWaitItAsksForThePresentBeforeTheLast)
{
  PC::TimerWaitForPresentPacer pacer(Settings(2));

  static_cast<void>(Frame(pacer, Start));
  // One present so far: the one before it does not exist
  EXPECT_FALSE(pacer.PlanFrame(At(Start + 3'100'000)).WaitsForPresent());
  static_cast<void>(Frame(pacer, Start + Period));
  EXPECT_EQ(pacer.PlanFrame(At(Start + Period + 3'100'000)).WaitForPresentFrameId, 1u);
  static_cast<void>(Frame(pacer, Start + (2 * Period)));
  EXPECT_EQ(pacer.PlanFrame(At(Start + (2 * Period) + 3'100'000)).WaitForPresentFrameId, 2u);
  // The longest the wait may take is the default: four of the frame's swap intervals
  EXPECT_EQ(pacer.PlanFrame(At(Start + (2 * Period) + 3'100'000)).WaitForPresentTimeout, FP::NanosecondTimeDuration::FromNanoseconds(4 * Period));
}

TEST(TimerWaitForPresentPacer, APresentTheSystemDidNotTakeIsNotWaitedForNorAnyBeforeIt)
{
  PC::TimerWaitForPresentPacer pacer(Settings(1));
  static_cast<void>(Frame(pacer, Start));
  static_cast<void>(Frame(pacer, Start + Period));
  ASSERT_EQ(pacer.PlanFrame(At(Start + Period + 3'100'000)).WaitForPresentFrameId, 2u);

  // Frame 3's present is refused (a swap chain out of date): nothing to wait for, not frame 3 and not frame 2
  static_cast<void>(Frame(pacer, Start + (2 * Period), false));
  EXPECT_FALSE(pacer.PlanFrame(At(Start + (2 * Period) + 3'100'000)).WaitsForPresent());
  // The next present that is taken is waited for again
  static_cast<void>(Frame(pacer, Start + (3 * Period)));
  EXPECT_EQ(pacer.PlanFrame(At(Start + (3 * Period) + 3'100'000)).WaitForPresentFrameId, 4u);

  // With one present allowed to wait, the one before the refused one is never asked for
  PC::TimerWaitForPresentPacer slack(Settings(2));
  static_cast<void>(Frame(slack, Start));
  static_cast<void>(Frame(slack, Start + Period, false));
  static_cast<void>(Frame(slack, Start + (2 * Period)));
  EXPECT_FALSE(slack.PlanFrame(At(Start + (2 * Period) + 3'100'000)).WaitsForPresent());
  static_cast<void>(Frame(slack, Start + (3 * Period)));
  EXPECT_EQ(slack.PlanFrame(At(Start + (3 * Period) + 3'100'000)).WaitForPresentFrameId, 3u);
}

TEST(TimerWaitForPresentPacer, AfterAResetNoPresentFromBeforeIsWaitedFor)
{
  PC::TimerWaitForPresentPacer pacer(Settings(1));
  static_cast<void>(Frame(pacer, Start));
  static_cast<void>(Frame(pacer, Start + Period));
  ASSERT_TRUE(pacer.PlanFrame(At(Start + Period + 3'100'000)).WaitsForPresent());

  pacer.Reset();
  const PC::FrameStartPlan plan = pacer.PlanFrame(At(Start + Period + 3'100'000));
  EXPECT_FALSE(plan.WaitsForPresent());
  EXPECT_FALSE(plan.WaitsForStartTime());
  // The frames after it are waited for as before
  EXPECT_EQ(Frame(pacer, Start + (2 * Period)), 3u);
  EXPECT_EQ(pacer.PlanFrame(At(Start + (2 * Period) + 3'100'000)).WaitForPresentFrameId, 3u);
}

TEST(TimerWaitForPresentPacer, AfterTheWaitTheFrameIsPlannedAgainAndThePresentIsNotAskedForTwice)
{
  PC::TimerWaitForPresentPacer pacer(Settings(1));
  static_cast<void>(Frame(pacer, Start));
  const PC::FrameStartPlan first = pacer.PlanFrame(At(Start + 3'100'000));
  ASSERT_EQ(first.WaitForPresentFrameId, 1u);
  ASSERT_EQ(first.StartTime, At(Start + Period));

  // The wait held the loop until 0.7 of a period after the step the frame was due at
  const int64_t end = Start + Period + 7'000'000;
  pacer.AddPresentWait(Wait(1, Start + 3'100'000, end));
  // Planned again: no present to wait for, and the time is the next step's, where the grid is now, not the one that has passed
  const PC::FrameStartPlan again = pacer.PlanFrame(At(end));
  EXPECT_FALSE(again.WaitsForPresent());
  EXPECT_TRUE(again.WaitsForStartTime());
  EXPECT_GT(again.StartTime, At(end));
  EXPECT_LT(again.StartTime, At(Start + (2 * Period) + 1));
  // The frame after it is waited for as usual
  static_cast<void>(Frame(pacer, again.StartTime.Nanoseconds()));
  EXPECT_EQ(pacer.PlanFrame(At(again.StartTime.Nanoseconds() + 3'100'000)).WaitForPresentFrameId, 2u);
}

TEST(TimerWaitForPresentPacer, AFramePresentedAgainOnANewSwapChainCanBeWaitedFor)
{
  PC::TimerWaitForPresentPacer pacer(Settings(1));
  static_cast<void>(Frame(pacer, Start));
  // Frame 2's present is not taken, the swap chain is made anew, and the same frame is ended and presented again
  ASSERT_EQ(Frame(pacer, Start + Period, false), 2u);
  EXPECT_FALSE(pacer.PlanFrame(At(Start + Period + 3'100'000)).WaitsForPresent());
  const PC::PresentPlan again = pacer.EndFrame(At(Start + Period + 5'000'000));
  EXPECT_EQ(again.FrameId, 2u);
  EXPECT_EQ(again.CpuBusy, FP::NanosecondTimeDuration::FromNanoseconds(5'000'000));
  PC::PresentReport report;
  report.FrameId = 2;
  report.CallTime = At(Start + Period + 5'000'000);
  report.ReturnTime = At(Start + Period + 5'060'000);
  pacer.AddPresent(report);
  EXPECT_EQ(pacer.PlanFrame(At(Start + Period + 5'100'000)).WaitForPresentFrameId, 2u);
}

TEST(TimerWaitForPresentPacer, ForgettingThePresentsLeavesTheGridTheFrameWindowAndTheSwapIntervalAsTheyAre)
{
  PC::TimerWaitForPresentPacer pacer(Settings(1));
  for (int64_t frame = 0; frame < 10; ++frame)
  {
    static_cast<void>(Frame(pacer, Start + (frame * Period)));
  }
  const int64_t now = Start + (9 * Period) + 3'100'000;
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
  EXPECT_EQ(pacer.PlanFrame(At(Start + (10 * Period) + 3'100'000)).WaitForPresentFrameId, 11u);
}

TEST(TimerWaitForPresentPacer, TheCpuBusyTimeCanBeAskedForWhileTheFrameIsOpen)
{
  PC::TimerWaitForPresentPacer pacer(Settings(1));
  EXPECT_EQ(pacer.CpuBusyAt(At(Start)), FP::NanosecondTimeDuration());
  static_cast<void>(pacer.BeginFrame(At(Start)));
  // Where a marker is drawn before the frame's work is done
  EXPECT_EQ(pacer.CpuBusyAt(At(Start + 1'200'000)), FP::NanosecondTimeDuration::FromNanoseconds(1'200'000));
  EXPECT_EQ(pacer.CpuBusyAt(At(Start - 1)), FP::NanosecondTimeDuration());
  pacer.Reset();
  EXPECT_EQ(pacer.CpuBusyAt(At(Start + 1'200'000)), FP::NanosecondTimeDuration());
}

TEST(TimerWaitForPresentPacer, AWaitThatHeldTheLoopMovesTheGridAQuarterOfTheWayToItsEnd)
{
  PC::TimerWaitForPresentPacer pacer(Settings(1));
  static_cast<void>(Frame(pacer, Start));
  ASSERT_EQ(pacer.PlanFrame(At(Start + 3'100'000)).StartTime, At(Start + Period));

  // The wait for frame 1 held the loop from 0.31 of a period until 0.2 of a period after the step the next frame is due at: the
  // display took the frame then, so the step moves a quarter of that, 0.5 ms, towards it
  pacer.AddPresentWait(Wait(1, Start + 3'100'000, Start + Period + 2'000'000));
  EXPECT_EQ(pacer.PlanFrame(At(Start + 3'100'000)).StartTime, At(Start + Period + 500'000));
  // An end before a step moves it back
  pacer.AddPresentWait(Wait(1, Start + 3'100'000, Start + Period + 500'000 - 4'000'000));
  EXPECT_EQ(pacer.PlanFrame(At(Start + 3'100'000)).StartTime, At(Start + Period - 500'000));

  // Waits that end at the same place bring the grid there
  for (int32_t repeat = 0; repeat < 40; ++repeat)
  {
    pacer.AddPresentWait(Wait(1, Start + 3'100'000, Start + Period + 3'000'000));
  }
  EXPECT_NEAR(static_cast<double>(pacer.PlanFrame(At(Start + 3'100'000)).StartTime.Nanoseconds()), static_cast<double>(Start + Period + 3'000'000),
              400.0);
}

TEST(TimerWaitForPresentPacer, AWaitThatReturnedAtOnceOrRanOutMovesNothing)
{
  PC::TimerWaitForPresentPacer pacer(Settings(1));
  // Before there is a grid a wait says nothing
  pacer.AddPresentWait(Wait(1, Start - 5'000'000, Start - 1'000'000));
  static_cast<void>(Frame(pacer, Start));
  const FP::NanosecondTickCount due = pacer.PlanFrame(At(Start + 3'100'000)).StartTime;
  ASSERT_EQ(due, At(Start + Period));

  // The present was shown some time before the wait began: it returned at once (under an eighth of a period)
  pacer.AddPresentWait(Wait(1, Start + Period + 2'000'000, Start + Period + 2'050'000));
  pacer.AddPresentWait(Wait(1, Start + Period + 2'000'000, Start + Period + 3'249'900));
  EXPECT_EQ(pacer.PlanFrame(At(Start + 3'100'000)).StartTime, due);
  EXPECT_EQ(pacer.PresentWaitTimeouts(), 0u);

  // The wait ran out: counted, and the grid stays
  pacer.AddPresentWait(Wait(1, Start + 3'100'000, Start + 253'100'000, false));
  EXPECT_EQ(pacer.PlanFrame(At(Start + 3'100'000)).StartTime, due);
  EXPECT_EQ(pacer.PresentWaitTimeouts(), 1u);

  // A wait that ended longer after the last frame than the pacer measures across (a pause) says nothing either
  pacer.AddPresentWait(Wait(1, Start + 3'100'000, Start + (600 * Period) + 2'000'000));
  EXPECT_EQ(pacer.PlanFrame(At(Start + 3'100'000)).StartTime, due);
}

TEST(TimerWaitForPresentPacer, AWaitThatHeldTheLoopPastTheFramesStepShowsAsARefreshTheDisplayLost)
{
  PC::TimerWaitForPresentPacer pacer(Settings(1));
  static_cast<void>(Frame(pacer, Start));
  static_cast<void>(Frame(pacer, Start + Period));

  // The display took frame 2 a refresh late: the wait ends a period after the step frame 3 was due at
  const int64_t end = Start + (3 * Period) + 400'000;
  pacer.AddPresentWait(Wait(2, Start + Period + 3'100'000, end));
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
  const PC::PresentPlan present = pacer.EndFrame(At(Start + 3'000'000));
  EXPECT_EQ(present.PresentTime, At(Start + (3 * Period) + 1'000'000));
  EXPECT_EQ(present.CpuBusy, FP::NanosecondTimeDuration::FromNanoseconds(3'000'000));
  EXPECT_EQ(pacer.LastPresentBlocked(), FP::NanosecondTimeDuration::Zero());
  EXPECT_EQ(pacer.SwapInterval(), 4u);
  EXPECT_EQ(pacer.Refresh(), g_hz100);
  EXPECT_EQ(pacer.Settings(), settings);

  // A frame that ran long costs whole steps, and the loop is back on the grid
  static_cast<void>(pacer.BeginFrame(At(Start + (4 * Period))));
  static_cast<void>(pacer.EndFrame(At(Start + (4 * Period) + 66'000'000)));
  EXPECT_EQ(pacer.PlanFrame(At(Start + (4 * Period) + 66'060'000)).StartTime, At(Start + (11 * Period)));
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
  EXPECT_TRUE(pacer.PlanFrame(At(Start + (12 * Period) + 100'000)).WaitsForStartTime());
  pacer.SetRefreshPeriod(PC::RefreshPeriod::FromRate(50));
  EXPECT_FALSE(pacer.PlanFrame(At(Start + (12 * Period) + 100'000)).WaitsForStartTime());
  static_cast<void>(pacer.BeginFrame(At(Start + (12 * Period) + 100'000)));
  settings.SetWaitingPresents(1);
  pacer.SetSettings(settings);
  EXPECT_FALSE(pacer.PlanFrame(At(Start + (12 * Period) + 200'000)).WaitsForStartTime());
}

TEST(TimerWaitForPresentPacer, AFrameWithoutAnEndIsNotJudgedByItsWorkAndAStartThatIsLateKeepsItsStep)
{
  PC::TimerWaitForPresentPacer pacer(Settings(2));
  static_cast<void>(pacer.BeginFrame(At(Start)));
  static_cast<void>(pacer.BeginFrame(At(Start + 14'000'000)));
  EXPECT_EQ(pacer.FrameWindow().Frames, 1u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  EXPECT_EQ(pacer.FrameWindow().AverageWork, Span(14'000'000));
  // Work over the frame's time is late although the frame after it keeps its step
  static_cast<void>(pacer.EndFrame(At(Start + 14'000'000 + 10'500'000)));
  EXPECT_FALSE(pacer.PlanFrame(At(Start + 24'600'000)).WaitsForStartTime());
  static_cast<void>(pacer.BeginFrame(At(Start + 24'600'000)));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  // A clock that went back starts the grid again
  static_cast<void>(pacer.BeginFrame(At(Start)));
  EXPECT_EQ(pacer.FrameWindow().Frames, 0u);
}

TEST(PacerSettings, TheSettingsOfTheTierPacersKeepToTheirRange)
{
  PC::PacerSettings settings(g_hz100);
  EXPECT_EQ(settings.Aim(), PC::PacerAim::Smoothness);
  settings.SetAim(PC::PacerAim::LowLatency);
  EXPECT_EQ(settings.Aim(), PC::PacerAim::LowLatency);
  settings.SetAim(PC::PacerAim::Smoothness);
  EXPECT_EQ(settings.WaitingPresents(), 2u);
  EXPECT_EQ(settings.PresentWaitSwapIntervals(), 4u);
  EXPECT_EQ(settings.MaxFramesInFlight(), 1u);
  EXPECT_EQ(settings.StartupPauseRefreshes(), 4u);
  EXPECT_EQ(settings.StartupPauseDelay(), Span(500'000'000));
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
  EXPECT_EQ(settings.StartupPauseDelay(), Span(10'000'000'000));
  EXPECT_NE(settings, PC::PacerSettings(g_hz100));
#ifdef NDEBUG
  settings.SetAim(static_cast<PC::PacerAim>(7));
  EXPECT_EQ(settings.Aim(), PC::PacerAim::Smoothness);
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
  settings.SetStartupPauseDelay(Span(20'000'000'000));
  EXPECT_EQ(settings.StartupPauseDelay(), PC::PacerSettings::MaxStartupPauseDelay);
#elif GTEST_HAS_DEATH_TEST
  EXPECT_DEATH(settings.SetAim(static_cast<PC::PacerAim>(7)), "");
  EXPECT_DEATH(settings.SetWaitingPresents(9), "");
  EXPECT_DEATH(settings.SetPresentWaitSwapIntervals(0), "");
  EXPECT_DEATH(settings.SetPresentWaitSwapIntervals(65), "");
  EXPECT_DEATH(settings.SetMaxFramesInFlight(0), "");
  EXPECT_DEATH(settings.SetMaxFramesInFlight(9), "");
  EXPECT_DEATH(settings.SetStartupPauseRefreshes(65), "");
  EXPECT_DEATH(settings.SetStartupPauseDelay(Span(-1)), "");
  EXPECT_DEATH(settings.SetStartupPauseDelay(Span(20'000'000'000)), "");
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
  const PC::FrameStartPlan plan = pacer.PlanFrame(At(Start + 3'100'000));
  EXPECT_EQ(plan.WaitForPresentFrameId, 1u);
  EXPECT_EQ(plan.WaitForPresentTimeout, FP::NanosecondTimeDuration::FromNanoseconds(16 * Period));
}

TEST(TimerWaitForPresentPacer, WithGpuWorkReportsAFramesWorkIsTheCpusAndTheGpus)
{
  PC::TimerWaitForPresentPacer pacer(Settings(2));
  EXPECT_EQ(pacer.GpuTime(), FP::NanosecondTimeDuration::Zero());
  static_cast<void>(Frame(pacer, Start));
  static_cast<void>(Frame(pacer, Start + Period));
  // GPU work of 0.8 periods that ends within the margin of the next frame's start: one after the other, the two added
  pacer.AddGpuWork(PC::GpuWorkReport::Times(1, At(Start + 3'000'000), At(Start + Period + 1'000'000)));
  EXPECT_EQ(pacer.GpuTime(), FP::NanosecondTimeDuration::FromNanoseconds(8'000'000));
  static_cast<void>(Frame(pacer, Start + (2 * Period)));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
  EXPECT_EQ(pacer.FrameWindow().AverageWork, Span((3'000'000 + 11'000'000) / 2));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);

  // Beside the CPU's work on the frame after it: the longer of the two
  pacer.AddGpuWork(PC::GpuWorkReport::Times(2, At(Start + Period + 5'000'000), At(Start + (2 * Period) + 3'000'000)));
  static_cast<void>(Frame(pacer, Start + (3 * Period)));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
  EXPECT_EQ(pacer.FrameWindow().AverageWork, Span((3'000'000 + 11'000'000 + 8'000'000) / 3));

  pacer.Reset();
  EXPECT_EQ(pacer.GpuTime(), FP::NanosecondTimeDuration::Zero());
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
    static_cast<void>(pacer.EndFrame(At(start + 3'000'000)));
    start += 2 * Period;
    schedule = pacer.BeginFrame(At(start));
  }
  ASSERT_EQ(schedule.Change, PC::SwapIntervalChange::Slower);
  ASSERT_EQ(schedule.SwapInterval, 2u);
  EXPECT_EQ(schedule.AnimationStep, Span(2 * Period));
  static_cast<void>(pacer.EndFrame(At(start + 3'000'000)));
  schedule = pacer.BeginFrame(At(start + (2 * Period)));
  EXPECT_EQ(schedule.AnimationStep, Span(2 * Period));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
}

// The aim of smoothness, which is the default: frames are made ahead of the display, and the wait keeps them to the reserve.

TEST(TimerWaitForPresentPacer, WithTheAimOfSmoothnessAFrameIsMadeAheadAndTheWaitKeepsItToThat)
{
  PC::TimerWaitForPresentPacer pacer{PC::PacerSettings(g_hz100)};
  ASSERT_EQ(pacer.Settings().Aim(), PC::PacerAim::Smoothness);
  ASSERT_EQ(pacer.Settings().WaitingPresents(), 2u);

  // The second frame starts at once, with nothing to wait for: it is the one made ahead
  static_cast<void>(Frame(pacer, Start));
  PC::FrameStartPlan plan = pacer.PlanFrame(At(Start + 3'060'000));
  EXPECT_FALSE(plan.WaitsForPresent());
  EXPECT_FALSE(plan.WaitsForStartTime());
  const PC::FrameSchedule second = pacer.BeginFrame(At(Start + 3'060'000));
  EXPECT_EQ(second.NextFrameStartTime, At(Start + Period));
  EXPECT_EQ(second.IntendedDisplayTime, At(Start + (2 * Period)));
  const PC::PresentPlan present = pacer.EndFrame(At(Start + 6'060'000));
  PC::PresentReport report;
  report.FrameId = present.FrameId;
  report.CallTime = At(Start + 6'060'000);
  report.ReturnTime = At(Start + 6'120'000);
  pacer.AddPresent(report);

  // The third waits until the first was shown, and then for its time, a period before the step it is for
  plan = pacer.PlanFrame(At(Start + 6'120'000));
  EXPECT_EQ(plan.WaitForPresentFrameId, 1u);
  EXPECT_EQ(plan.StartTime, At(Start + Period));
}

TEST(TimerWaitForPresentPacer, WithTheAimOfSmoothnessALongFrameIsMadeUpForWithinTheReserveAndGivenUpBeyondIt)
{
  PC::TimerWaitForPresentPacer pacer{PC::PacerSettings(g_hz100)};
  static_cast<void>(Frame(pacer, Start));
  static_cast<void>(Frame(pacer, Start + 3'060'000));
  static_cast<void>(Frame(pacer, Start + Period));

  // A frame of 1.6 periods, begun a period before its step: within the frame made ahead
  static_cast<void>(pacer.BeginFrame(At(Start + (2 * Period))));
  PC::PresentPlan present = pacer.EndFrame(At(Start + (2 * Period) + 16'000'000));
  PC::PresentReport report;
  report.FrameId = present.FrameId;
  report.CallTime = At(Start + (2 * Period) + 16'000'000);
  report.ReturnTime = report.CallTime;
  pacer.AddPresent(report);
  int64_t now = Start + (2 * Period) + 16'060'000;
  EXPECT_FALSE(pacer.PlanFrame(At(now)).WaitsForStartTime());
  static_cast<void>(Frame(pacer, now));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 0u);
  EXPECT_EQ(pacer.PlanFrame(At(now + 3'060'000)).StartTime, At(Start + (4 * Period)));

  // A frame of 2.4 periods: a step beyond it, which is given up
  static_cast<void>(pacer.BeginFrame(At(Start + (4 * Period))));
  present = pacer.EndFrame(At(Start + (4 * Period) + 24'000'000));
  // A report of another frame says nothing of this one's present
  report.FrameId = present.FrameId - 1u;
  report.CallTime = At(Start + (20 * Period));
  pacer.AddPresent(report);
  now = Start + (4 * Period) + 24'060'000;
  static_cast<void>(Frame(pacer, now));
  EXPECT_EQ(pacer.RefreshesBehindClock(), 1u);
  EXPECT_EQ(pacer.PlanFrame(At(now + 3'060'000)).StartTime, At(Start + (7 * Period)));
}

// A window that is not shown (another one covers it, or it is minimised): the system takes its presents and shows none, so every
// wait for one runs out. Seen on the first integration: the pacer counted the frames its own wait had held as late and slowed
// down, to a swap interval of 13 in 15 s, and stayed slow for 2 s after the window was back.

namespace
{
  //! What a frame of a loop did: where it started, its frame id, and what its plan asked for
  struct LoopStep
  {
    int64_t StartNanoseconds{0};
    uint64_t FrameId{0};
    uint64_t AskedForId{0};
    int64_t TimeoutNanoseconds{0};
  };

  //! A frame as an application makes it at now: it waits for the present the plan asks for (shown at once, or never, so the wait
  //! takes all the time it was given and askCost more: an answer is not free everywhere), plans again, waits for the start time
  //! and makes the frame.
  LoopStep LoopFrame(PC::TimerWaitForPresentPacer& rPacer, const int64_t nowNanoseconds, const bool presentsAreShown,
                     const int64_t askCostNanoseconds = 0)
  {
    LoopStep step;
    int64_t now = nowNanoseconds;
    PC::FrameStartPlan plan = rPacer.PlanFrame(At(now));
    if (plan.WaitsForPresent())
    {
      step.AskedForId = plan.WaitForPresentFrameId;
      step.TimeoutNanoseconds = plan.WaitForPresentTimeout.Nanoseconds();
      const int64_t end = presentsAreShown ? now + 50'000 : now + step.TimeoutNanoseconds + askCostNanoseconds;
      rPacer.AddPresentWait(Wait(plan.WaitForPresentFrameId, now, end, presentsAreShown));
      now = end;
      plan = rPacer.PlanFrame(At(now));
      EXPECT_FALSE(plan.WaitsForPresent());
    }
    step.StartNanoseconds = plan.WaitsForStartTime() ? plan.StartTime.Nanoseconds() : now;
    step.FrameId = Frame(rPacer, step.StartNanoseconds);
    return step;
  }
}

TEST(TimerWaitForPresentPacer, AWindowThatIsNotShownDoesNotSlowThePacerDown)
{
  // An answer to "was it shown" that is free, and one that takes a period: on the first integration's system it took 10 ms
  // while the window was covered
  for (const int64_t askCost : {int64_t{0}, Period})
  {
    for (const uint32_t waitingPresents : {1u, 2u})
    {
      PC::TimerWaitForPresentPacer pacer(Settings(waitingPresents));
      ASSERT_TRUE(pacer.Settings().AutoSwapInterval());
      const int64_t timeout = int64_t{pacer.Settings().PresentWaitSwapIntervals()} * Period;

      // In view: a frame every period, from the first one's start
      LoopStep step{Start - 3'100'000, 0, 0, 0};
      for (int32_t frame = 0; frame < 100; ++frame)
      {
        step = LoopFrame(pacer, step.StartNanoseconds + 3'100'000, true);
      }
      const int64_t lastInView = step.StartNanoseconds;
      const uint32_t framesInView = pacer.FrameWindow().Frames;
      EXPECT_EQ(lastInView, Start + (99 * Period));
      EXPECT_FALSE(pacer.PresentWaitsStopped());

      // Covered for 15 s. The first two waits run out, each after the time it was given: those two frames start that much
      // late, and neither is judged
      step = LoopFrame(pacer, step.StartNanoseconds + 3'100'000, false, askCost);
      EXPECT_EQ(step.TimeoutNanoseconds, timeout);
      EXPECT_EQ(step.StartNanoseconds, lastInView + 3'100'000 + timeout + askCost);
      EXPECT_FALSE(pacer.PresentWaitsStopped());
      step = LoopFrame(pacer, step.StartNanoseconds + 3'100'000, false, askCost);
      EXPECT_EQ(step.TimeoutNanoseconds, timeout);
      EXPECT_TRUE(pacer.PresentWaitsStopped());
      EXPECT_EQ(pacer.PresentWaitTimeouts(), 2u);
      EXPECT_EQ(pacer.FrameWindow().Frames, framesInView);

      // From then on nothing is waited for. Once in sixteen frames the plan asks, with no time to wait, after a present that has
      // had the time a wait would have given it; the other frames go on a period apart, and no frame is late
      int32_t asks = 0;
      for (int32_t frame = 0; frame < 1'500; ++frame)
      {
        const int64_t before = step.StartNanoseconds;
        step = LoopFrame(pacer, step.StartNanoseconds + 3'100'000, false, askCost);
        ASSERT_EQ(step.TimeoutNanoseconds, 0) << frame;
        ASSERT_EQ(step.AskedForId != 0u, (frame % 16) == 15) << frame;
        if (step.AskedForId != 0u)
        {
          ++asks;
          // The present asked after is the settings' swap intervals of a wait older than the one a wait would be for
          ASSERT_EQ(step.AskedForId, step.FrameId - uint64_t{waitingPresents} - pacer.Settings().PresentWaitSwapIntervals()) << frame;
        }
        // A frame an ask held starts when the answer is there, and the grid goes on from it
        const bool held = step.AskedForId != 0u && askCost > 0;
        ASSERT_EQ(step.StartNanoseconds, held ? before + 3'100'000 + askCost : before + Period) << frame;
        ASSERT_EQ(pacer.SwapInterval(), 1u) << frame;
        ASSERT_EQ(pacer.FrameWindow().LateFrames, 0u) << frame;
        ASSERT_TRUE(pacer.PresentWaitsStopped()) << frame;
      }
      EXPECT_EQ(asks, 93);
      EXPECT_EQ(pacer.PresentWaitTimeouts(), 95u);

      // A frame of the covered window that is shown in passing: one answer says shown, the next does not, and nothing changes
      bool shown = true;
      for (int32_t frame = 0; frame < 40; ++frame)
      {
        step = LoopFrame(pacer, step.StartNanoseconds + 3'100'000, shown, askCost);
        shown = shown && step.AskedForId == 0u;
        ASSERT_TRUE(pacer.PresentWaitsStopped()) << frame;
      }

      // In view again: two answers in a row say shown, and the frame after waits as before, at the swap interval it had
      int32_t frames = 0;
      while (pacer.PresentWaitsStopped() && frames < 100)
      {
        step = LoopFrame(pacer, step.StartNanoseconds + 3'100'000, true);
        ++frames;
      }
      EXPECT_LE(frames, 32);
      EXPECT_GE(frames, 17);
      const int64_t before = step.StartNanoseconds;
      step = LoopFrame(pacer, step.StartNanoseconds + 3'100'000, true);
      EXPECT_EQ(step.TimeoutNanoseconds, timeout);
      EXPECT_EQ(step.AskedForId, step.FrameId - uint64_t{waitingPresents});
      EXPECT_EQ(step.StartNanoseconds, before + Period);
      EXPECT_EQ(pacer.SwapInterval(), 1u);
      EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
    }
  }
}

TEST(TimerWaitForPresentPacer, OneWaitThatRunsOutIsNotALateFrameAndDoesNotStopTheWaits)
{
  PC::TimerWaitForPresentPacer pacer(Settings(1));
  LoopStep step{Start, 0, 0, 0};
  for (int32_t frame = 0; frame < 10; ++frame)
  {
    step = LoopFrame(pacer, step.StartNanoseconds + 3'100'000, true);
  }
  const uint32_t frames = pacer.FrameWindow().Frames;
  const uint64_t behind = pacer.RefreshesBehindClock();

  // A present that is never shown, as at the start of a window: the wait for it runs out after four periods
  step = LoopFrame(pacer, step.StartNanoseconds + 3'100'000, false);
  EXPECT_EQ(pacer.PresentWaitTimeouts(), 1u);
  EXPECT_FALSE(pacer.PresentWaitsStopped());
  // The frame it held is not judged: not late, not in the frame window, and no refresh is counted as lost
  EXPECT_EQ(pacer.FrameWindow().Frames, frames);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), behind);

  // The grid goes on from that frame, and the next wait is a wait again
  const int64_t held = step.StartNanoseconds;
  step = LoopFrame(pacer, step.StartNanoseconds + 3'100'000, true);
  EXPECT_EQ(step.TimeoutNanoseconds, 4 * Period);
  EXPECT_EQ(step.StartNanoseconds, held + Period);
  EXPECT_EQ(pacer.FrameWindow().Frames, frames + 1u);
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 0u);

  // A wait that ran out without holding the loop (it was only asked) is no reason not to judge the frame: one that starts late
  // after it is late
  pacer.AddPresentWait(Wait(11, step.StartNanoseconds + 3'100'000, step.StartNanoseconds + 3'100'000, false));
  static_cast<void>(Frame(pacer, step.StartNanoseconds + (3 * Period)));
  EXPECT_EQ(pacer.FrameWindow().LateFrames, 1u);
}

TEST(TimerWaitForPresentPacer, ANewSwapChainsPresentsAreWaitedForAgain)
{
  PC::TimerWaitForPresentPacer pacer(Settings(1));
  LoopStep step{Start, 0, 0, 0};
  for (int32_t frame = 0; frame < 10; ++frame)
  {
    step = LoopFrame(pacer, step.StartNanoseconds + 3'100'000, frame < 5);
  }
  ASSERT_TRUE(pacer.PresentWaitsStopped());

  // The presents so far are gone with their swap chain, and the waits that ran out with them
  pacer.ForgetPresents();
  EXPECT_FALSE(pacer.PresentWaitsStopped());
  step = LoopFrame(pacer, step.StartNanoseconds + 3'100'000, true);
  EXPECT_EQ(step.AskedForId, 0u);
  step = LoopFrame(pacer, step.StartNanoseconds + 3'100'000, true);
  EXPECT_EQ(step.AskedForId, 11u);
  EXPECT_EQ(step.TimeoutNanoseconds, 4 * Period);
}

TEST(PacerSettings, ThePacerPicksThePresentsThatMayWaitUnlessTheyAreSetForMeasuring)
{
  PC::PacerSettings settings(g_hz100);
  // An application says its aim; how many presents may wait is the pacer's: two, so one may wait, with either aim
  EXPECT_TRUE(settings.PicksWaitingPresents());
  EXPECT_EQ(settings.WaitingPresents(), PC::PacerSettings::PickedWaitingPresents);
  EXPECT_EQ(settings.WaitingPresents(), 2u);
  settings.SetAim(PC::PacerAim::LowLatency);
  EXPECT_EQ(settings.WaitingPresents(), 2u);
  EXPECT_EQ(settings.ReserveFrames(), 1u);

  // Set for a measurement it is what was set, and it is not the same settings as the pacer's pick of that number
  settings.SetWaitingPresents(1);
  EXPECT_FALSE(settings.PicksWaitingPresents());
  EXPECT_EQ(settings.WaitingPresents(), 1u);
  EXPECT_EQ(settings.ReserveFrames(), 0u);
  PC::PacerSettings picked(g_hz100);
  picked.SetAim(PC::PacerAim::LowLatency);
  settings.SetWaitingPresents(2);
  EXPECT_NE(settings, picked);
  // And zero gives it back to the pacer
  settings.SetWaitingPresents(0);
  EXPECT_TRUE(settings.PicksWaitingPresents());
  EXPECT_EQ(settings, picked);
}
