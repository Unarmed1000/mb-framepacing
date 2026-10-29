// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// EXPERIMENTAL. An application that is not pacing (sdk/doc/pacer-design.md, decision 7): every call is made as before and
// gives nothing to carry out, what is reported is taken, and pacing starts again with a history.

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/capability/PacerCapabilities.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <mb/framepacing/pacer/capability/PacerTier.hpp>
#include <mb/framepacing/pacer/capability/PacerTierText.hpp>
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
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <mb/framepacing/pacer/tier/FreeRunLoop.hpp>
#include <mb/framepacing/pacer/tier/PacerHandover.hpp>
#include <mb/framepacing/pacer/tier/TierPacer.hpp>
#include <gtest/gtest.h>
#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <string_view>
#include <vector>
#include "FrameLoopSimulation.hpp"
#include "LoopFrame.hpp"
#include "LoopSettings.hpp"

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;
namespace Sim = MB::FramePacing::Pacer::Simulation;
namespace Text = MB::FramePacing::Pacer::PacerTierText;
using PC::PacerCapabilities;
using PC::PacerCapability;
using PC::PacerTier;

namespace
{
  // 100 Hz: a refresh period of exactly 10,000,000 ns. The display's vertical blanks are at Start + n periods
  constexpr int64_t Period = 10'000'000;
  constexpr int64_t Start = 1'000'000'000;
  constexpr int64_t Work = 3'000'000;
  // A loop that is not paced makes a frame every so often: no whole number of refreshes
  constexpr int64_t FreeStep = 7'000'000;

  const PC::RefreshPeriod g_hz100 = PC::RefreshPeriod::FromRate(100);

  constexpr PacerCapability Wait = PacerCapability::WaitForPresent;
  constexpr PacerCapability VBlank = PacerCapability::VBlankTimes;

  constexpr FP::NanosecondTickCount At(const int64_t nanoseconds) noexcept
  {
    return FP::NanosecondTickCount(nanoseconds);
  }

  PC::PacerSettings Settings(const PC::PacerAim aim = PC::PacerAim::LowLatency)
  {
    PC::PacerSettings settings(g_hz100);
    settings.SetAim(aim);
    // No pause after start-up but in the test of it
    settings.SetStartupPauseRefreshes(0);
    settings.SetMinWaitTimeout(FP::NanosecondTimeDuration::Zero());
    return settings;
  }

  //! An application's frame loop on the pacer: it carries out what it is given, the display shows every present it waits for
  //! at once, and while the pacer gives it nothing it makes a frame every FreeStep
  struct Loop
  {
    PC::TierPacer Pacer;
    int64_t Now{Start};
    PC::FrameStartPlan Plan;
    PC::PresentPlan Present;
    int64_t StartNanoseconds{0};
    uint32_t WaitsAskedFor{0};

    Loop(const PC::PacerSettings& settings, const PacerCapabilities& capabilities)
      : Pacer(settings, capabilities)
    {
    }

    PC::FrameSchedule Frame()
    {
      if (Pacer.ActiveCapabilities().Has(VBlank))
      {
        PC::VBlankReading reading;
        reading.VBlankTime = At(Start + (((Now - Start) / Period) * Period));
        reading.ReadTime = At(Now);
        Pacer.AddVBlank(reading);
      }
      const int64_t planned = Now;
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
        Plan.StartTime = Pacer.PlanFrame(At(Now)).StartTime;
      }
      StartNanoseconds = Plan.WaitsForStartTime() ? Plan.StartTime.Nanoseconds() : Now;
      const PC::FrameSchedule schedule = Pacer.BeginFrame(At(StartNanoseconds));
      Present = Pacer.EndFrame(At(StartNanoseconds + Work));
      const int64_t presentNanoseconds = Present.WaitsForPresentTime() ? Present.PresentTime.Nanoseconds() : StartNanoseconds + Work;
      PC::PresentReport report;
      report.FrameId = Present.FrameId;
      report.CallTime = At(presentNanoseconds);
      report.ReturnTime = At(presentNanoseconds + 60'000);
      Pacer.AddPresent(report);
      // A loop the pacer gives nothing makes its next frame FreeStep after this one's start
      Now = Pacer.IsPacing() ? presentNanoseconds + 100'000 : planned + FreeStep;
      return schedule;
    }
  };
}

