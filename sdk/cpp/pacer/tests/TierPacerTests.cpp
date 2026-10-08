// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. The one pacer an application talks to (sdk/doc/pacer-design.md, "How a pacer is put together"): the capability
// sets pick the tier, and a change of the active set is a handover in which the frames, the animation time and the rule go on.
// What each tier does with a fixed set is tested in the tier's own file, through the tier's class, which is this pacer.

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/capability/PacerCapabilities.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <mb/framepacing/pacer/capability/PacerTier.hpp>
#include <mb/framepacing/pacer/display/DisplayErrorState.hpp>
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
#include <mb/framepacing/pacer/frame/VBlankReading.hpp>
#include <mb/framepacing/pacer/rule/FrameWindowState.hpp>
#include <mb/framepacing/pacer/tier/TierPacer.hpp>
#include <gtest/gtest.h>
#include <algorithm>
#include <cstdint>

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;
using PC::PacerCapabilities;
using PC::PacerCapability;
using PC::PacerTier;

namespace
{
  // 100 Hz: a refresh period of exactly 10,000,000 ns. The display's vertical blanks are at Start + n periods
  constexpr int64_t Period = 10'000'000;
  constexpr int64_t Start = 1'000'000'000;
  constexpr int64_t Work = 3'000'000;

  const PC::RefreshPeriod g_hz100 = PC::RefreshPeriod::FromRate(100);

  constexpr FP::NanosecondTickCount At(const int64_t nanoseconds) noexcept
  {
    return FP::NanosecondTickCount(nanoseconds);
  }

  PC::PacerSettings Settings(const PC::PacerAim aim = PC::PacerAim::LowLatency)
  {
    PC::PacerSettings settings(g_hz100);
    settings.SetAim(aim);
    // No pause after start-up: these tests are of the handover
    settings.SetStartupPauseRefreshes(0);
    return settings;
  }

  //! An application's frame loop on the pacer: it carries out what it is given, and the display shows every present it waits
  //! for at once
  struct Loop
  {
    PC::TierPacer Pacer;
    int64_t Now{Start};
    //! What the last frame's calls gave
    PC::FrameStartPlan Plan;
    PC::PresentPlan Present;
    int64_t StartNanoseconds{0};
    int64_t PresentNanoseconds{0};
    uint32_t WaitsAskedFor{0};
    uint32_t GpuWaitsAskedFor{0};
    uint64_t GpuWaitFrameId{0};

    Loop(const PC::PacerSettings& settings, const PacerCapabilities& capabilities)
      : Pacer(settings, capabilities)
    {
    }

    PC::FrameSchedule Frame()
    {
      if (Pacer.ActiveCapabilities().Has(PacerCapability::VBlankTimes))
      {
        // The last vertical blank at or before now
        PC::VBlankReading reading;
        reading.VBlankTime = At(Start + (((Now - Start) / Period) * Period));
        reading.ReadTime = At(Now);
        Pacer.AddVBlank(reading);
      }
      Plan = Pacer.PlanFrame(At(Now));
      if (Plan.WaitsForPresent())
      {
        ++WaitsAskedFor;
        PC::PresentWaitReport wait;
        wait.FrameId = Plan.WaitForPresentFrameId;
        wait.BeginTime = At(Now);
        Now += 20'000;
        wait.EndTime = At(Now);
        Pacer.AddPresentWait(wait);
        const PC::FrameStartPlan again = Pacer.PlanFrame(At(Now));
        EXPECT_FALSE(again.WaitsForPresent());
        Plan.StartTime = again.StartTime;
      }
      GpuWaitFrameId = Plan.WaitForGpuWorkFrameId;
      if (Plan.WaitsForGpuWork())
      {
        // The GPU is done with every frame it is asked about
        ++GpuWaitsAskedFor;
        PC::GpuWaitReport wait;
        wait.FrameId = Plan.WaitForGpuWorkFrameId;
        wait.BeginTime = At(Now);
        Now += 20'000;
        wait.EndTime = At(Now);
        Pacer.AddGpuWait(wait);
        const PC::FrameStartPlan again = Pacer.PlanFrame(At(Now));
        EXPECT_FALSE(again.WaitsForGpuWork());
        Plan.StartTime = again.StartTime;
      }
      StartNanoseconds = Plan.WaitsForStartTime() ? Plan.StartTime.Nanoseconds() : Now;
      const PC::FrameSchedule schedule = Pacer.BeginFrame(At(StartNanoseconds));
      Present = Pacer.EndFrame(At(StartNanoseconds + Work));
      PresentNanoseconds = Present.WaitsForPresentTime() ? Present.PresentTime.Nanoseconds() : StartNanoseconds + Work;
      PC::PresentReport report;
      report.FrameId = Present.FrameId;
      report.CallTime = At(PresentNanoseconds);
      report.ReturnTime = At(PresentNanoseconds + 60'000);
      Pacer.AddPresent(report);
      Now = PresentNanoseconds + 100'000;
      return schedule;
    }
  };

