// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The pacer runs every frame, so pacing must never allocate after the pacer is made. This test binary links the counting global
// operator new/delete (mb_framepacing_test_support) and checks that every per-frame call stays at zero allocations, with present
// feedback too; only SetSettings with settings that need a larger frame window may allocate.
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/clock/PacerRefreshClock.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/frame/FrameStartPlan.hpp>
#include <mb/framepacing/pacer/frame/GpuWorkReport.hpp>
#include <mb/framepacing/pacer/frame/PresentFeedback.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/frame/PresentWaitReport.hpp>
#include <mb/framepacing/pacer/rule/SlowDownRule.hpp>
#include <mb/framepacing/pacer/tier/TimerPeriodOnlyPacer.hpp>
#include <mb/framepacing/pacer/tier/TimerWaitForPresentPacer.hpp>
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
    int64_t now = FP::TimeSpan::TicksPerSecond;
    for (int64_t frame = 0; frame < 10'000; ++frame)
    {
      // Calm frames, then a busy stretch (every third frame over a refresh), the rule slowing down and speeding up again
      const bool busy = (frame / 600) % 2 == 1;
      const int64_t work = busy && frame % 3 == 0 ? 220'000 : 90'000;
      const PC::FrameSchedule schedule = pacer.BeginFrame(FP::TickCount64(now));
      written += static_cast<int64_t>(pacer.EndFrame(FP::TickCount64(now + work), FP::TimeSpan(work)).Ticks());
      const PC::FrameSchedule other = fullWindowPacer.BeginFrame(FP::TickCount64(now));
      written += static_cast<int64_t>(fullWindowPacer.EndFrame(FP::TickCount64(now + work)).Ticks());
      written += clock.Advance(FP::TickCount64(now), schedule.SwapInterval).Step.Ticks() + (clock.DisplayTimeAfter(1).Ticks() % 3);
      written += static_cast<int64_t>(pacer.FrameWindow().Frames) + (other.IntendedDisplayTime.Ticks() % 7);
      // Present feedback three frames after each frame: on time, late, off the grid, not shown, and none at all
      const PC::FrameSchedule measured = feedbackPacer.BeginFrame(FP::TickCount64(now));
      written += static_cast<int64_t>(feedbackPacer.EndFrame(FP::TickCount64(now + work)).Ticks()) + (measured.IntendedDisplayTime.Ticks() % 5);
      if (measured.FrameId > 3u && frame % 7 != 0)
      {
        const FP::TickCount64 shown(now + (frame % 11 == 0 ? 250'000 : 0) + (frame % 13 == 0 ? 70'000 : 0));
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
      now = std::max(schedule.IntendedDisplayTime.Ticks(), now + work);
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
  longer.SetFrameWindowLength(FP::TimeSpan(10 * FP::TimeSpan::TicksPerSecond));
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
    int64_t now = 10 * FP::TimeSpan::TicksPerSecond;
    PC::PresentReport report;
    for (int32_t frame = 0; frame < 2'000; ++frame)
    {
      const PC::FrameStartPlan plan = pacer.PlanFrame(FP::TickCount64(now));
      now = plan.WaitsForStartTime() ? plan.StartTime.Ticks() : now;
      const PC::FrameSchedule schedule = pacer.BeginFrame(FP::TickCount64(now));
      // Work that runs long now and then, so the grid loses steps and the rule changes the swap interval both ways
      now += (frame % 300) < 80 ? 120'000 : 20'000;
      const PC::PresentPlan present = pacer.EndFrame(FP::TickCount64(now));
      now = present.WaitsForPresentTime() ? present.PresentTime.Ticks() : now;
      report.FrameId = present.FrameId;
      report.CallTime = FP::TickCount64(now);
      report.ReturnTime = FP::TickCount64(now + 600);
      pacer.AddPresent(report);
      // The GPU's work on the frame, given a frame later: beside the next frame's work for a stretch, after it for another
      pacer.AddGpuWork(
        PC::GpuWorkReport::Times(present.FrameId - 1u, FP::TickCount64(now - 60'000), FP::TickCount64(now - ((frame % 200) < 100 ? 35'000 : 5'000))));
      now += 600;
      checked += static_cast<int64_t>(schedule.SwapInterval);
    }
    // A pause, another refresh period and a reset are frames like any other
    static_cast<void>(pacer.BeginFrame(FP::TickCount64(now + (120 * FP::TimeSpan::TicksPerSecond))));
    pacer.SetRefreshPeriod(PC::RefreshPeriod::FromRate(120));
    static_cast<void>(pacer.BeginFrame(FP::TickCount64(now + (121 * FP::TimeSpan::TicksPerSecond))));
    pacer.Reset();
    static_cast<void>(pacer.BeginFrame(FP::TickCount64(now + (122 * FP::TimeSpan::TicksPerSecond))));
    EXPECT_EQ(FT::AllocationCounter::Count(), 0u);
  }
  EXPECT_GT(checked, 2'000);
}

TEST(Allocations, ThePacerOfATimerWithAWaitForAPresentPacesFramesWithoutAllocating)
{
  PC::PacerSettings settings(PC::RefreshPeriod::FromRate(240));
  settings.SetWaitingPresents(1);
  PC::TimerWaitForPresentPacer pacer(settings);

  int64_t checked = 0;
  {
    const FT::AllocationCounter counter;
    int64_t now = 10 * FP::TimeSpan::TicksPerSecond;
    PC::PresentReport report;
    PC::PresentWaitReport waitReport;
    for (int32_t frame = 0; frame < 2'000; ++frame)
    {
      const PC::FrameStartPlan plan = pacer.PlanFrame(FP::TickCount64(now));
      if (plan.WaitsForPresent())
      {
        // A wait that holds the loop, one that returns at once, and now and then one that runs out
        waitReport.FrameId = plan.WaitForPresentFrameId;
        waitReport.BeginTime = FP::TickCount64(now);
        now += (frame % 3) == 0 ? 500 : 30'000;
        waitReport.EndTime = FP::TickCount64(now);
        waitReport.Shown = (frame % 97) != 0;
        pacer.AddPresentWait(waitReport);
      }
      now = plan.WaitsForStartTime() && plan.StartTime.Ticks() > now ? plan.StartTime.Ticks() : now;
      const PC::FrameSchedule schedule = pacer.BeginFrame(FP::TickCount64(now));
      now += (frame % 300) < 80 ? 120'000 : 20'000;
      const PC::PresentPlan present = pacer.EndFrame(FP::TickCount64(now));
      now = present.WaitsForPresentTime() ? present.PresentTime.Ticks() : now;
      report.FrameId = present.FrameId;
      report.CallTime = FP::TickCount64(now);
      report.ReturnTime = FP::TickCount64(now + 600);
      report.Accepted = (frame % 211) != 0;
      pacer.AddPresent(report);
      // The GPU's work on the frame, given a frame later: beside the next frame's work for a stretch, after it for another
      pacer.AddGpuWork(
        PC::GpuWorkReport::Times(present.FrameId - 1u, FP::TickCount64(now - 60'000), FP::TickCount64(now - ((frame % 200) < 100 ? 35'000 : 5'000))));
      now += 600;
      checked += static_cast<int64_t>(schedule.SwapInterval);
    }
    pacer.SetRefreshPeriod(PC::RefreshPeriod::FromRate(120));
    static_cast<void>(pacer.BeginFrame(FP::TickCount64(now + (121 * FP::TimeSpan::TicksPerSecond))));
    pacer.Reset();
    static_cast<void>(pacer.BeginFrame(FP::TickCount64(now + (122 * FP::TimeSpan::TicksPerSecond))));
    EXPECT_EQ(FT::AllocationCounter::Count(), 0u);
  }
  EXPECT_GT(checked, 2'000);
}