TEST(NotPacing, EveryCallGivesNothingToCarryOutAndTheFramesGoOn)
{
  for (const PacerCapability named : {PacerCapability::NoCapabilities, Wait, VBlank, VBlank | Wait, VBlank | Wait | PacerCapability::PresentAtTime,
                                      PacerCapability::PresentAfterDuration, PacerCapability::PresentSwapInterval | PacerCapability::WaitForGpuWork})
  {
    for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
    {
      PC::PacerSettings settings = Settings(aim);
      settings.SetPreferredSwapInterval(2);
      Loop loop(settings, PacerCapabilities(named, named == (PacerCapability::PresentSwapInterval | PacerCapability::WaitForGpuWork) ? 4u : 1u));
      EXPECT_TRUE(loop.Pacer.IsPacing());
      PC::FrameSchedule schedule;
      for (int32_t frame = 0; frame < 30; ++frame)
      {
        schedule = loop.Frame();
      }
      ASSERT_EQ(schedule.FrameId, 30u);
      ASSERT_EQ(schedule.SwapInterval, 2u);
      const PacerTier tier = loop.Pacer.WorkingTier();
      const PC::FrameWindowState window = loop.Pacer.FrameWindow();
      const int64_t lastPacedStart = loop.StartNanoseconds;

      loop.Pacer.SetPacing(false);
      EXPECT_FALSE(loop.Pacer.IsPacing());
      for (int32_t frame = 0; frame < 25; ++frame)
      {
        const PC::FrameSchedule before = schedule;
        const int64_t now = loop.Now;
        schedule = loop.Frame();
        // Nothing to wait for before the frame: it starts when the loop is there
        ASSERT_FALSE(loop.Plan.WaitsForPresent()) << frame;
        ASSERT_FALSE(loop.Plan.WaitsForGpuWork()) << frame;
        ASSERT_FALSE(loop.Plan.WaitsForStartTime()) << frame;
        ASSERT_EQ(loop.StartNanoseconds, now) << frame;
        // The frames and the animation time go on, and a step is the time since the frame before started
        ASSERT_EQ(schedule.FrameId, before.FrameId + 1u) << frame;
        ASSERT_EQ(schedule.AnimationStep.Nanoseconds(), frame == 0 ? now - lastPacedStart : FreeStep) << frame;
        ASSERT_EQ(schedule.AnimationTime.Nanoseconds(), before.AnimationTime.Nanoseconds() + schedule.AnimationStep.Nanoseconds()) << frame;
        // Nothing is aimed for: no display time, no start for the next frame, no target, and what the application prefers
        ASSERT_EQ(schedule.SwapInterval, 1u) << frame;
        ASSERT_EQ(schedule.IntendedDisplayTime, FP::NanosecondTickCount()) << frame;
        ASSERT_EQ(schedule.NextFrameStartTime, FP::NanosecondTickCount()) << frame;
        ASSERT_EQ(schedule.TargetFrameTime, FP::NanosecondTimeDuration()) << frame;
        ASSERT_EQ(schedule.PreferredFrameTime.Nanoseconds(), 2 * Period) << frame;
        ASSERT_EQ(schedule.RefreshPeriod.Nanoseconds(), Period) << frame;
        ASSERT_EQ(schedule.Change, PC::SwapIntervalChange::Unchanged) << frame;
        ASSERT_FALSE(schedule.Paced) << frame;
        // Nothing to wait for before the present, and nothing to give it
        ASSERT_EQ(loop.Present.FrameId, schedule.FrameId) << frame;
        ASSERT_FALSE(loop.Present.WaitsForPresentTime()) << frame;
        ASSERT_EQ(loop.Present.NotBeforeTime, FP::NanosecondTickCount()) << frame;
        ASSERT_EQ(loop.Present.MinimumDuration, FP::NanosecondTimeDuration()) << frame;
        ASSERT_EQ(loop.Present.SwapInterval, 1u) << frame;
        ASSERT_EQ(loop.Present.CpuBusy.Nanoseconds(), Work) << frame;
        // The rule gets no frame and keeps its swap interval, and the tier that would pace is the one that did
        ASSERT_EQ(loop.Pacer.FrameWindow(), window) << frame;
        ASSERT_EQ(loop.Pacer.SwapInterval(), 2u) << frame;
        ASSERT_EQ(loop.Pacer.WorkingTier(), tier) << frame;
      }

      // Pacing again: the first frame starts at once, is placed anew and judged against nothing, and the frames, the
      // animation time and the swap interval go on
      loop.Pacer.SetPacing(true);
      EXPECT_TRUE(loop.Pacer.IsPacing());
      const PC::FrameSchedule before = schedule;
      const int64_t now = loop.Now;
      schedule = loop.Frame();
      EXPECT_GE(loop.StartNanoseconds, now);
      EXPECT_LT(loop.StartNanoseconds, now + (2 * Period));
      EXPECT_EQ(schedule.FrameId, before.FrameId + 1u);
      EXPECT_EQ(schedule.SwapInterval, 2u);
      EXPECT_EQ(schedule.AnimationStep.Nanoseconds(), 2 * Period);
      EXPECT_NE(schedule.IntendedDisplayTime, FP::NanosecondTickCount());
      EXPECT_EQ(schedule.TargetFrameTime.Nanoseconds(), 2 * Period);
      EXPECT_TRUE(schedule.Paced);
      EXPECT_EQ(loop.Pacer.FrameWindow().Frames, window.Frames);
      EXPECT_EQ(loop.Pacer.FrameWindow().LateFrames, 0u);
      for (int32_t frame = 0; frame < 40; ++frame)
      {
        const PC::FrameSchedule previous = schedule;
        schedule = loop.Frame();
        ASSERT_EQ(schedule.FrameId, previous.FrameId + 1u) << frame;
        ASSERT_EQ(schedule.SwapInterval, 2u) << frame;
        ASSERT_EQ(schedule.AnimationStep.Nanoseconds(), 2 * Period) << frame;
        // Two refreshes after the frame before it, from the second paced frame on
        ASSERT_EQ(schedule.IntendedDisplayTime.Nanoseconds() - previous.IntendedDisplayTime.Nanoseconds(), 2 * Period) << frame;
      }
      EXPECT_EQ(loop.Pacer.WorkingTier(), tier);
      EXPECT_EQ(loop.Pacer.FrameWindow().LateFrames, 0u);
      EXPECT_EQ(loop.Pacer.RefreshesBehindClock(), 0u);
      EXPECT_GT(loop.Pacer.FrameWindow().Frames, window.Frames);
    }
  }
}