  constexpr PacerCapability Wait = PacerCapability::WaitForPresent;
  constexpr PacerCapability VBlank = PacerCapability::VBlankTimes;
  constexpr PacerCapability AtTime = PacerCapability::PresentAtTime;
  constexpr PacerCapability AfterDuration = PacerCapability::PresentAfterDuration;
}

TEST(TierPacer, TheCapabilitySetsGiveTheRatingsAndTheTierThatPaces)
{
  // The baseline
  const PC::TierPacer baseline(Settings(), PacerCapabilities());
  EXPECT_EQ(baseline.Rating().Tier, PacerTier::TimerPeriodOnly);
  EXPECT_EQ(baseline.ActiveRating().Tier, PacerTier::TimerPeriodOnly);
  EXPECT_EQ(baseline.WorkingTier(), PacerTier::TimerPeriodOnly);

  // A wait for a present
  const PC::TierPacer waits(Settings(), PacerCapabilities(Wait));
  EXPECT_EQ(waits.Rating().Tier, PacerTier::TimerWaitForPresent);
  EXPECT_EQ(waits.WorkingTier(), PacerTier::TimerWaitForPresent);

  // Vertical blank times: until one is read the pacer is on a timer, and says so
  Loop loop(Settings(), PacerCapabilities(VBlank | Wait));
  EXPECT_EQ(loop.Pacer.Rating().Tier, PacerTier::VBlankWaitForPresent);
  EXPECT_EQ(loop.Pacer.ActiveRating().Tier, PacerTier::VBlankWaitForPresent);
  EXPECT_EQ(loop.Pacer.WorkingTier(), PacerTier::TimerWaitForPresent);
  static_cast<void>(loop.Frame());
  EXPECT_TRUE(loop.Pacer.HasVBlankReading());
  EXPECT_EQ(loop.Pacer.WorkingTier(), PacerTier::VBlankWaitForPresent);

  // A timed present: the tier above the same set without one
  PC::TierPacer timed(Settings(), PacerCapabilities(AfterDuration | Wait | PacerCapability::DisplayTimes));
  EXPECT_EQ(timed.Rating().Tier, PacerTier::TimedTimerWaitForPresent);
  EXPECT_TRUE(timed.Rating().ReportsDisplayTimes);
  EXPECT_EQ(timed.WorkingTier(), PacerTier::TimedTimerWaitForPresent);

  // What is active is a part of what the application has: a capability it does not have is left out
  timed.SetActiveCapabilities(PacerCapabilities(VBlank | Wait));
  EXPECT_EQ(timed.ActiveCapabilities(), PacerCapabilities(Wait));
  timed.SetActiveCapabilities(PacerCapabilities());
  EXPECT_EQ(timed.ActiveRating().Tier, PacerTier::TimerPeriodOnly);
  EXPECT_EQ(timed.WorkingTier(), PacerTier::TimerPeriodOnly);
  EXPECT_EQ(timed.Rating().Tier, PacerTier::TimedTimerWaitForPresent);
  // And what the application has can get smaller: what is active and gone goes out of the active set
  timed.SetActiveCapabilities(PacerCapabilities(Wait | PacerCapability::DisplayTimes));
  timed.SetCapabilities(PacerCapabilities(PacerCapability::DisplayTimes));
  EXPECT_EQ(timed.Capabilities(), PacerCapabilities(PacerCapability::DisplayTimes));
  EXPECT_EQ(timed.ActiveCapabilities(), PacerCapabilities(PacerCapability::DisplayTimes));
  EXPECT_EQ(timed.WorkingTier(), PacerTier::TimerPeriodOnly);
}

TEST(TierPacer, AChangeOfTheActiveSetTakesEffectWhenTheOpenFrameHasEnded)
{
  PC::TierPacer pacer(Settings(), PacerCapabilities(Wait));
  // No frame is open: at once
  pacer.SetActiveCapabilities(PacerCapabilities());
  EXPECT_EQ(pacer.WorkingTier(), PacerTier::TimerPeriodOnly);
  pacer.SetActiveCapabilities(PacerCapabilities(Wait));
  EXPECT_EQ(pacer.WorkingTier(), PacerTier::TimerWaitForPresent);

  // A frame is open: the set is the new one, and the frame is paced to its end as it began
  static_cast<void>(pacer.BeginFrame(At(Start)));
  pacer.SetActiveCapabilities(PacerCapabilities());
  EXPECT_EQ(pacer.ActiveCapabilities(), PacerCapabilities());
  EXPECT_EQ(pacer.ActiveRating().Tier, PacerTier::TimerPeriodOnly);
  EXPECT_EQ(pacer.WorkingTier(), PacerTier::TimerWaitForPresent);
  static_cast<void>(pacer.EndFrame(At(Start + Work)));
  EXPECT_EQ(pacer.WorkingTier(), PacerTier::TimerPeriodOnly);

  // A frame that is never ended: the change is made when the next one starts
  static_cast<void>(pacer.BeginFrame(At(Start + Period)));
  pacer.SetActiveCapabilities(PacerCapabilities(Wait));
  EXPECT_EQ(pacer.WorkingTier(), PacerTier::TimerPeriodOnly);
  static_cast<void>(pacer.BeginFrame(At(Start + (2 * Period))));
  EXPECT_EQ(pacer.WorkingTier(), PacerTier::TimerWaitForPresent);
}

TEST(TierPacer, WhenAnotherPartPlacesTheFramesTheFramesTheAnimationTimeAndTheRuleGoOn)
{
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    PC::PacerSettings settings = Settings(aim);
    settings.SetPreferredSwapInterval(2);
    // The application has vertical blank times, and begins without them
    Loop loop(settings, PacerCapabilities(VBlank));
    loop.Pacer.SetActiveCapabilities(PacerCapabilities());
    PC::FrameSchedule schedule;
    for (int32_t frame = 0; frame < 40; ++frame)
    {
      schedule = loop.Frame();
    }
    ASSERT_EQ(loop.Pacer.WorkingTier(), PacerTier::TimerPeriodOnly);
    ASSERT_EQ(schedule.FrameId, 40u);
    ASSERT_EQ(schedule.SwapInterval, 2u);
    ASSERT_GT(loop.Pacer.FrameWindow().Frames, 10u);

    // The vertical blank times are made active: the frame after it is the first that is placed on a vertical blank
    for (int32_t change = 0; change < 4; ++change)
    {
      const bool toVBlanks = (change % 2) == 0;
      const PC::FrameSchedule before = schedule;
      const PC::FrameWindowState windowBefore = loop.Pacer.FrameWindow();
      loop.Pacer.SetActiveCapabilities(toVBlanks ? PacerCapabilities(VBlank) : PacerCapabilities());
      const int64_t now = loop.Now;
      schedule = loop.Frame();
      // It starts when the frame before it said the next one would, or at once when that has passed. From vertical blanks
      // with the aim of smoothness, where the presents were held, to the grid: a refresh before the next present would
      // have been made and the frame margin, as the grid holds a present of two refreshes for one and the margin
      const bool presentsWereHeld = !toVBlanks && aim == PC::PacerAim::Smoothness;
      const int64_t held = presentsWereHeld ? Period - settings.FrameMargin().Nanoseconds() : 0;
      EXPECT_EQ(loop.StartNanoseconds, std::max(now, before.NextFrameStartTime.Nanoseconds() + held)) << change;
      // Its id is the next one, its swap interval is the same, its animation time is a step of two refreshes on
      EXPECT_EQ(schedule.FrameId, before.FrameId + 1u) << change;
      EXPECT_EQ(schedule.SwapInterval, 2u) << change;
      EXPECT_EQ(schedule.AnimationStep.Nanoseconds(), 2 * Period) << change;
      EXPECT_EQ(schedule.AnimationTime.Nanoseconds(), before.AnimationTime.Nanoseconds() + (2 * Period)) << change;
      // The frame before it was placed by the other part and is not judged: the frame window is what it was
      EXPECT_EQ(loop.Pacer.FrameWindow().Frames, windowBefore.Frames) << change;
      EXPECT_EQ(loop.Pacer.FrameWindow().Span, windowBefore.Span) << change;
      EXPECT_EQ(loop.Pacer.FrameWindow().LateFrames, 0u) << change;
      EXPECT_EQ(loop.Pacer.RefreshesBehindClock(), 0u) << change;

      // And the frames go on from there, each two refreshes after the one before it
      for (int32_t frame = 0; frame < 19; ++frame)
      {
        const PC::FrameSchedule previous = schedule;
        schedule = loop.Frame();
        ASSERT_EQ(schedule.FrameId, previous.FrameId + 1u) << change;
        ASSERT_EQ(schedule.AnimationStep.Nanoseconds(), 2 * Period) << change << ' ' << frame;
        ASSERT_EQ(schedule.SwapInterval, 2u) << change;
        // The frame window spans its length and no more, whichever part gave the frames their times
        ASSERT_LE(loop.Pacer.FrameWindow().Span.Nanoseconds(), (settings.FrameWindowLength().Nanoseconds() + (4 * Period))) << change << ' ' << frame;
        ASSERT_GE(loop.Pacer.FrameWindow().Frames, windowBefore.Frames) << change << ' ' << frame;
      }
      EXPECT_EQ(loop.Pacer.WorkingTier(), toVBlanks ? PacerTier::VBlankPeriodOnly : PacerTier::TimerPeriodOnly) << change;
      EXPECT_EQ(loop.Pacer.HasVBlankReading(), toVBlanks) << change;
      EXPECT_EQ(loop.Pacer.FrameWindow().LateFrames, 0u) << change;
    }
    EXPECT_EQ(schedule.FrameId, 40u + (4u * 20u));
    EXPECT_EQ(loop.Pacer.VBlankJumps(), 0u);
  }
}

