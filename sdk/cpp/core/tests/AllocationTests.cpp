// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The time types are used every frame, so they must never allocate. This test binary replaces the global operator new/delete with
// counting versions and checks that every operation that does not throw stays at zero allocations.
#include <mb/framepacing/core/time/TickCount32.hpp>
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <gtest/gtest.h>
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

// Counting replacements of the global allocation functions, as the marker module's allocation test has them: every non-aligned new
// allocates with malloc because every non-aligned delete below frees with free (a sanitizer reports any mismatched pair); the aligned
// variants keep their default, matching pair. No top-level const on the parameters: clang 22 then treats the sized operator delete as
// "non-usual" and rejects libstdc++'s __builtin_operator_delete calls.
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

TEST(Allocations, CountingWorks)
{
  const AllocationCounter counter;
  // An explicit call: compilers may elide a new-expression pair like 'delete new int(1)' (clang does), but not this.
  void* const memory = ::operator new(sizeof(int));
  ::operator delete(memory);
  EXPECT_EQ(AllocationCounter::Count(), 1u);
}

TEST(Allocations, TheTimeTypesDoNotAllocate)
{
  int64_t sum = 0;
  {
    const AllocationCounter counter;
    FP::TickCount64 now(123'456'789);
    FP::TickCount32 now32 = FP::TickCount32::FromTickCount64(now);
    for (int64_t frame = 0; frame < 1000; ++frame)
    {
      const FP::TimeSpan step = FP::TimeSpan::FromMicroseconds(16'667) + FP::TimeSpan(frame % 3);
      const FP::TickCount64 previous = now;
      const FP::TickCount32 previous32 = now32;
      now += step;
      now32 += step;
      const FP::TimeSpan elapsed = now - previous;
      const FP::TimeSpan elapsed32 = now32 - previous32;
      const FP::TimeSpan32 busy = FP::TimeSpan32::FromTimeSpan((elapsed * 0.5).Duration());
      sum += elapsed.Ticks() + elapsed32.Ticks() + busy.ToTimeSpan().Ticks() + (now32 > previous32 ? 1 : 0) + (now > previous ? 1 : 0);
      sum += FP::TimeSpan::FromSeconds(1.0 / 60).Ticks() + FP::TimeSpan(0, 0, 0, 1, 2, 3).Ticks() + (step / 2.0).Ticks();
      sum += static_cast<int64_t>(step.TotalMilliseconds() + now.TotalSeconds() + now32.TotalMicroseconds() + (step / elapsed));
      sum += now.Days() + now.Milliseconds() + now32.Milliseconds() + step.Microseconds() + step.Nanoseconds();
      sum += FP::TickCount64::FromMilliseconds(frame).Ticks() + FP::TickCount32::FromMilliseconds(static_cast<int32_t>(frame)).Ticks();
    }
    EXPECT_EQ(AllocationCounter::Count(), 0u);
  }
  EXPECT_GT(sum, 0) << "the calls must actually have produced output";
}
