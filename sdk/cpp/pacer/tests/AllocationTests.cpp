// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The pacer runs every frame, so pacing must never allocate after the pacer is made. This test binary links the counting global
// operator new/delete (mb_framepacing_test_support) and checks that every per-frame call stays at zero allocations; only SetSettings
// with settings that need a larger frame window may allocate.
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/clock/PacerRefreshClock.hpp>
#include <mb/framepacing/pacer/frame/FrameSchedule.hpp>
#include <mb/framepacing/pacer/rule/SlowDownRule.hpp>
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