TEST(TierPacer, WithFramesMadeAheadAChangeOfWhatPlacesTheFramesMakesNoneAgain)
{
  // The aim of smoothness at one refresh per frame: a frame is made ahead of the display. A part that starts by itself makes
  // it with two frames back to back. One that takes over does not: the frames made ahead are on their way already, and one
  // more would wait behind them for good
  const PC::PacerSettings settings = Settings(PC::PacerAim::Smoothness);
  Loop fresh(settings, PacerCapabilities());
  static_cast<void>(fresh.Frame());
  const int64_t firstStart = fresh.StartNanoseconds;
  static_cast<void>(fresh.Frame());
  EXPECT_LT(fresh.StartNanoseconds - firstStart, Period / 2);

  Loop loop(settings, PacerCapabilities(VBlank));
  loop.Pacer.SetActiveCapabilities(PacerCapabilities());
  PC::FrameSchedule schedule;
  for (int32_t frame = 0; frame < 40; ++frame)
  {
    schedule = loop.Frame();
  }
  for (int32_t change = 0; change < 6; ++change)
  {
    const bool toVBlanks = (change % 2) == 0;
    loop.Pacer.SetActiveCapabilities(toVBlanks ? PacerCapabilities(VBlank) : PacerCapabilities());
    int64_t previousStart = loop.StartNanoseconds;
    int64_t previousPresent = loop.PresentNanoseconds;
    for (int32_t frame = 0; frame < 30; ++frame)
    {
      const PC::FrameSchedule before = schedule;
      schedule = loop.Frame();
      ASSERT_EQ(schedule.AnimationStep.Nanoseconds(), Period) << change << ' ' << frame;
      // No frame is made right after the one before it. On a grid a frame is presented when it is done, and it is the
      // starts that are apart: at least half a period. On vertical blanks a frame starts when the one before it was
      // presented and its own present is held, and it is the presents that are apart. From the third frame on the
      // presents are a period apart
      if (toVBlanks)
      {
        ASSERT_GE(loop.PresentNanoseconds - previousPresent, Period / 2) << change << ' ' << frame;
      }
      else
      {
        ASSERT_GE(loop.StartNanoseconds - previousStart, Period / 2) << change << ' ' << frame;
      }
      ASSERT_LE(loop.PresentNanoseconds - previousPresent, 2 * Period) << change << ' ' << frame;
      if (frame >= 2)
      {
        ASSERT_EQ(loop.PresentNanoseconds - previousPresent, Period) << change << ' ' << frame;
        ASSERT_EQ(schedule.IntendedDisplayTime.Nanoseconds(), before.IntendedDisplayTime.Nanoseconds() + Period) << change << ' ' << frame;
      }
      // Made ahead as before the change: for a refresh well over a period after its start, where a frame that is not
      // made ahead is for the next one
      ASSERT_GE(schedule.IntendedDisplayTime.Nanoseconds() - loop.StartNanoseconds, Period + (Period / 4)) << change << ' ' << frame;
      previousStart = loop.StartNanoseconds;
      previousPresent = loop.PresentNanoseconds;
    }
    EXPECT_EQ(loop.Pacer.FrameWindow().LateFrames, 0u) << change;
    EXPECT_EQ(loop.Pacer.RefreshesBehindClock(), 0u) << change;
  }
}

TEST(TierPacer, TheWaitForAPresentGoesOnAcrossAChangeOfWhatPlacesTheFrames)
{
  Loop loop(Settings(), PacerCapabilities(VBlank | Wait));
  loop.Pacer.SetActiveCapabilities(PacerCapabilities(Wait));
  PC::FrameSchedule schedule;
  for (int32_t frame = 0; frame < 10; ++frame)
  {
    schedule = loop.Frame();
  }
  // Two presents may wait: the plan asks for the present two frames back, and goes on doing so with the vertical blanks
  ASSERT_EQ(loop.Plan.WaitForPresentFrameId, schedule.FrameId - 2u);
  loop.Pacer.SetActiveCapabilities(PacerCapabilities(VBlank | Wait));
  for (int32_t frame = 0; frame < 10; ++frame)
  {
    schedule = loop.Frame();
    ASSERT_EQ(loop.Plan.WaitForPresentFrameId, schedule.FrameId - 2u) << frame;
  }
  EXPECT_EQ(loop.Pacer.WorkingTier(), PacerTier::VBlankWaitForPresent);
  EXPECT_EQ(loop.Pacer.PresentWaitTimeouts(), 0u);

  // The wait is given up: no plan asks for one, and giving it up starts no pause after start-up
  PC::PacerSettings pausing = Settings();
  pausing.SetStartupPauseRefreshes(4);
  Loop held(pausing, PacerCapabilities(Wait));
  for (int32_t frame = 0; frame < 10; ++frame)
  {
    static_cast<void>(held.Frame());
  }
  const uint32_t asked = held.WaitsAskedFor;
  ASSERT_GT(asked, 5u);
  held.Pacer.SetActiveCapabilities(PacerCapabilities());
  for (int32_t frame = 0; frame < 200; ++frame)
  {
    static_cast<void>(held.Frame());
  }
  EXPECT_EQ(held.WaitsAskedFor, asked);
  EXPECT_EQ(held.Pacer.StartupPauses(), 0u);
  EXPECT_EQ(held.Pacer.WorkingTier(), PacerTier::TimerPeriodOnly);
  // A pacer that never had the wait makes its pause
  Loop unheld(pausing, PacerCapabilities());
  for (int32_t frame = 0; frame < 200; ++frame)
  {
    static_cast<void>(unheld.Frame());
  }
  EXPECT_EQ(unheld.Pacer.StartupPauses(), 1u);
}

TEST(TierPacer, APauseAcrossAChangeStartsTheFramesAgain)
{
  Loop loop(Settings(), PacerCapabilities(VBlank));
  loop.Pacer.SetActiveCapabilities(PacerCapabilities());
  PC::FrameSchedule schedule;
  for (int32_t frame = 0; frame < 40; ++frame)
  {
    schedule = loop.Frame();
  }
  ASSERT_GT(loop.Pacer.FrameWindow().Frames, 10u);
  loop.Pacer.SetActiveCapabilities(PacerCapabilities(VBlank));
  // The application stood for a minute: nothing is measured across it, and the frame window starts empty
  loop.Now += int64_t{60} * 1'000'000'000;
  const PC::FrameSchedule after = loop.Frame();
  EXPECT_EQ(after.FrameId, schedule.FrameId + 1u);
  EXPECT_EQ(loop.Pacer.FrameWindow().Frames, 0u);
  EXPECT_EQ(after.AnimationTime.Nanoseconds(), schedule.AnimationTime.Nanoseconds() + Period);
}