TEST(NotPacing, OnVerticalBlanksTheReadingsAreTakenAllAlongAndTheFramesAreOnTheBlanksAgainAtOnce)
{
  // Not pacing from before the first frame: the first vertical blank reading comes while nothing is paced
  Loop loop(Settings(), PacerCapabilities(VBlank));
  loop.Pacer.SetPacing(false);
  EXPECT_FALSE(loop.Pacer.HasVBlankReading());
  PC::FrameSchedule schedule = loop.Frame();
  // The pacer's first frame: its animation time is where the pacer starts
  EXPECT_EQ(schedule.FrameId, 1u);
  EXPECT_EQ(schedule.AnimationTime.Nanoseconds(), 0);
  EXPECT_EQ(schedule.AnimationStep.Nanoseconds(), 0);
  EXPECT_TRUE(loop.Pacer.HasVBlankReading());
  for (int32_t frame = 0; frame < 20; ++frame)
  {
    schedule = loop.Frame();
    ASSERT_EQ(schedule.AnimationStep.Nanoseconds(), FreeStep) << frame;
  }
  EXPECT_EQ(loop.Pacer.VBlankJumps(), 0u);

  loop.Pacer.SetPacing(true);
  for (int32_t frame = 0; frame < 30; ++frame)
  {
    const PC::FrameSchedule previous = schedule;
    schedule = loop.Frame();
    // Every paced frame is for a vertical blank of the display, from the first one on
    ASSERT_EQ((schedule.IntendedDisplayTime.Nanoseconds() - Start) % Period, 0) << frame;
    if (frame >= 2)
    {
      ASSERT_EQ(schedule.AnimationStep.Nanoseconds(), Period) << frame;
      ASSERT_EQ(schedule.IntendedDisplayTime.Nanoseconds() - previous.IntendedDisplayTime.Nanoseconds(), Period) << frame;
    }
  }
  EXPECT_EQ(loop.Pacer.WorkingTier(), PacerTier::VBlankPeriodOnly);
  EXPECT_EQ(loop.Pacer.VBlankJumps(), 0u);
  // The first paced frame is placed as the first frame of a run is: it began where the loop happened to be in a refresh,
  // too late to be ready for the vertical blank after it, which is found out after its present and counted once
  EXPECT_LE(loop.Pacer.FrameWindow().LateFrames, 1u);
  EXPECT_LE(loop.Pacer.RefreshesBehindClock(), 1u);
}

