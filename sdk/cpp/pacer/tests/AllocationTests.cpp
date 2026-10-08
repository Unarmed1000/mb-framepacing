// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The pacer runs every frame, so pacing must never allocate after the pacer is made. This test binary links the counting global
// operator new/delete (mb_framepacing_test_support) and checks that every per-frame call stays at zero allocations, with present
// feedback too; only SetSettings with settings that need a larger frame window may allocate.
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/capability/PacerCapabilities.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <mb/framepacing/pacer/clock/PacerRefreshClock.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/frame/FrameStartPlan.hpp>
#include <mb/framepacing/pacer/frame/GpuWorkReport.hpp>
#include <mb/framepacing/pacer/frame/PresentFeedback.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/frame/PresentWaitReport.hpp>
#include <mb/framepacing/pacer/frame/VBlankReading.hpp>
#include <mb/framepacing/pacer/rule/SlowDownRule.hpp>
#include <mb/framepacing/pacer/tier/TierPacer.hpp>
#include <mb/framepacing/pacer/tier/TimerPeriodOnlyPacer.hpp>
#include <mb/framepacing/pacer/tier/TimerWaitForPresentPacer.hpp>
#include <mb/framepacing/pacer/tier/VBlankPeriodOnlyPacer.hpp>
#include <mb/framepacing/pacer/tier/VBlankWaitForPresentPacer.hpp>
#include <mb/framepacing/testing/AllocationCounter.hpp>
#include <gtest/gtest.h>
#include <algorithm>
#include <cstdint>
#include <new>

namespace FP = MB::FramePacing;
namespace FT = MB::FramePacing::Testing;
namespace PC = MB::FramePacing::Pacer;

TEST(Allocations, CountingWorks)
{
  const FT::AllocationCounter counter;
  // An explicit call: compilers may elide a new-expression pair like 'delete new int(1)' (clang does), but not this.
  void* const memory = ::operator new(sizeof(int));
  ::operator delete(memory);
  EXPECT_EQ(FT::AllocationCounter::Count(), 1u);
}