TEST(TierPacer, SettingsAndAResetReachBothParts)
{
  Loop loop(Settings(), PacerCapabilities(VBlank));
  static_cast<void>(loop.Frame());
  PC::PacerSettings settings = Settings();
  settings.SetPreferredSwapInterval(3);
  loop.Pacer.SetSettings(settings);
  EXPECT_EQ(loop.Pacer.SwapInterval(), 3u);
  loop.Pacer.SetActiveCapabilities(PacerCapabilities());
  EXPECT_EQ(loop.Pacer.SwapInterval(), 3u);
  EXPECT_EQ(loop.Pacer.Settings(), settings);
  loop.Pacer.SetRefreshPeriod(PC::RefreshPeriod::FromRate(50));
  EXPECT_EQ(loop.Pacer.Refresh(), PC::RefreshPeriod::FromRate(50));
  loop.Pacer.SetActiveCapabilities(PacerCapabilities(VBlank));
  EXPECT_EQ(loop.Pacer.Refresh(), PC::RefreshPeriod::FromRate(50));
  loop.Pacer.Reset();
  loop.Pacer.ForgetPresents();
  EXPECT_EQ(loop.Pacer.FrameWindow().Frames, 0u);
  EXPECT_EQ(loop.Pacer.CpuBusyAt(At(loop.Now)).Nanoseconds(), 0);
  EXPECT_EQ(loop.Pacer.GpuTime().Nanoseconds(), 0);
  EXPECT_EQ(loop.Pacer.LastPresentBlocked().Nanoseconds(), 60'000);
  EXPECT_EQ(loop.Pacer.ShownLaterByWaits(), 0u);
  EXPECT_EQ(loop.Pacer.SystemHeldFrames(), 0u);
  EXPECT_EQ(loop.Pacer.FrameSlotHeldFrames(), 0u);
  EXPECT_FALSE(loop.Pacer.PresentWaitsStopped());
  EXPECT_GT(loop.Pacer.ReadyPlaceNow().Nanoseconds(), 0);
}

// The timed present (tiers 1 to 4): the present is given a time. With a time before which the frame is not shown the display's
// side puts the frame on its refresh and the loop holds no present; a time the frame before it stays is given next to what the
// loop does without one.

TEST(TierPacer, WithATimedPresentTheTierThatPacesIsOneOfTheFourWithOne)
{
  for (const PacerCapability timed : {AtTime, AfterDuration, AtTime | AfterDuration})
  {
    const PC::TierPacer timer(Settings(), PacerCapabilities(timed));
    EXPECT_EQ(timer.WorkingTier(), PacerTier::TimedTimerPeriodOnly);
    const PC::TierPacer waits(Settings(), PacerCapabilities(timed | Wait));
    EXPECT_EQ(waits.WorkingTier(), PacerTier::TimedTimerWaitForPresent);

    // Until a vertical blank is read the pacer is on a timer, and says so
    Loop vblank(Settings(), PacerCapabilities(timed | VBlank));
    EXPECT_EQ(vblank.Pacer.ActiveRating().Tier, PacerTier::TimedVBlankPeriodOnly);
    EXPECT_EQ(vblank.Pacer.WorkingTier(), PacerTier::TimedTimerPeriodOnly);
    static_cast<void>(vblank.Frame());
    EXPECT_EQ(vblank.Pacer.WorkingTier(), PacerTier::TimedVBlankPeriodOnly);

    Loop all(Settings(), PacerCapabilities(timed | VBlank | Wait));
    static_cast<void>(all.Frame());
    EXPECT_EQ(all.Pacer.WorkingTier(), PacerTier::TimedVBlankWaitForPresent);
    // Left out of the active set, the tier is the one without it
    all.Pacer.SetActiveCapabilities(PacerCapabilities(VBlank | Wait));
    EXPECT_EQ(all.Pacer.WorkingTier(), PacerTier::VBlankWaitForPresent);
  }
}

TEST(TierPacer, ATimeOnThePresentIsHalfAPeriodBeforeTheFramesRefreshAndTheLoopHoldsNoPresent)
{
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    for (const PacerCapability named : {PacerCapability::NoCapabilities, Wait, VBlank, VBlank | Wait})
    {
      for (const uint32_t swapInterval : {1u, 2u})
      {
        PC::PacerSettings settings = Settings(aim);
        settings.SetPreferredSwapInterval(swapInterval);
        // The same loop without a timed present holds presents: on vertical blanks every one with the aim of smoothness (with
        // low latency it is the start that is held), and on a grid those of two refreshes
        Loop untimed(settings, PacerCapabilities(named));
        Loop timed(settings, PacerCapabilities(named | AtTime));
        uint32_t held = 0;
        for (int32_t frame = 0; frame < 60; ++frame)
        {
          static_cast<void>(untimed.Frame());
          held += untimed.Present.WaitsForPresentTime() ? 1u : 0u;
          ASSERT_EQ(untimed.Present.NotBeforeTime, FP::NanosecondTickCount());
          ASSERT_EQ(untimed.Present.MinimumDuration, FP::NanosecondTimeDuration());

          const PC::FrameSchedule schedule = timed.Frame();
          ASSERT_FALSE(timed.Present.WaitsForPresentTime()) << frame;
          ASSERT_EQ(timed.Present.NotBeforeTime.Nanoseconds(), schedule.IntendedDisplayTime.Nanoseconds() - (Period / 2)) << frame;
          ASSERT_EQ(timed.Present.MinimumDuration, FP::NanosecondTimeDuration()) << frame;
          ASSERT_EQ(timed.Present.FrameId, schedule.FrameId);
          ASSERT_EQ(schedule.SwapInterval, swapInterval);
          if (frame > 10)
          {
            // One frame per swap interval, each for the refresh that is its swap interval after the one before it
            ASSERT_EQ(schedule.AnimationStep.Nanoseconds(), int64_t{swapInterval} * Period) << frame;
          }
        }
        const bool holds = PacerCapabilities(named).Has(VBlank) ? aim == PC::PacerAim::Smoothness : swapInterval > 1;
        EXPECT_EQ(held > 30u, holds) << static_cast<uint32_t>(named) << ' ' << swapInterval;
        EXPECT_EQ(timed.Pacer.FrameWindow().LateFrames, 0u);
        EXPECT_EQ(timed.Pacer.RefreshesBehindClock(), untimed.Pacer.RefreshesBehindClock());
      }
    }
  }
}

