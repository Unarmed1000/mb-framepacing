// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The pacer runs every frame, so it must never allocate after it is made. This test binary links the counting global operator new/delete
// (mb_framepacing_test_support) and checks that every per-frame call stays at zero allocations.
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/pacer/FramePacer.hpp>
#include <mb/framepacing/pacer/PacerSettings.hpp>
#include <mb/framepacing/pacer/RefreshPeriod.hpp>
#include <mb/framepacing/pacer/animation/AnimationClock.hpp>
#include <mb/framepacing/pacer/frame/FrameInput.hpp>
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
  PC::AnimationClock clock(settings.Refresh(), 0, 30);
  PC::AnimationClock measuredClock(settings.Refresh());

  int64_t written = 0;
  {
    const FT::AllocationCounter counter;
    int64_t now = FP::TimeSpan::TicksPerSecond;
    for (int64_t frame = 0; frame < 10'000; ++frame)
    {
      // Calm frames, then a busy stretch (every third frame over a refresh), the rule slowing down and speeding up again
      const bool busy = (frame / 600) % 2 == 1;
      const int64_t work = busy && frame % 3 == 0 ? 220'000 : 90'000;
      PC::FrameInput input{now};
      if (frame % 97 == 0)
      {
        input.VsyncTicks = now;
      }
      const PC::FrameSchedule schedule = pacer.BeginFrame(input);
      written += static_cast<int64_t>(pacer.EndFrame({now + work, work}));
      const PC::FrameSchedule other = fullWindowPacer.BeginFrame({now, 0, frame % 5 == 0 ? schedule.EarliestPresentTicks : 0, 0});
      written += static_cast<int64_t>(fullWindowPacer.EndFrame({now + work}));
      written += clock.Advance(schedule).StepTicks + measuredClock.AdvanceMeasured(now, 1).StepTicks;
      written += static_cast<int64_t>(pacer.Window().Frames) + other.IntendedDisplayTicks % 7;
      if (frame % 2'500 == 1'250)
      {
        clock.Pause();
        pacer.Reset();
      }
      if (frame % 2'500 == 1'260)
      {
        clock.Resume();
      }
      if (frame == 5'000)
      {
        pacer.SetRefreshPeriod(PC::RefreshPeriod::FromRate(60'000, 1'001));
      }
      now = std::max(schedule.IntendedDisplayTicks, now + work);
    }
    EXPECT_EQ(FT::AllocationCounter::Count(), 0u);
  }
  EXPECT_GT(written, 0) << "the calls must actually have produced output";
}
