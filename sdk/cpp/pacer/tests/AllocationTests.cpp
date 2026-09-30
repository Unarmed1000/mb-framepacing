// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The pacer runs every frame, so it must never allocate after it is made. This test binary replaces the global operator new/delete with
// counting versions and checks that every per-frame call stays at zero allocations.
#include <mb/framepacing/Pacer.hpp>
#include <gtest/gtest.h>
#include <algorithm>
#include <atomic>
#include <cstdint>
#include <cstdlib>
#include <new>

namespace
{
  std::atomic<bool> g_countAllocations{false};
  std::atomic<std::size_t> g_allocationCount{0};

  //! Counts the allocations made while it is alive (the tests run on one thread).
  class AllocationCounter
  {
  public:
    AllocationCounter() noexcept
    {
      g_allocationCount = 0;
      g_countAllocations = true;
    }

    ~AllocationCounter()
    {
      g_countAllocations = false;
    }

    AllocationCounter(const AllocationCounter&) = delete;
    AllocationCounter& operator=(const AllocationCounter&) = delete;
    AllocationCounter(AllocationCounter&&) = delete;
    AllocationCounter& operator=(AllocationCounter&&) = delete;

    static std::size_t Count() noexcept
    {
      return g_allocationCount;
    }
  };
}

// Counting replacements of the global allocation functions, as in the marker module's AllocationTests.cpp (see its notes: every
// non-aligned new allocates with malloc because every non-aligned delete frees with free; no top-level const on the parameters).
// NOLINTBEGIN(cppcoreguidelines-no-malloc,cppcoreguidelines-owning-memory,misc-new-delete-overloads)
namespace
{
  void* CountedMalloc(const std::size_t size) noexcept
  {
    if (g_countAllocations)
    {
      ++g_allocationCount;
    }
    return std::malloc(size == 0 ? 1 : size);
  }
}

void* operator new(std::size_t size)
{
  if (void* const memory = CountedMalloc(size))
  {
    return memory;
  }
  throw std::bad_alloc();
}

void* operator new[](std::size_t size)
{
  return operator new(size);
}

void* operator new(std::size_t size, const std::nothrow_t& /*tag*/) noexcept
{
  return CountedMalloc(size);
}

void* operator new[](std::size_t size, const std::nothrow_t& /*tag*/) noexcept
{
  return CountedMalloc(size);
}

void operator delete(void* memory, const std::nothrow_t& /*tag*/) noexcept
{
  std::free(memory);
}

void operator delete[](void* memory, const std::nothrow_t& /*tag*/) noexcept
{
  std::free(memory);
}

void operator delete(void* memory) noexcept
{
  std::free(memory);
}

void operator delete[](void* memory) noexcept
{
  std::free(memory);
}

void operator delete(void* memory, std::size_t /*size*/) noexcept
{
  std::free(memory);
}

void operator delete[](void* memory, std::size_t /*size*/) noexcept
{
  std::free(memory);
}
// NOLINTEND(cppcoreguidelines-no-malloc,cppcoreguidelines-owning-memory,misc-new-delete-overloads)

namespace FP = MB::FramePacing;
namespace PC = MB::FramePacing::Pacer;

TEST(Allocations, CountingWorks)
{
  const AllocationCounter counter;
  // An explicit call: compilers may elide a new-expression pair like 'delete new int(1)' (clang does), but not this.
  void* const memory = ::operator new(sizeof(int));
  ::operator delete(memory);
  EXPECT_EQ(AllocationCounter::Count(), 1u);
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
    const AllocationCounter counter;
    int64_t now = FP::TicksPerSecond;
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
    EXPECT_EQ(AllocationCounter::Count(), 0u);
  }
  EXPECT_GT(written, 0) << "the calls must actually have produced output";
}