TEST(TierPacer, WithATimeOnThePresentSmoothnessHoldsTheNextFramesStartWhereItHeldThePresent)
{
  // Vertical blank times and no wait for a present: without a timed present every present waits for its time, and the next
  // frame starts right after it. With one the frame is presented when it is done, and the next frame's start waits for that time
  const PC::PacerSettings settings = Settings(PC::PacerAim::Smoothness);
  Loop untimed(settings, PacerCapabilities(VBlank));
  Loop timed(settings, PacerCapabilities(VBlank | AtTime));
  PC::FrameSchedule before;
  for (int32_t frame = 0; frame < 80; ++frame)
  {
    const PC::FrameSchedule plain = untimed.Frame();
    const PC::FrameSchedule schedule = timed.Frame();
    if (frame > 10)
    {
      ASSERT_TRUE(untimed.Present.WaitsForPresentTime()) << frame;
      ASSERT_FALSE(untimed.Plan.WaitsForStartTime()) << frame;
      // The time the untimed loop presents at is the time the frame after it is said to start at
      ASSERT_EQ(untimed.Present.PresentTime, plain.NextFrameStartTime) << frame;

      ASSERT_TRUE(timed.Plan.WaitsForStartTime()) << frame;
      ASSERT_EQ(timed.Plan.StartTime, before.NextFrameStartTime) << frame;
      ASSERT_EQ(timed.PresentNanoseconds, timed.StartNanoseconds + Work) << frame;
      // A frame a refresh, for the same refresh as without the time, and as far ahead of it
      ASSERT_EQ(schedule.IntendedDisplayTime.Nanoseconds(), before.IntendedDisplayTime.Nanoseconds() + Period) << frame;
      ASSERT_EQ(schedule.NextFrameStartTime.Nanoseconds(), before.NextFrameStartTime.Nanoseconds() + Period) << frame;
      ASSERT_EQ(schedule.IntendedDisplayTime.Nanoseconds() - schedule.NextFrameStartTime.Nanoseconds(),
                plain.IntendedDisplayTime.Nanoseconds() - plain.NextFrameStartTime.Nanoseconds())
        << frame;
    }
    before = schedule;
  }
  EXPECT_EQ(timed.Pacer.FrameWindow().LateFrames, 0u);

  // With the aim of low latency the start is held with a timed present as without one
  Loop lowLatency(Settings(), PacerCapabilities(VBlank));
  Loop lowLatencyTimed(Settings(), PacerCapabilities(VBlank | AtTime));
  for (int32_t frame = 0; frame < 40; ++frame)
  {
    const PC::FrameSchedule plain = lowLatency.Frame();
    const PC::FrameSchedule schedule = lowLatencyTimed.Frame();
    ASSERT_EQ(lowLatencyTimed.StartNanoseconds, lowLatency.StartNanoseconds) << frame;
    ASSERT_EQ(schedule.IntendedDisplayTime, plain.IntendedDisplayTime) << frame;
  }
}

TEST(TierPacer, ATimeTheFrameBeforeStaysIsGivenNextToWhatTheLoopDoesWithoutOne)
{
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    for (const PacerCapability named : {PacerCapability::NoCapabilities, Wait, VBlank, VBlank | Wait})
    {
      for (const uint32_t swapInterval : {1u, 2u, 3u})
      {
        PC::PacerSettings settings = Settings(aim);
        settings.SetPreferredSwapInterval(swapInterval);
        Loop untimed(settings, PacerCapabilities(named));
        Loop timed(settings, PacerCapabilities(named | AfterDuration));
        for (int32_t frame = 0; frame < 60; ++frame)
        {
          const PC::FrameSchedule plain = untimed.Frame();
          const PC::FrameSchedule schedule = timed.Frame();
          // The frame is the one the loop makes without the time, started and presented when that one is
          ASSERT_EQ(timed.StartNanoseconds, untimed.StartNanoseconds) << frame;
          ASSERT_EQ(timed.Present.PresentTime, untimed.Present.PresentTime) << frame;
          ASSERT_EQ(schedule.IntendedDisplayTime, plain.IntendedDisplayTime) << frame;
          ASSERT_EQ(schedule.AnimationTime, plain.AnimationTime) << frame;
          ASSERT_EQ(schedule.NextFrameStartTime, plain.NextFrameStartTime) << frame;
          // And its present says how long the frame before it stays: its refreshes, less half of one
          ASSERT_EQ(timed.Present.MinimumDuration.Nanoseconds(), (int64_t{swapInterval} * Period) - (Period / 2)) << frame;
          ASSERT_EQ(timed.Present.NotBeforeTime, FP::NanosecondTickCount()) << frame;
        }
      }
    }
  }
}

TEST(TierPacer, WithBothTimedPresentsTheTimeBeforeWhichAFrameIsNotShownIsGiven)
{
  Loop loop(Settings(), PacerCapabilities(AtTime | AfterDuration));
  const PC::FrameSchedule schedule = loop.Frame();
  EXPECT_EQ(loop.Present.NotBeforeTime.Nanoseconds(), schedule.IntendedDisplayTime.Nanoseconds() - (Period / 2));
  EXPECT_EQ(loop.Present.MinimumDuration, FP::NanosecondTimeDuration());
  // The application leaves it out of the active set to have the other
  loop.Pacer.SetActiveCapabilities(PacerCapabilities(AfterDuration));
  static_cast<void>(loop.Frame());
  EXPECT_EQ(loop.Present.NotBeforeTime, FP::NanosecondTickCount());
  EXPECT_EQ(loop.Present.MinimumDuration.Nanoseconds(), Period / 2);
}

TEST(TierPacer, ATimedPresentIsSwitchedOnAndOffWhileTheFramesGoOn)
{
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    for (const PacerCapability named : {PacerCapability::NoCapabilities, VBlank, VBlank | Wait})
    {
      PC::PacerSettings settings = Settings(aim);
      settings.SetPreferredSwapInterval(2);
      Loop loop(settings, PacerCapabilities(named | AtTime));
      loop.Pacer.SetActiveCapabilities(PacerCapabilities(named));
      PC::FrameSchedule schedule;
      for (int32_t frame = 0; frame < 30; ++frame)
      {
        schedule = loop.Frame();
      }
      for (int32_t change = 0; change < 6; ++change)
      {
        const bool on = (change % 2) == 0;
        // Made while a frame is open, it is the next frame's present that gets the time, or does not any more
        static_cast<void>(loop.Pacer.PlanFrame(At(loop.Now)));
        loop.Pacer.SetActiveCapabilities(on ? PacerCapabilities(named | AtTime) : PacerCapabilities(named));
        for (int32_t frame = 0; frame < 20; ++frame)
        {
          const PC::FrameSchedule before = schedule;
          schedule = loop.Frame();
          ASSERT_EQ(schedule.FrameId, before.FrameId + 1u) << change;
          ASSERT_EQ(schedule.SwapInterval, 2u) << change;
          ASSERT_EQ(schedule.AnimationStep.Nanoseconds(), 2 * Period) << change << ' ' << frame;
          ASSERT_EQ(schedule.IntendedDisplayTime.Nanoseconds(), before.IntendedDisplayTime.Nanoseconds() + (2 * Period)) << change << ' ' << frame;
          ASSERT_EQ(loop.Present.NotBeforeTime != FP::NanosecondTickCount(), on) << change << ' ' << frame;
        }
        EXPECT_EQ(loop.Pacer.FrameWindow().LateFrames, 0u) << change;
        EXPECT_EQ(loop.Pacer.RefreshesBehindClock(), 0u) << change;
      }
    }
  }
}

// The pause after start-up (low latency, no wait for a present) is the swap chain's: made once, whatever the active set was
// when the pacer was made and whichever part places the frames when it is due.