TEST(Allocations, PacingFramesDoesNotAllocate)
{
  // Made before counting: the pacer and the rule allocate their window here, once
  const PC::PacerSettings settings(PC::RefreshPeriod::FromRate(60));
  PC::FramePacer pacer(settings);
  PC::FramePacer fullWindowPacer(
    [&settings]
    {
      PC::PacerSettings copy = settings;
      copy.SetSlowDown(PC::SlowDownRule::FullWindow);
      return copy;
    }());
  PC::FramePacer feedbackPacer(
    [&settings]
    {
      PC::PacerSettings copy = settings;
      copy.SetUsePresentFeedback(true);
      return copy;
    }());
  PC::PacerRefreshClock clock(settings.Refresh(), settings.FrameWindowLength());

  int64_t written = 0;
  {
    const FT::AllocationCounter counter;
    int64_t now = FP::NanosecondTimeSpan::NanosecondsPerSecond;
    for (int64_t frame = 0; frame < 10'000; ++frame)
    {
      // Calm frames, then a busy stretch (every third frame over a refresh), the rule slowing down and speeding up again
      const bool busy = (frame / 600) % 2 == 1;
      const int64_t work = busy && frame % 3 == 0 ? 22'000'000 : 9'000'000;
      const PC::FrameSchedule schedule = pacer.BeginFrame(FP::NanosecondTickCount(now));
      written += pacer.EndFrame(FP::NanosecondTickCount(now + work), FP::NanosecondTimeSpan(work)).Nanoseconds();
      const PC::FrameSchedule other = fullWindowPacer.BeginFrame(FP::NanosecondTickCount(now));
      written += fullWindowPacer.EndFrame(FP::NanosecondTickCount(now + work)).Nanoseconds();
      written +=
        clock.Advance(FP::NanosecondTickCount(now), schedule.SwapInterval).Step.Nanoseconds() + (clock.DisplayTimeAfter(1).Nanoseconds() % 3);
      written += static_cast<int64_t>(pacer.FrameWindow().Frames) + (other.IntendedDisplayTime.Nanoseconds() % 7);
      // Present feedback three frames after each frame: on time, late, off the grid, not shown, and none at all
      const PC::FrameSchedule measured = feedbackPacer.BeginFrame(FP::NanosecondTickCount(now));
      written += feedbackPacer.EndFrame(FP::NanosecondTickCount(now + work)).Nanoseconds() + (measured.IntendedDisplayTime.Nanoseconds() % 5);
      if (measured.FrameId > 3u && frame % 7 != 0)
      {
        const FP::NanosecondTickCount shown(now + (frame % 11 == 0 ? 25'000'000 : 0) + (frame % 13 == 0 ? 7'000'000 : 0));
        feedbackPacer.AddPresentFeedback(frame % 17 == 0 ? PC::PresentFeedback::NotShown(measured.FrameId - 3u)
                                                         : PC::PresentFeedback::Shown(measured.FrameId - 3u, shown));
      }
      written += static_cast<int64_t>(feedbackPacer.FeedbackState().Used);
      if (frame % 2'500 == 1'250)
      {
        clock.Restart();
        pacer.Reset();
      }
      if (frame == 5'000)
      {
        pacer.SetRefreshPeriod(PC::RefreshPeriod::FromRate(60'000, 1'001));
        clock.SetRefreshPeriod(PC::RefreshPeriod::FromRate(60'000, 1'001));
      }
      // The same settings every frame, and now and then other ones that need no more room than the window has: a target frame rate
      // and back
      PC::PacerSettings current = pacer.Settings();
      if (frame % 1'000 == 500)
      {
        current.SetPreferredFrameRate(30);
      }
      else if (frame % 1'000 == 750)
      {
        current.SetPreferredFrameTime({});
      }
      pacer.SetSettings(current);
      now = std::max(schedule.IntendedDisplayTime.Nanoseconds(), now + work);
    }
    EXPECT_EQ(FT::AllocationCounter::Count(), 0u);
  }
  EXPECT_GT(written, 0) << "the calls must actually have produced output";
}

TEST(Allocations, SettingsThatNeedMoreRoomAllocateOnce)
{
  const PC::PacerSettings settings(PC::RefreshPeriod::FromRate(60));
  PC::FramePacer pacer(settings);
  PC::PacerSettings longer = settings;
  longer.SetFrameWindowLength(FP::NanosecondTimeSpan(10 * FP::NanosecondTimeSpan::NanosecondsPerSecond));
  {
    const FT::AllocationCounter counter;
    pacer.SetSettings(longer);
    EXPECT_GE(FT::AllocationCounter::Count(), 1u);
  }
  {
    // The room stays: the shorter window again, and the longer one again, allocate nothing
    const FT::AllocationCounter counter;
    pacer.SetSettings(settings);
    pacer.SetSettings(longer);
    pacer.SetSettings(longer);
    EXPECT_EQ(FT::AllocationCounter::Count(), 0u);
  }
}

TEST(Allocations, TheLowestPairsPacerPacesFramesWithoutAllocating)
{
  // Made before counting: the pacer's rule allocates its frame window here, once
  PC::PacerSettings settings(PC::RefreshPeriod::FromRate(240));
  settings.SetPreferredFrameRate(120);
  PC::TimerPeriodOnlyPacer pacer(settings);

  int64_t checked = 0;
  {
    const FT::AllocationCounter counter;
    int64_t now = 10 * FP::NanosecondTimeSpan::NanosecondsPerSecond;
    PC::PresentReport report;
    for (int32_t frame = 0; frame < 2'000; ++frame)
    {
      const PC::FrameStartPlan plan = pacer.PlanFrame(FP::NanosecondTickCount(now));
      now = plan.WaitsForStartTime() ? plan.StartTime.Nanoseconds() : now;
      const PC::FrameSchedule schedule = pacer.BeginFrame(FP::NanosecondTickCount(now));
      // Work that runs long now and then, so the grid loses steps and the rule changes the swap interval both ways
      now += (frame % 300) < 80 ? 12'000'000 : 2'000'000;
      const PC::PresentPlan present = pacer.EndFrame(FP::NanosecondTickCount(now));
      now = present.WaitsForPresentTime() ? present.PresentTime.Nanoseconds() : now;
      report.FrameId = present.FrameId;
      report.CallTime = FP::NanosecondTickCount(now);
      report.ReturnTime = FP::NanosecondTickCount(now + 60'000);
      pacer.AddPresent(report);
      // The GPU's work on the frame, given a frame later: beside the next frame's work for a stretch, after it for another
      pacer.AddGpuWork(PC::GpuWorkReport::Times(present.FrameId - 1u, FP::NanosecondTickCount(now - 6'000'000),
                                                FP::NanosecondTickCount(now - ((frame % 200) < 100 ? 3'500'000 : 500'000))));
      now += 60'000;
      checked += static_cast<int64_t>(schedule.SwapInterval);
    }
    // A pause, another refresh period and a reset are frames like any other
    static_cast<void>(pacer.BeginFrame(FP::NanosecondTickCount(now + (120 * FP::NanosecondTimeSpan::NanosecondsPerSecond))));
    pacer.SetRefreshPeriod(PC::RefreshPeriod::FromRate(120));
    static_cast<void>(pacer.BeginFrame(FP::NanosecondTickCount(now + (121 * FP::NanosecondTimeSpan::NanosecondsPerSecond))));
    pacer.Reset();
    static_cast<void>(pacer.BeginFrame(FP::NanosecondTickCount(now + (122 * FP::NanosecondTimeSpan::NanosecondsPerSecond))));
    EXPECT_EQ(FT::AllocationCounter::Count(), 0u);
  }
  EXPECT_GT(checked, 2'000);
}

TEST(Allocations, ThePacerOfATimerWithAWaitForAPresentPacesFramesWithoutAllocating)
{
  PC::PacerSettings settings(PC::RefreshPeriod::FromRate(240));
  settings.SetWaitingPresents(1);
  settings.SetAim(PC::PacerAim::LowLatency);
  PC::TimerWaitForPresentPacer pacer(settings);

  int64_t checked = 0;
  {
    const FT::AllocationCounter counter;
    int64_t now = 10 * FP::NanosecondTimeSpan::NanosecondsPerSecond;
    PC::PresentReport report;
    PC::PresentWaitReport waitReport;
    for (int32_t frame = 0; frame < 2'000; ++frame)
    {
      const PC::FrameStartPlan plan = pacer.PlanFrame(FP::NanosecondTickCount(now));
      if (plan.WaitsForPresent())
      {
        // A wait that holds the loop, one that returns at once, and now and then one that runs out
        waitReport.FrameId = plan.WaitForPresentFrameId;
        waitReport.BeginTime = FP::NanosecondTickCount(now);
        now += (frame % 3) == 0 ? 50'000 : 3'000'000;
        waitReport.EndTime = FP::NanosecondTickCount(now);
        waitReport.Shown = (frame % 97) != 0;
        pacer.AddPresentWait(waitReport);
      }
      now = plan.WaitsForStartTime() && plan.StartTime.Nanoseconds() > now ? plan.StartTime.Nanoseconds() : now;
      const PC::FrameSchedule schedule = pacer.BeginFrame(FP::NanosecondTickCount(now));
      now += (frame % 300) < 80 ? 12'000'000 : 2'000'000;
      const PC::PresentPlan present = pacer.EndFrame(FP::NanosecondTickCount(now));
      now = present.WaitsForPresentTime() ? present.PresentTime.Nanoseconds() : now;
      report.FrameId = present.FrameId;
      report.CallTime = FP::NanosecondTickCount(now);
      report.ReturnTime = FP::NanosecondTickCount(now + 60'000);
      report.Accepted = (frame % 211) != 0;
      pacer.AddPresent(report);
      // The GPU's work on the frame, given a frame later: beside the next frame's work for a stretch, after it for another
      pacer.AddGpuWork(PC::GpuWorkReport::Times(present.FrameId - 1u, FP::NanosecondTickCount(now - 6'000'000),
                                                FP::NanosecondTickCount(now - ((frame % 200) < 100 ? 3'500'000 : 500'000))));
      now += 60'000;
      checked += static_cast<int64_t>(schedule.SwapInterval);
    }
    pacer.SetRefreshPeriod(PC::RefreshPeriod::FromRate(120));
    static_cast<void>(pacer.BeginFrame(FP::NanosecondTickCount(now + (121 * FP::NanosecondTimeSpan::NanosecondsPerSecond))));
    pacer.Reset();
    static_cast<void>(pacer.BeginFrame(FP::NanosecondTickCount(now + (122 * FP::NanosecondTimeSpan::NanosecondsPerSecond))));
    EXPECT_EQ(FT::AllocationCounter::Count(), 0u);
  }
  EXPECT_GT(checked, 2'000);
}

TEST(Allocations, ThePacerOfVerticalBlankTimesPacesFramesWithoutAllocating)
{
  for (const PC::PacerAim aim : {PC::PacerAim::Smoothness, PC::PacerAim::LowLatency})
  {
    PC::PacerSettings settings(PC::RefreshPeriod::FromRate(240));
    settings.SetAim(aim);
    PC::VBlankPeriodOnlyPacer pacer(settings);

    int64_t checked = 0;
    {
      const FT::AllocationCounter counter;
      const int64_t period = settings.Refresh().ToNanosecondTimeSpan().Nanoseconds();
      int64_t now = 10 * FP::NanosecondTimeSpan::NanosecondsPerSecond;
      PC::PresentReport report;
      PC::VBlankReading reading;
      for (int32_t frame = 0; frame < 2'000; ++frame)
      {
        // A vertical blank a little before now, as a window system gives one
        reading.VBlankTime = FP::NanosecondTickCount(now - (now % period));
        reading.ReadTime = FP::NanosecondTickCount(now);
        pacer.AddVBlank(reading);
        const PC::FrameStartPlan plan = pacer.PlanFrame(FP::NanosecondTickCount(now));
        now = plan.WaitsForStartTime() ? plan.StartTime.Nanoseconds() : now;
        const PC::FrameSchedule schedule = pacer.BeginFrame(FP::NanosecondTickCount(now));
        // Work that runs long now and then, so frames miss their vertical blank and the rule changes the swap interval both ways
        now += (frame % 300) < 80 ? 12'000'000 : 2'000'000;
        const PC::PresentPlan present = pacer.EndFrame(FP::NanosecondTickCount(now));
        now = present.WaitsForPresentTime() ? present.PresentTime.Nanoseconds() : now;
        report.FrameId = present.FrameId;
        report.CallTime = FP::NanosecondTickCount(now);
        report.ReturnTime = FP::NanosecondTickCount(now + 60'000);
        report.Accepted = (frame % 211) != 0;
        pacer.AddPresent(report);
        pacer.AddGpuWork(
          PC::GpuWorkReport::Times(present.FrameId - 1u, FP::NanosecondTickCount(now - 6'000'000), FP::NanosecondTickCount(now - 500'000)));
        now += 60'000;
        checked += static_cast<int64_t>(schedule.SwapInterval);
      }
      // A pause, another refresh period and a reset are frames like any other
      static_cast<void>(pacer.BeginFrame(FP::NanosecondTickCount(now + (120 * FP::NanosecondTimeSpan::NanosecondsPerSecond))));
      pacer.SetRefreshPeriod(PC::RefreshPeriod::FromRate(120));
      static_cast<void>(pacer.BeginFrame(FP::NanosecondTickCount(now + (121 * FP::NanosecondTimeSpan::NanosecondsPerSecond))));
      pacer.Reset();
      static_cast<void>(pacer.BeginFrame(FP::NanosecondTickCount(now + (122 * FP::NanosecondTimeSpan::NanosecondsPerSecond))));
      EXPECT_EQ(FT::AllocationCounter::Count(), 0u);
    }
    EXPECT_GT(checked, 2'000);
  }
}

TEST(Allocations, ThePacerOfVerticalBlankTimesWithAWaitPacesFramesWithoutAllocating)
{
  for (const PC::PacerAim aim : {PC::PacerAim::Smoothness, PC::PacerAim::LowLatency})
  {
    PC::PacerSettings settings(PC::RefreshPeriod::FromRate(240));
    settings.SetAim(aim);
    PC::VBlankWaitForPresentPacer pacer(settings);

    int64_t checked = 0;
    {
      const FT::AllocationCounter counter;
      const int64_t period = settings.Refresh().ToNanosecondTimeSpan().Nanoseconds();
      int64_t now = 10 * FP::NanosecondTimeSpan::NanosecondsPerSecond;
      PC::PresentReport report;
      PC::PresentWaitReport waitReport;
      PC::VBlankReading reading;
      for (int32_t frame = 0; frame < 2'000; ++frame)
      {
        reading.VBlankTime = FP::NanosecondTickCount(now - (now % period));
        reading.ReadTime = FP::NanosecondTickCount(now);
        pacer.AddVBlank(reading);
        PC::FrameStartPlan plan = pacer.PlanFrame(FP::NanosecondTickCount(now));
        if (plan.WaitsForPresent())
        {
          // A wait that returns at once, one that holds the loop, and a stretch in which the waits run out
          const bool covered = (frame % 500) > 400;
          waitReport.FrameId = plan.WaitForPresentFrameId;
          waitReport.BeginTime = FP::NanosecondTickCount(now);
          now += covered ? plan.WaitForPresentTimeout.Nanoseconds() : ((frame % 3) == 0 ? 50'000 : 2'000'000);
          waitReport.EndTime = FP::NanosecondTickCount(now);
          waitReport.Shown = !covered;
          pacer.AddPresentWait(waitReport);
          plan = pacer.PlanFrame(FP::NanosecondTickCount(now));
        }
        now = plan.WaitsForStartTime() ? plan.StartTime.Nanoseconds() : now;
        const PC::FrameSchedule schedule = pacer.BeginFrame(FP::NanosecondTickCount(now));
        // Work that runs long now and then
        now += (frame % 300) < 80 ? 12'000'000 : 2'000'000;
        const PC::PresentPlan present = pacer.EndFrame(FP::NanosecondTickCount(now));
        now = present.WaitsForPresentTime() ? present.PresentTime.Nanoseconds() : now;
        report.FrameId = present.FrameId;
        report.CallTime = FP::NanosecondTickCount(now);
        report.ReturnTime = FP::NanosecondTickCount(now + 60'000);
        report.Accepted = (frame % 211) != 0;
        pacer.AddPresent(report);
        pacer.AddGpuWork(
          PC::GpuWorkReport::Times(present.FrameId - 1u, FP::NanosecondTickCount(now - 6'000'000), FP::NanosecondTickCount(now - 500'000)));
        now += 60'000;
        checked += static_cast<int64_t>(schedule.SwapInterval);
      }
      // A pause, another refresh period and a reset are frames like any other
      static_cast<void>(pacer.BeginFrame(FP::NanosecondTickCount(now + (120 * FP::NanosecondTimeSpan::NanosecondsPerSecond))));
      pacer.SetRefreshPeriod(PC::RefreshPeriod::FromRate(120));
      static_cast<void>(pacer.BeginFrame(FP::NanosecondTickCount(now + (121 * FP::NanosecondTimeSpan::NanosecondsPerSecond))));
      pacer.Reset();
      static_cast<void>(pacer.BeginFrame(FP::NanosecondTickCount(now + (122 * FP::NanosecondTimeSpan::NanosecondsPerSecond))));
      EXPECT_EQ(FT::AllocationCounter::Count(), 0u);
    }
    EXPECT_GE(checked, 2'000);
  }
}

TEST(Allocations, TheOnePacerPacesFramesAndChangesItsActiveCapabilitiesWithoutAllocating)
{
  using PC::PacerCapabilities;
  using PC::PacerCapability;
  for (const PC::PacerAim aim : {PC::PacerAim::Smoothness, PC::PacerAim::LowLatency})
  {
    PC::PacerSettings settings(PC::RefreshPeriod::FromRate(240));
    settings.SetAim(aim);
    const PacerCapabilities has(PacerCapability::VBlankTimes | PacerCapability::WaitForPresent | PacerCapability::PresentAfterDuration |
                                PacerCapability::PresentAtTime);
    PC::TierPacer pacer(settings, has);

    int64_t checked = 0;
    {
      const FT::AllocationCounter counter;
      const int64_t period = settings.Refresh().ToNanosecondTimeSpan().Nanoseconds();
      int64_t now = 10 * FP::NanosecondTimeSpan::NanosecondsPerSecond;
      PC::PresentReport report;
      PC::PresentWaitReport waitReport;
      PC::VBlankReading reading;
      for (int32_t frame = 0; frame < 2'000; ++frame)
      {
        // Every 50 frames another part of what the application has is active: all eight tiers, with either timed present and
        // with both, in turn, the change made between two frames or while one is open
        if ((frame % 50) == 0)
        {
          const int32_t turn = (frame / 50) % 16;
          pacer.SetActiveCapabilities(PacerCapabilities(((turn & 1) != 0 ? PacerCapability::VBlankTimes : PacerCapability::NoCapabilities) |
                                                        ((turn & 2) != 0 ? PacerCapability::WaitForPresent : PacerCapability::NoCapabilities) |
                                                        ((turn & 4) != 0 ? PacerCapability::PresentAtTime : PacerCapability::NoCapabilities) |
                                                        ((turn & 8) != 0 ? PacerCapability::PresentAfterDuration : PacerCapability::NoCapabilities)));
        }
        reading.VBlankTime = FP::NanosecondTickCount(now - (now % period));
        reading.ReadTime = FP::NanosecondTickCount(now);
        pacer.AddVBlank(reading);
        PC::FrameStartPlan plan = pacer.PlanFrame(FP::NanosecondTickCount(now));
        if (plan.WaitsForPresent())
        {
          waitReport.FrameId = plan.WaitForPresentFrameId;
          waitReport.BeginTime = FP::NanosecondTickCount(now);
          now += (frame % 3) == 0 ? 50'000 : 2'000'000;
          waitReport.EndTime = FP::NanosecondTickCount(now);
          waitReport.Shown = (frame % 97) != 0;
          pacer.AddPresentWait(waitReport);
          plan = pacer.PlanFrame(FP::NanosecondTickCount(now));
        }
        now = plan.WaitsForStartTime() ? plan.StartTime.Nanoseconds() : now;
        const PC::FrameSchedule schedule = pacer.BeginFrame(FP::NanosecondTickCount(now));
        if ((frame % 50) == 25)
        {
          pacer.SetActiveCapabilities(has);
        }
        now += (frame % 300) < 80 ? 12'000'000 : 2'000'000;
        const PC::PresentPlan present = pacer.EndFrame(FP::NanosecondTickCount(now));
        now = present.WaitsForPresentTime() ? present.PresentTime.Nanoseconds() : now;
        report.FrameId = present.FrameId;
        report.CallTime = FP::NanosecondTickCount(now);
        report.ReturnTime = FP::NanosecondTickCount(now + 60'000);
        report.Accepted = (frame % 211) != 0;
        pacer.AddPresent(report);
        pacer.AddGpuWork(
          PC::GpuWorkReport::Times(present.FrameId - 1u, FP::NanosecondTickCount(now - 6'000'000), FP::NanosecondTickCount(now - 500'000)));
        now += 60'000;
        checked +=
          static_cast<int64_t>(schedule.SwapInterval) + static_cast<int64_t>(pacer.WorkingTier()) + static_cast<int64_t>(pacer.ActiveRating().Tier);
      }
      pacer.SetCapabilities(PacerCapabilities(PacerCapability::WaitForPresent));
      pacer.SetRefreshPeriod(PC::RefreshPeriod::FromRate(120));
      static_cast<void>(pacer.BeginFrame(FP::NanosecondTickCount(now + (121 * FP::NanosecondTimeSpan::NanosecondsPerSecond))));
      pacer.Reset();
      static_cast<void>(pacer.BeginFrame(FP::NanosecondTickCount(now + (122 * FP::NanosecondTimeSpan::NanosecondsPerSecond))));
      EXPECT_EQ(FT::AllocationCounter::Count(), 0u);
    }
    EXPECT_GE(checked, 2'000);
  }
}