TEST(NotPacing, TheSwitchTakesEffectWhenTheOpenFrameHasEnded)
{
  PC::PacerSettings settings = Settings();
  settings.SetPreferredSwapInterval(2);
  Loop loop(settings, PacerCapabilities());
  for (int32_t frame = 0; frame < 10; ++frame)
  {
    (void)loop.Frame();
  }
  // Switched off inside a frame: the frame is ended as a paced one, with its present held for its two refreshes
  const PC::FrameStartPlan start = loop.Pacer.PlanFrame(At(loop.Now));
  ASSERT_TRUE(start.WaitsForStartTime());
  const PC::FrameSchedule schedule = loop.Pacer.BeginFrame(start.StartTime);
  loop.Pacer.SetPacing(false);
  EXPECT_FALSE(loop.Pacer.IsPacing());
  EXPECT_EQ(loop.Pacer.CpuBusyAt(At(start.StartTime.Nanoseconds() + 1'000'000)).Nanoseconds(), 1'000'000);
  const PC::PresentPlan present = loop.Pacer.EndFrame(At(start.StartTime.Nanoseconds() + Work));
  EXPECT_EQ(present.FrameId, schedule.FrameId);
  EXPECT_TRUE(present.WaitsForPresentTime());
  // And from then on nothing is paced
  loop.Now = present.PresentTime.Nanoseconds() + 100'000;
  EXPECT_FALSE(loop.Pacer.PlanFrame(At(loop.Now)).WaitsForStartTime());
  PC::FrameSchedule free = loop.Frame();
  EXPECT_EQ(free.FrameId, schedule.FrameId + 1u);
  EXPECT_EQ(free.TargetFrameTime, FP::NanosecondTimeDuration());
  EXPECT_FALSE(free.Paced);

  // Switched on inside a frame that is not paced: that frame ends with nothing to carry out, and the next one is paced
  const PC::FrameSchedule open = loop.Pacer.BeginFrame(At(loop.Now));
  loop.Pacer.SetPacing(true);
  EXPECT_TRUE(loop.Pacer.IsPacing());
  EXPECT_EQ(loop.Pacer.CpuBusyAt(At(loop.Now + 500'000)).Nanoseconds(), 500'000);
  const PC::PresentPlan last = loop.Pacer.EndFrame(At(loop.Now + Work));
  EXPECT_EQ(last.FrameId, open.FrameId);
  EXPECT_FALSE(last.WaitsForPresentTime());
  EXPECT_EQ(last.CpuBusy.Nanoseconds(), Work);
  loop.Now += Work + 100'000;
  free = loop.Frame();
  EXPECT_EQ(free.FrameId, open.FrameId + 1u);
  EXPECT_EQ(free.TargetFrameTime.Nanoseconds(), 2 * Period);
  EXPECT_TRUE(free.Paced);

  // Switched off and on again before a frame was made: nothing changed, and the same once more says nothing new
  loop.Pacer.SetPacing(false);
  loop.Pacer.SetPacing(true);
  loop.Pacer.SetPacing(true);
  const PC::FrameSchedule next = loop.Frame();
  EXPECT_EQ(next.FrameId, free.FrameId + 1u);
  EXPECT_EQ(next.AnimationStep.Nanoseconds(), 2 * Period);
}

TEST(NotPacing, AChangeOfTheActiveSetWhileNotPacingIsWhatPacesAfterwards)
{
  for (const bool toVBlanks : {true, false})
  {
    PC::PacerSettings settings = Settings();
    settings.SetPreferredSwapInterval(2);
    Loop loop(settings, PacerCapabilities(VBlank | Wait));
    loop.Pacer.SetActiveCapabilities(PacerCapabilities(toVBlanks ? PacerCapability::NoCapabilities : VBlank));
    PC::FrameSchedule schedule;
    for (int32_t frame = 0; frame < 20; ++frame)
    {
      schedule = loop.Frame();
    }
    // Not pacing, and then another part is to place the frames, with the wait: in one frame, both waiting for its end
    (void)loop.Pacer.BeginFrame(At(loop.Now));
    const PC::FrameWindowState window = loop.Pacer.FrameWindow();
    ASSERT_GT(window.Frames, 5u);
    loop.Pacer.SetPacing(false);
    loop.Pacer.SetActiveCapabilities(PacerCapabilities((toVBlanks ? VBlank : PacerCapability::NoCapabilities) | Wait));
    (void)loop.Pacer.EndFrame(At(loop.Now + Work));
    loop.Now += FreeStep;
    for (int32_t frame = 0; frame < 10; ++frame)
    {
      schedule = loop.Frame();
      ASSERT_EQ(schedule.TargetFrameTime, FP::NanosecondTimeDuration()) << frame;
      // The rule went with the part that is to pace: its frame window and its swap interval are what they were
      ASSERT_EQ(loop.Pacer.FrameWindow().Frames, window.Frames) << frame;
      ASSERT_EQ(loop.Pacer.SwapInterval(), 2u) << frame;
    }
    EXPECT_EQ(loop.WaitsAskedFor, 0u);
    // And changed back and forth while nothing is paced, which only moves the rule
    loop.Pacer.SetActiveCapabilities(PacerCapabilities(toVBlanks ? PacerCapability::NoCapabilities : VBlank));
    loop.Pacer.SetActiveCapabilities(PacerCapabilities((toVBlanks ? VBlank : PacerCapability::NoCapabilities) | Wait));
    schedule = loop.Frame();
    EXPECT_EQ(loop.Pacer.FrameWindow().Frames, window.Frames);

    loop.Pacer.SetPacing(true);
    for (int32_t frame = 0; frame < 30; ++frame)
    {
      const PC::FrameSchedule previous = schedule;
      schedule = loop.Frame();
      ASSERT_EQ(schedule.FrameId, previous.FrameId + 1u) << frame;
      ASSERT_EQ(schedule.SwapInterval, 2u) << frame;
      ASSERT_EQ(schedule.AnimationStep.Nanoseconds(), 2 * Period) << frame;
    }
    EXPECT_EQ(loop.Pacer.WorkingTier(), toVBlanks ? PacerTier::VBlankWaitForPresent : PacerTier::TimerWaitForPresent);
    // The wait asks for presents again, of frames that were made while nothing was paced too
    EXPECT_GT(loop.WaitsAskedFor, 20u);
    EXPECT_EQ(loop.Pacer.PresentWaitTimeouts(), 0u);
    EXPECT_EQ(loop.Pacer.FrameWindow().LateFrames, 0u);
  }
}

TEST(NotPacing, ThePresentsMadeWhileNotPacingAreTheOnesTheFirstWaitAsksFor)
{
  Loop loop(Settings(), PacerCapabilities(Wait));
  loop.Pacer.SetPacing(false);
  PC::FrameSchedule schedule;
  for (int32_t frame = 0; frame < 12; ++frame)
  {
    schedule = loop.Frame();
  }
  ASSERT_EQ(schedule.FrameId, 12u);
  loop.Pacer.SetPacing(true);
  // The plan before the first paced frame asks for the present two back from the frame about to be made: frame 11's
  const PC::FrameStartPlan plan = loop.Pacer.PlanFrame(At(loop.Now));
  EXPECT_EQ(plan.WaitForPresentFrameId, 11u);

  // A swap chain made anew while nothing is paced: its presents are gone, and none of them is asked for
  Loop gone(Settings(), PacerCapabilities(Wait));
  gone.Pacer.SetPacing(false);
  for (int32_t frame = 0; frame < 12; ++frame)
  {
    (void)gone.Frame();
  }
  gone.Pacer.ForgetPresents();
  gone.Pacer.SetPacing(true);
  EXPECT_FALSE(gone.Pacer.PlanFrame(At(gone.Now)).WaitsForPresent());

  // And so does a present the system did not take: it and the ones before it will not be shown
  Loop refused(Settings(), PacerCapabilities(Wait));
  refused.Pacer.SetPacing(false);
  for (int32_t frame = 0; frame < 12; ++frame)
  {
    (void)refused.Frame();
  }
  PC::PresentReport report;
  report.FrameId = 12;
  report.CallTime = At(refused.Now);
  report.ReturnTime = At(refused.Now + 60'000);
  report.Accepted = false;
  refused.Pacer.AddPresent(report);
  refused.Pacer.SetPacing(true);
  EXPECT_FALSE(refused.Pacer.PlanFrame(At(refused.Now)).WaitsForPresent());
}

TEST(NotPacing, WaitsNoPlanAskedForAreNotTakenAndTheOtherReportsAre)
{
  PC::PacerSettings settings = Settings();
  settings.SetSystemHoldsLoop(true);
  Loop loop(settings, PacerCapabilities(Wait | PacerCapability::WaitForGpuWork | PacerCapability::DisplayTimes));
  PC::FrameSchedule schedule;
  for (int32_t frame = 0; frame < 10; ++frame)
  {
    schedule = loop.Frame();
  }
  loop.Pacer.SetPacing(false);
  const uint64_t systemHeld = loop.Pacer.SystemHeldFrames();
  for (int32_t frame = 0; frame < 10; ++frame)
  {
    const PC::FrameSchedule before = schedule;
    // A wait for a present and one for the GPU's work that ran out, and a long wait for an image: none is the pacer's
    PC::PresentWaitReport wait;
    wait.FrameId = before.FrameId;
    wait.BeginTime = At(loop.Now);
    wait.EndTime = At(loop.Now + Period);
    wait.Shown = false;
    loop.Pacer.AddPresentWait(wait);
    PC::GpuWaitReport gpuWait;
    gpuWait.FrameId = before.FrameId;
    gpuWait.BeginTime = At(loop.Now);
    gpuWait.EndTime = At(loop.Now + Period);
    gpuWait.Done = false;
    loop.Pacer.AddGpuWait(gpuWait);
    loop.Pacer.AddSystemWait({PC::SystemWaitKind::Acquire, At(loop.Now), At(loop.Now + Period)});
    schedule = loop.Frame();

    // The GPU's work on the frame before, and when it was shown: three refreshes after its start
    PC::GpuWorkReport work;
    work.FrameId = before.FrameId;
    work.Duration = FP::NanosecondTimeDuration::FromNanoseconds(2'500'000);
    loop.Pacer.AddGpuWork(work);
    PC::DisplayReport shown;
    shown.FrameId = schedule.FrameId;
    shown.DisplayTime = At(loop.StartNanoseconds + (3 * Period));
    loop.Pacer.AddDisplayReport(shown);
  }
  EXPECT_EQ(loop.Pacer.PresentWaitTimeouts(), 0u);
  EXPECT_EQ(loop.Pacer.GpuWaitTimeouts(), 0u);
  EXPECT_FALSE(loop.Pacer.PresentWaitsStopped());
  EXPECT_EQ(loop.Pacer.SystemHeldFrames(), systemHeld);
  // The GPU's time is the one that was reported while nothing was paced, then and when pacing starts again
  EXPECT_EQ(loop.Pacer.GpuTime().Nanoseconds(), 2'500'000);
  EXPECT_EQ(loop.Pacer.LastPresentBlocked().Nanoseconds(), 60'000);
  // The display reports are counted as ever: each frame's animation step was the time since the one before, and so was
  // the time between their display times
  const PC::DisplayErrorState state = loop.Pacer.DisplayErrors();
  EXPECT_EQ(state.Reports, 10u);
  EXPECT_EQ(state.Refused, 0u);
  EXPECT_EQ(state.JudgedFrames, 9u);
  EXPECT_EQ(state.ErrorFrames, 0u);
  EXPECT_EQ(state.StartToDisplayLongest.Nanoseconds(), 3 * Period);

  loop.Pacer.SetPacing(true);
  EXPECT_EQ(loop.Pacer.GpuTime().Nanoseconds(), 2'500'000);
  EXPECT_EQ(loop.Pacer.LastPresentBlocked().Nanoseconds(), 60'000);
  (void)loop.Frame();
  EXPECT_EQ(loop.Pacer.GpuTime().Nanoseconds(), 2'500'000);
}

TEST(NotPacing, AcrossAPauseTheAnimationTimeGoesOnByOneRefresh)
{
  Loop loop(Settings(), PacerCapabilities());
  loop.Pacer.SetPacing(false);
  PC::FrameSchedule schedule;
  for (int32_t frame = 0; frame < 5; ++frame)
  {
    schedule = loop.Frame();
  }
  EXPECT_EQ(schedule.AnimationStep.Nanoseconds(), FreeStep);
  // A gap longer than the frame window is a pause, and a clock that went back is one: nothing is measured across either
  loop.Now += loop.Pacer.Settings().FrameWindowLength().Nanoseconds() + Period;
  schedule = loop.Frame();
  EXPECT_EQ(schedule.AnimationStep.Nanoseconds(), Period);
  schedule = loop.Frame();
  EXPECT_EQ(schedule.AnimationStep.Nanoseconds(), FreeStep);
  loop.Now -= 20 * Period;
  schedule = loop.Frame();
  EXPECT_EQ(schedule.AnimationStep.Nanoseconds(), Period);

  // A reset starts the frames again: the frame after it is measured against nothing, and their ids and the animation go on
  const PC::FrameSchedule before = loop.Frame();
  loop.Pacer.Reset();
  EXPECT_FALSE(loop.Pacer.IsPacing());
  EXPECT_EQ(loop.Pacer.CpuBusyAt(At(loop.Now)).Nanoseconds(), 0);
  schedule = loop.Frame();
  EXPECT_EQ(schedule.FrameId, before.FrameId + 1u);
  EXPECT_EQ(schedule.AnimationStep.Nanoseconds(), Period);
  EXPECT_EQ(schedule.AnimationTime.Nanoseconds(), before.AnimationTime.Nanoseconds() + Period);
  // An end without a frame gives nothing
  PC::FreeRunLoop free;
  EXPECT_EQ(free.EndFrame(At(Start)).FrameId, 0u);
  EXPECT_FALSE(free.IsFrameOpen());
  EXPECT_FALSE(free.GiveOver().HasFrame);
}

TEST(NotPacing, ThePauseAfterStartUpIsMadeOnceMoreWhenFramesWereMadeUnpaced)
{
  // The lowest tier with the aim of low latency makes one pause after start-up, for the frames that pile up behind the first
  // presents. Frames made while nothing is paced pile up the same way
  PC::PacerSettings settings = Settings();
  settings.SetStartupPauseRefreshes(4);
  settings.SetStartupPauseDelay(FP::NanosecondTimeSpan(20 * Period));
  Loop loop(settings, PacerCapabilities());
  for (int32_t frame = 0; frame < 60; ++frame)
  {
    (void)loop.Frame();
  }
  ASSERT_EQ(loop.Pacer.StartupPauses(), 1u);

  // Switched off and on with no frame in between: nothing piled up, and no second pause
  loop.Pacer.SetPacing(false);
  loop.Pacer.SetPacing(true);
  for (int32_t frame = 0; frame < 60; ++frame)
  {
    (void)loop.Frame();
  }
  EXPECT_EQ(loop.Pacer.StartupPauses(), 1u);

  loop.Pacer.SetPacing(false);
  for (int32_t frame = 0; frame < 10; ++frame)
  {
    (void)loop.Frame();
  }
  EXPECT_EQ(loop.Pacer.StartupPauses(), 1u);
  loop.Pacer.SetPacing(true);
  for (int32_t frame = 0; frame < 60; ++frame)
  {
    (void)loop.Frame();
  }
  EXPECT_EQ(loop.Pacer.StartupPauses(), 2u);
}

TEST(NotPacing, TheWordsForItAreThereAndFitTheirPlaces)
{
  EXPECT_FALSE(Text::NotPacingName.empty());
  EXPECT_FALSE(Text::NotPacingDescription.empty());
  EXPECT_FALSE(Text::NotPacingShortDescription.empty());
  EXPECT_LE(Text::NotPacingShortDescription.size(), Text::ShortDescriptionMaxLength);
  // Each ends with a zero, as every text of the tiers does: it can be given to a function that takes a C string
  for (const std::string_view text : {Text::NotPacingName, Text::NotPacingDescription, Text::NotPacingShortDescription})
  {
    // A string literal: read as a C string it ends where the view does, which is what this checks
    // NOLINTNEXTLINE(bugprone-suspicious-stringview-data-usage)
    EXPECT_EQ(std::strlen(text.data()), text.size()) << text;
  }
}

TEST(NotPacing, ASimulatedLoopThatStopsPacingAndStartsAgainIsOnItsRefreshesAgain)
{
  // Light work on a display whose swap chain has three images, so that a loop nothing paces is held by the display's side
  // when its queue is full: a frame every refresh, with one waiting. Paced, not paced for 200 frames, paced again
  for (const PacerCapability named : {PacerCapability::NoCapabilities, Wait, VBlank, VBlank | Wait})
  {
    for (const PC::PacerAim aim : {PC::PacerAim::LowLatency, PC::PacerAim::Smoothness})
    {
      Sim::LoopSettings settings;
      settings.Frames = 900;
      settings.Aim = aim;
      settings.CpuWork = {100'000, 100'000};
      settings.GpuWork = {300'000, 300'000};
      settings.Display.Images = 3;
      settings.PacingChanges = {{200, false}, {400, true}};
      const std::vector<Sim::LoopFrame> frames = Sim::SimulateTierLoop(settings, named);
      ASSERT_EQ(frames.size(), 900u);
      const int64_t period = 1'000'000'000 / 240;
      const auto message = [named, aim](const std::size_t index)
      { return testing::Message() << static_cast<uint32_t>(named) << ' ' << static_cast<int32_t>(aim) << " frame " << index; };
      for (std::size_t index = 1; index < frames.size(); ++index)
      {
        ASSERT_EQ(frames[index].FrameId, frames[index - 1].FrameId + 1u) << message(index);
        ASSERT_GT(frames[index].AnimationNanoseconds, frames[index - 1].AnimationNanoseconds) << message(index);
        const bool paced = index < 200 || index >= 400;
        ASSERT_EQ(frames[index].IntendedDisplayNanoseconds != 0, paced) << message(index);
        ASSERT_EQ(frames[index].TargetFrameTimeNanoseconds != 0, paced) << message(index);
        // No frame is late, but the first paced one where it began too late in a refresh for its vertical blank
        ASSERT_LE(frames[index].WindowLateFrames, index > 400 ? 1u : 0u) << message(index);
      }
      // Once the first paced frames are through (and the pause after start-up, where one is made), every frame is on screen
      // for one refresh. No more frames wait than did before pacing stopped, but with a wait for a present, which keeps the
      // frames that wait to the one that may and takes none away: the frame that piled up while nothing was paced stays
      int32_t waitingBefore = 0;
      for (std::size_t index = 100; index < 200; ++index)
      {
        waitingBefore = std::max(waitingBefore, frames[index].PendingAtStart);
      }
      waitingBefore = PC::HasCapability(named, Wait) ? std::max(waitingBefore, 1) : waitingBefore;
      for (std::size_t index = 700; index < frames.size(); ++index)
      {
        const int64_t onScreen = frames[index].ShownNanoseconds - frames[index - 1].ShownNanoseconds;
        ASSERT_NEAR(static_cast<double>(onScreen), static_cast<double>(period), 2.0) << message(index);
        ASSERT_LE(frames[index].PendingAtStart, waitingBefore) << message(index);
      }
    }
  }
}

TEST(NotPacing, AFrameIsLateOnlyWhenItIsAlsoHalfARefreshPastThePreferredFrameTime)
{
  // Not paced, a frame's marker carries no target frame time: its intended period is the preferred frame time, as the tools read
  // it. Every frame is animated for the FreeStep since the one before, far short of a refresh
  for (const uint32_t preferred : {1u, 2u})
  {
    PC::PacerSettings settings = Settings();
    settings.SetPreferredSwapInterval(preferred);
    Loop loop(settings, PacerCapabilities(PacerCapability::DisplayTimes));
    loop.Pacer.SetPacing(false);
    std::array<PC::FrameSchedule, 3> frames{};
    for (PC::FrameSchedule& rFrame : frames)
    {
      rFrame = loop.Frame();
    }
    EXPECT_EQ(frames[1].AnimationStep.Nanoseconds(), FreeStep) << preferred;
    EXPECT_EQ(frames[2].AnimationStep.Nanoseconds(), FreeStep) << preferred;
    // Shown just under half a refresh past the preferred frame time, then half a refresh past it: each later than it was
    // animated for by more than half a refresh, and only the second is late
    const int64_t intended = static_cast<int64_t>(preferred) * Period;
    int64_t shown = loop.Now + (10 * Period);
    for (std::size_t frame = 0; frame < frames.size(); ++frame)
    {
      shown += frame == 0 ? 0 : (frame == 1 ? intended + 4'999'999 : intended + 5'000'000);
      PC::DisplayReport report;
      report.FrameId = frames[frame].FrameId;
      report.DisplayTime = At(shown);
      loop.Pacer.AddDisplayReport(report);
    }
    const PC::DisplayErrorState state = loop.Pacer.DisplayErrors();
    EXPECT_EQ(state.JudgedFrames, 2u) << preferred;
    EXPECT_EQ(state.OffTargetFrames, 2u) << preferred;
    EXPECT_EQ(state.LateFrames, 1u) << preferred;
  }
}
