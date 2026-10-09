// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The pacer runs every frame, so pacing must never allocate after the pacer is made. This test binary links the counting global
// operator new/delete (mb_framepacing_test_support) and checks that every per-frame call stays at zero allocations, with present
// feedback too; only SetSettings with settings that need a larger frame window may allocate.
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/pacer/PacerAim.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/capability/PacerCapabilities.hpp>
#include <mb/framepacing/pacer/capability/PacerCapability.hpp>
#include <mb/framepacing/pacer/display/DisplayReport.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/frame/FrameStartPlan.hpp>
#include <mb/framepacing/pacer/frame/GpuWaitReport.hpp>
#include <mb/framepacing/pacer/frame/GpuWorkReport.hpp>
#include <mb/framepacing/pacer/frame/PresentPlan.hpp>
#include <mb/framepacing/pacer/frame/PresentReport.hpp>
#include <mb/framepacing/pacer/frame/PresentWaitReport.hpp>
#include <mb/framepacing/pacer/frame/VBlankReading.hpp>
#include <mb/framepacing/pacer/rule/SlowDownRule.hpp>
#include <mb/framepacing/pacer/tier/TierPacer.hpp>
#include <mb/framepacing/testing/AllocationCounter.hpp>
#include <gtest/gtest.h>
#include <algorithm>
#include <cstdint>
#include <new>
#include "TimerPeriodOnlyPacer.hpp"
#include "TimerWaitForPresentPacer.hpp"
#include "VBlankPeriodOnlyPacer.hpp"
#include "VBlankWaitForPresentPacer.hpp"

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
                                PacerCapability::PresentAtTime | PacerCapability::DisplayTimes | PacerCapability::WaitForGpuWork);
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
        // with both, with the wait for the GPU's work and without it, in turn, the change made between two frames or while
        // one is open
        if ((frame % 50) == 0)
        {
          const int32_t turn = (frame / 50) % 32;
          pacer.SetActiveCapabilities(PacerCapabilities(((turn & 1) != 0 ? PacerCapability::VBlankTimes : PacerCapability::NoCapabilities) |
                                                        ((turn & 2) != 0 ? PacerCapability::WaitForPresent : PacerCapability::NoCapabilities) |
                                                        ((turn & 4) != 0 ? PacerCapability::PresentAtTime : PacerCapability::NoCapabilities) |
                                                        ((turn & 8) != 0 ? PacerCapability::PresentAfterDuration : PacerCapability::NoCapabilities) |
                                                        ((turn & 16) != 0 ? PacerCapability::WaitForGpuWork : PacerCapability::NoCapabilities)));
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
        if (plan.WaitsForGpuWork())
        {
          PC::GpuWaitReport gpuWait;
          gpuWait.FrameId = plan.WaitForGpuWorkFrameId;
          gpuWait.BeginTime = FP::NanosecondTickCount(now);
          now += (frame % 5) == 0 ? 3'000'000 : 20'000;
          gpuWait.EndTime = FP::NanosecondTickCount(now);
          gpuWait.Done = (frame % 89) != 0;
          pacer.AddGpuWait(gpuWait);
          plan = pacer.PlanFrame(FP::NanosecondTickCount(now));
          checked += 1;
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
        // The display time of the frame three back, as a platform reports it, and with it what the pacer counted
        PC::DisplayReport displayReport;
        displayReport.FrameId = present.FrameId > 3u ? present.FrameId - 3u : 0u;
        displayReport.DisplayTime = FP::NanosecondTickCount(now - (now % period) - period);
        displayReport.Shown = (frame % 53) != 0;
        pacer.AddDisplayReport(displayReport);
        checked += static_cast<int64_t>(pacer.DisplayErrors().RecentJudgedFrames);
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