TEST(TierPacer, AWaitThatIsLeftOutBeforeItHeldAFrameLeavesThePauseAfterStartUpToBeMade)
{
  PC::PacerSettings pausing = Settings();
  pausing.SetStartupPauseRefreshes(4);

  // Made with what the application has, and the wait taken out of the active set before the first frame
  Loop loop(pausing, PacerCapabilities(Wait | VBlank));
  loop.Pacer.SetActiveCapabilities(PacerCapabilities());
  for (int32_t frame = 0; frame < 200; ++frame)
  {
    static_cast<void>(loop.Frame());
  }
  EXPECT_EQ(loop.WaitsAskedFor, 0u);
  EXPECT_EQ(loop.Pacer.StartupPauses(), 1u);
  EXPECT_EQ(loop.Pacer.RefreshesBehindClock(), 4u);

  // A new swap chain's presents pile up as a new window's do: the wait given up before it held one of its frames
  Loop again(pausing, PacerCapabilities(Wait));
  for (int32_t frame = 0; frame < 20; ++frame)
  {
    static_cast<void>(again.Frame());
  }
  again.Pacer.ForgetPresents();
  again.Pacer.SetActiveCapabilities(PacerCapabilities());
  for (int32_t frame = 0; frame < 200; ++frame)
  {
    static_cast<void>(again.Frame());
  }
  EXPECT_EQ(again.Pacer.StartupPauses(), 1u);
}

TEST(TierPacer, ThePauseAfterStartUpIsMadeOnceWhicheverPartPlacesTheFrames)
{
  PC::PacerSettings pausing = Settings();
  pausing.SetStartupPauseRefreshes(4);

  // Made on the grid on the clock, and the vertical blanks taken up long after: no second pause
  Loop late(pausing, PacerCapabilities(VBlank));
  late.Pacer.SetActiveCapabilities(PacerCapabilities());
  for (int32_t frame = 0; frame < 200; ++frame)
  {
    static_cast<void>(late.Frame());
  }
  ASSERT_EQ(late.Pacer.StartupPauses(), 1u);
  late.Pacer.SetActiveCapabilities(PacerCapabilities(VBlank));
  for (int32_t frame = 0; frame < 200; ++frame)
  {
    static_cast<void>(late.Frame());
  }
  late.Pacer.SetActiveCapabilities(PacerCapabilities());
  for (int32_t frame = 0; frame < 200; ++frame)
  {
    static_cast<void>(late.Frame());
  }
  EXPECT_EQ(late.Pacer.StartupPauses(), 1u);
  EXPECT_EQ(late.Pacer.RefreshesBehindClock(), 4u);

  // The vertical blanks taken up before the pause was due: it is made on them, once, and counted from the first frame
  Loop early(pausing, PacerCapabilities(VBlank));
  early.Pacer.SetActiveCapabilities(PacerCapabilities());
  for (int32_t frame = 0; frame < 10; ++frame)
  {
    static_cast<void>(early.Frame());
  }
  ASSERT_EQ(early.Pacer.StartupPauses(), 0u);
  early.Pacer.SetActiveCapabilities(PacerCapabilities(VBlank));
  const int64_t first = Start;
  int64_t pausedAt = 0;
  for (int32_t frame = 0; frame < 200; ++frame)
  {
    static_cast<void>(early.Frame());
    pausedAt = (pausedAt == 0 && early.Pacer.StartupPauses() == 1u) ? early.StartNanoseconds : pausedAt;
  }
  EXPECT_EQ(early.Pacer.StartupPauses(), 1u);
  // Half a second after the first frame, not after the first on the vertical blanks
  EXPECT_GE(pausedAt, first + pausing.StartupPauseDelay().Nanoseconds());
  EXPECT_LT(pausedAt, first + pausing.StartupPauseDelay().Nanoseconds() + (3 * Period));
}

// Display reports (the "+" beside a tier): the animation error where the application runs, counted from the display times it
// reports. Statistics only.

TEST(TierPacer, DisplayReportsAreCountedAndPaceNothing)
{
  for (const PacerCapability named : {PacerCapability::NoCapabilities, Wait, VBlank, VBlank | Wait, VBlank | Wait | AtTime})
  {
    PC::PacerSettings settings = Settings();
    settings.SetPreferredSwapInterval(2);
    Loop plain(settings, PacerCapabilities(named));
    Loop loop(settings, PacerCapabilities(named | PacerCapability::DisplayTimes));
    EXPECT_TRUE(loop.Pacer.Rating().ReportsDisplayTimes);
    EXPECT_EQ(loop.Pacer.Rating().Tier, plain.Pacer.Rating().Tier);
    PC::FrameSchedule before;
    for (int32_t frame = 0; frame < 100; ++frame)
    {
      const PC::FrameSchedule expected = plain.Frame();
      const PC::FrameSchedule schedule = loop.Frame();
      // The frame is the one the pacer makes without the reports
      ASSERT_EQ(loop.StartNanoseconds, plain.StartNanoseconds) << frame;
      ASSERT_EQ(schedule.IntendedDisplayTime, expected.IntendedDisplayTime) << frame;
      ASSERT_EQ(schedule.AnimationTime, expected.AnimationTime) << frame;
      if (frame > 0)
      {
        // The platform reports the frame before this one: shown when it was meant to be, but for every tenth, which was
        // shown a refresh late
        PC::DisplayReport report;
        report.FrameId = before.FrameId;
        report.DisplayTime = At(before.IntendedDisplayTime.Nanoseconds() + ((before.FrameId % 10u) == 0 ? Period : 0));
        loop.Pacer.AddDisplayReport(report);
        // A pacer whose application has no display times takes none
        plain.Pacer.AddDisplayReport(report);
      }
      before = schedule;
    }
    EXPECT_EQ(plain.Pacer.DisplayErrors(), PC::DisplayErrorState());
    const PC::DisplayErrorState state = loop.Pacer.DisplayErrors();
    EXPECT_EQ(state.Reports, 99u);
    EXPECT_EQ(state.Refused, 0u);
    // Every frame but the first: one of them late and the one after it a refresh sooner after it, nine times
    EXPECT_EQ(state.JudgedFrames, 98u);
    EXPECT_EQ(state.LateFrames, 9u);
    EXPECT_EQ(state.OffTargetFrames, 18u);
    EXPECT_EQ(state.ErrorFrames, 18u);
    EXPECT_EQ(loop.Pacer.FrameWindow().LateFrames, plain.Pacer.FrameWindow().LateFrames);

    // Left out of the active set, reports are not taken; and a reset judges nothing across it
    loop.Pacer.SetActiveCapabilities(PacerCapabilities(named));
    PC::DisplayReport late;
    late.FrameId = before.FrameId;
    late.DisplayTime = At(before.IntendedDisplayTime.Nanoseconds() + (5 * Period));
    loop.Pacer.AddDisplayReport(late);
    EXPECT_EQ(loop.Pacer.DisplayErrors(), state);
    loop.Pacer.SetActiveCapabilities(PacerCapabilities(named | PacerCapability::DisplayTimes));
    loop.Pacer.Reset();
    loop.Pacer.AddDisplayReport(late);
    EXPECT_EQ(loop.Pacer.DisplayErrors().Refused, 1u);
    EXPECT_EQ(loop.Pacer.DisplayErrors().JudgedFrames, 98u);
  }
}

TEST(TierPacer, DisplayReportsGoOnAcrossAChangeOfWhatPlacesTheFrames)
{
  PC::PacerSettings settings = Settings();
  settings.SetPreferredSwapInterval(2);
  Loop loop(settings, PacerCapabilities(VBlank | PacerCapability::DisplayTimes));
  loop.Pacer.SetActiveCapabilities(PacerCapabilities(PacerCapability::DisplayTimes));
  PC::FrameSchedule before;
  for (int32_t frame = 0; frame < 120; ++frame)
  {
    if (frame == 40 || frame == 80)
    {
      loop.Pacer.SetActiveCapabilities(frame == 40 ? PacerCapabilities(VBlank | PacerCapability::DisplayTimes)
                                                   : PacerCapabilities(PacerCapability::DisplayTimes));
    }
    const PC::FrameSchedule schedule = loop.Frame();
    if (frame > 0)
    {
      // Each frame shown its animation time step after the frame before it, whichever part made its time
      PC::DisplayReport report;
      report.FrameId = before.FrameId;
      report.DisplayTime = At(Start + before.AnimationTime.Nanoseconds());
      loop.Pacer.AddDisplayReport(report);
    }
    before = schedule;
  }
  const PC::DisplayErrorState state = loop.Pacer.DisplayErrors();
  EXPECT_EQ(state.Reports, 119u);
  EXPECT_EQ(state.JudgedFrames, 118u);
  EXPECT_EQ(state.ErrorFrames, 0u);
}

// The wait for the GPU's work on an earlier frame (a fence, a frame slot): what holds the loop where the application can make
// it and there is no wait for a present. A mechanism of the tiers without that wait, and no tier of its own.

TEST(TierPacer, TheWaitForTheGpusWorkIsAskedForWhereThereIsNoWaitForAPresent)
{
  constexpr PacerCapability GpuWait = PacerCapability::WaitForGpuWork;
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    for (const PacerCapability named : {PacerCapability::NoCapabilities, VBlank, AtTime, VBlank | AtTime})
    {
      PC::PacerSettings settings = Settings(aim);
      settings.SetMaxFramesInFlight(2);
      // It changes no tier
      Loop loop(settings, PacerCapabilities(named | GpuWait));
      EXPECT_EQ(loop.Pacer.Rating().Tier, PC::TierPacer(settings, PacerCapabilities(named)).Rating().Tier);
      // The frame before with the aim of low latency, the one before that with smoothness
      const uint64_t back = aim == PC::PacerAim::Smoothness ? 2u : 1u;
      for (int32_t frame = 0; frame < 40; ++frame)
      {
        const PC::FrameSchedule schedule = loop.Frame();
        ASSERT_EQ(loop.GpuWaitFrameId, schedule.FrameId > back ? schedule.FrameId - back : 0u) << frame;
        ASSERT_FALSE(loop.Plan.WaitsForPresent());
      }
      EXPECT_EQ(loop.GpuWaitsAskedFor, 40u - static_cast<uint32_t>(back));
      EXPECT_EQ(loop.WaitsAskedFor, 0u);
      EXPECT_EQ(loop.Pacer.GpuWaitTimeouts(), 0u);
      EXPECT_EQ(loop.Pacer.FrameWindow().LateFrames, 0u);
    }
    for (const PacerCapability named : {Wait, VBlank | Wait})
    {
      // With a wait for a present that wait holds the loop, and the GPU's work is not waited for
      Loop loop(Settings(aim), PacerCapabilities(named | GpuWait));
      for (int32_t frame = 0; frame < 40; ++frame)
      {
        static_cast<void>(loop.Frame());
      }
      EXPECT_EQ(loop.GpuWaitsAskedFor, 0u);
      EXPECT_GT(loop.WaitsAskedFor, 30u);
      // A report the plan did not ask for is not taken
      PC::GpuWaitReport stray;
      stray.FrameId = 40;
      stray.Done = false;
      loop.Pacer.AddGpuWait(stray);
      EXPECT_EQ(loop.Pacer.GpuWaitTimeouts(), 0u);

      // The wait for a present left out of the active set: the wait for the GPU's work takes its place, and back
      loop.Pacer.SetActiveCapabilities(PacerCapabilities((named | GpuWait) & VBlank).Capabilities() == VBlank ? PacerCapabilities(VBlank | GpuWait)
                                                                                                              : PacerCapabilities(GpuWait));
      const uint32_t presentWaits = loop.WaitsAskedFor;
      for (int32_t frame = 0; frame < 20; ++frame)
      {
        static_cast<void>(loop.Frame());
      }
      EXPECT_EQ(loop.GpuWaitsAskedFor, 20u);
      EXPECT_EQ(loop.WaitsAskedFor, presentWaits);
      loop.Pacer.SetActiveCapabilities(PacerCapabilities(named | GpuWait));
      for (int32_t frame = 0; frame < 20; ++frame)
      {
        static_cast<void>(loop.Frame());
      }
      EXPECT_EQ(loop.GpuWaitsAskedFor, 20u);
      EXPECT_GT(loop.WaitsAskedFor, presentWaits + 15u);
    }
  }
}

TEST(TierPacer, AWaitForTheGpusWorkThatRanOutIsCountedAndTheFramesGoOn)
{
  constexpr PacerCapability GpuWait = PacerCapability::WaitForGpuWork;
  PC::TierPacer pacer(Settings(), PacerCapabilities(GpuWait));
  int64_t now = Start;
  for (uint64_t frame = 1; frame <= 20; ++frame)
  {
    const PC::FrameStartPlan plan = pacer.PlanFrame(At(now));
    if (plan.WaitsForGpuWork())
    {
      // The GPU never finishes: every wait takes its whole time
      ASSERT_EQ(plan.WaitForGpuWorkFrameId, frame - 1u);
      PC::GpuWaitReport wait;
      wait.FrameId = plan.WaitForGpuWorkFrameId;
      wait.BeginTime = At(now);
      now += plan.WaitForGpuWorkTimeout.Nanoseconds();
      wait.EndTime = At(now);
      wait.Done = false;
      pacer.AddGpuWait(wait);
      ASSERT_FALSE(pacer.PlanFrame(At(now)).WaitsForGpuWork());
    }
    const PC::FrameSchedule schedule = pacer.BeginFrame(At(now));
    ASSERT_EQ(schedule.FrameId, frame);
    const PC::PresentPlan present = pacer.EndFrame(At(now + Work));
    PC::PresentReport report;
    report.FrameId = present.FrameId;
    report.CallTime = At(now + Work);
    report.ReturnTime = At(now + Work);
    pacer.AddPresent(report);
    now += Work + 100'000;
  }
  EXPECT_EQ(pacer.GpuWaitTimeouts(), 19u);
  // A loop held that long is late: its frames are late frames to the rule, as those of any loop that does not keep up
  EXPECT_GT(pacer.FrameWindow().LateFrames, 15u);
}

// A start the display's side held, on vertical blanks (the proposal's decision 17): the frames before it were still on their
// way and are shown one after the other, so the animation time does not step over the refreshes the loop was held for.

namespace
{
  //! What held the loop for five refresh periods before a frame
  enum class Held
  {
    Nothing,
    Acquire,
    FrameSlot,
  };

  //! Twenty frames, then five refresh periods in which the loop stands before the next frame, reported as given: that frame
  PC::FrameSchedule FrameAfterAHold(Loop& rLoop, const Held held, const int64_t gpuWorkNanoseconds)
  {
    PC::FrameSchedule schedule;
    for (int32_t frame = 0; frame < 20; ++frame)
    {
      schedule = rLoop.Frame();
      if (gpuWorkNanoseconds > 0)
      {
        // The GPU's work on the frame, begun when the CPU was done with it
        const int64_t begin = rLoop.StartNanoseconds + Work;
        rLoop.Pacer.AddGpuWork(PC::GpuWorkReport::Times(schedule.FrameId, At(begin), At(begin + gpuWorkNanoseconds)));
      }
    }
    if (held != Held::Nothing)
    {
      PC::SystemWaitReport wait;
      wait.Kind = held == Held::Acquire ? PC::SystemWaitKind::Acquire : PC::SystemWaitKind::FrameSlot;
      wait.BeginTime = At(rLoop.Now);
      wait.EndTime = At(rLoop.Now + (5 * Period));
      rLoop.Pacer.AddSystemWait(wait);
    }
    rLoop.Now += 5 * Period;
    return rLoop.Frame();
  }
}

TEST(TierPacer, OnVerticalBlanksAStartTheDisplaysSideHeldIsNotSteppedOver)
{
  for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
  {
    for (const PacerCapability named : {VBlank, VBlank | Wait})
    {
      SCOPED_TRACE(testing::Message() << "aim " << static_cast<int32_t>(aim) << " set " << static_cast<uint32_t>(named));
      // Nobody says what held the loop: the time passed, and the animation time steps over it
      Loop unsaid(Settings(aim), PacerCapabilities(named));
      const PC::FrameSchedule late = FrameAfterAHold(unsaid, Held::Nothing, 0);
      // By the vertical blanks its start was late for (fewer than five where a frame is made ahead, or started late in its
      // refresh)
      const auto lateBlanks = static_cast<uint64_t>((late.AnimationStep.Nanoseconds() - Period) / Period);
      EXPECT_GE(lateBlanks, 3u);
      EXPECT_LE(lateBlanks, 5u);
      EXPECT_EQ(unsaid.Pacer.DisplayHeldRefreshes(), 0u);

      // A wait for an image held it: the display's side. The frame is for the vertical blank after the last one's, and the
      // refreshes it was late by are behind the clock
      Loop acquire(Settings(aim), PacerCapabilities(named));
      const PC::FrameSchedule held = FrameAfterAHold(acquire, Held::Acquire, 0);
      EXPECT_EQ(held.AnimationStep.Nanoseconds(), Period);
      EXPECT_EQ(acquire.Pacer.DisplayHeldRefreshes(), lateBlanks);
      EXPECT_EQ(acquire.Pacer.RefreshesBehindClock(), unsaid.Pacer.RefreshesBehindClock() + lateBlanks);
      // Its time is that of a real vertical blank all the same: the first one it can make
      EXPECT_EQ(held.IntendedDisplayTime, late.IntendedDisplayTime);
      EXPECT_EQ(held.FrameId, late.FrameId);
      // And the frames go on from there a refresh apart, none of them late for it
      for (int32_t frame = 0; frame < 20; ++frame)
      {
        const PC::FrameSchedule next = acquire.Frame();
        ASSERT_EQ(next.AnimationStep.Nanoseconds(), Period) << frame;
      }
      EXPECT_EQ(acquire.Pacer.DisplayHeldRefreshes(), lateBlanks);
      EXPECT_EQ(acquire.Pacer.FrameWindow().LateFrames, 0u);

      // A wait for a frame slot, and no GPU time reported: not known whose wait it was, so it is the GPU's, and time passed
      Loop slotUnknown(Settings(aim), PacerCapabilities(named));
      EXPECT_EQ(FrameAfterAHold(slotUnknown, Held::FrameSlot, 0).AnimationStep, late.AnimationStep);
      EXPECT_EQ(slotUnknown.Pacer.DisplayHeldRefreshes(), 0u);

      // A wait for a frame slot while the GPU's work on a frame takes a hundredth of a refresh: the GPU did not work for it,
      // the display's side held the loop
      Loop slotIdle(Settings(aim), PacerCapabilities(named));
      EXPECT_EQ(FrameAfterAHold(slotIdle, Held::FrameSlot, Period / 100).AnimationStep.Nanoseconds(), Period);
      EXPECT_EQ(slotIdle.Pacer.DisplayHeldRefreshes(), lateBlanks);

      // The same wait while the GPU's work on a frame takes five refreshes: the GPU's, and time passed
      Loop slotBusy(Settings(aim), PacerCapabilities(named));
      const PC::FrameSchedule busy = FrameAfterAHold(slotBusy, Held::FrameSlot, 5 * Period);
      EXPECT_GT(busy.AnimationStep.Nanoseconds(), Period);
      EXPECT_EQ(slotBusy.Pacer.DisplayHeldRefreshes(), 0u);
    }
  }
}

TEST(TierPacer, OnVerticalBlanksAWaitForTheGpusWorkThatTheGpuDidNotWorkForIsTheDisplaysSide)
{
  constexpr PacerCapability GpuWait = PacerCapability::WaitForGpuWork;
  PC::TierPacer pacer(Settings(PC::PacerAim::Smoothness), PacerCapabilities(VBlank | GpuWait));
  int64_t now = Start;
  PC::FrameSchedule schedule;
  for (uint64_t frame = 1; frame <= 40; ++frame)
  {
    PC::VBlankReading reading;
    reading.VBlankTime = At(Start + (((now - Start) / Period) * Period));
    reading.ReadTime = At(now);
    pacer.AddVBlank(reading);
    PC::FrameStartPlan plan = pacer.PlanFrame(At(now));
    if (plan.WaitsForGpuWork())
    {
      // Before the 30th frame the wait takes five refresh periods; the GPU's work on a frame is a hundredth of one
      PC::GpuWaitReport wait;
      wait.FrameId = plan.WaitForGpuWorkFrameId;
      wait.BeginTime = At(now);
      now += frame == 30 ? 5 * Period : 20'000;
      wait.EndTime = At(now);
      pacer.AddGpuWait(wait);
      plan = pacer.PlanFrame(At(now));
    }
    now = plan.WaitsForStartTime() ? std::max(now, plan.StartTime.Nanoseconds()) : now;
    schedule = pacer.BeginFrame(At(now));
    if (frame > 5)
    {
      // Held or not, every frame is a refresh after the one before it to the animation
      ASSERT_EQ(schedule.AnimationStep.Nanoseconds(), Period) << frame;
    }
    const PC::PresentPlan present = pacer.EndFrame(At(now + Work));
    pacer.AddGpuWork(PC::GpuWorkReport::Times(schedule.FrameId, At(now + Work), At(now + Work + (Period / 100))));
    const int64_t presentNanoseconds = present.WaitsForPresentTime() ? std::max(now + Work, present.PresentTime.Nanoseconds()) : now + Work;
    PC::PresentReport report;
    report.FrameId = present.FrameId;
    report.CallTime = At(presentNanoseconds);
    report.ReturnTime = At(presentNanoseconds);
    pacer.AddPresent(report);
    now = presentNanoseconds + 100'000;
  }
  // Made ahead, the frame was late for three vertical blanks of the five periods
  EXPECT_EQ(pacer.DisplayHeldRefreshes(), 3u);
  EXPECT_EQ(pacer.RefreshesBehindClock(), 3u);
  EXPECT_EQ(pacer.GpuWaitTimeouts(), 0u);
}
