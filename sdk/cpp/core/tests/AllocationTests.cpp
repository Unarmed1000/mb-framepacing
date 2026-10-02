// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The core's types are used every frame, so they must never allocate. This test binary links the counting global operator new/delete
// (mb_framepacing_test_support) and checks that the operations a frame uses stay at zero allocations: the time types' arithmetic,
// conversions and components, the little-endian reads and writes, Point and Rectangle.
#include <mb/framepacing/core/ByteSpanUtil.hpp>
#include <mb/framepacing/core/Point.hpp>
#include <mb/framepacing/core/Rectangle.hpp>
#include <mb/framepacing/core/time/TickCount32.hpp>
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <mb/framepacing/testing/AllocationCounter.hpp>
#include <gtest/gtest.h>
#include <array>
#include <cstdint>
#include <new>

namespace FP = MB::FramePacing;
namespace FT = MB::FramePacing::Testing;

TEST(Allocations, CountingWorks)
{
  const FT::AllocationCounter counter;
  // An explicit call: compilers may elide a new-expression pair like 'delete new int(1)' (clang does), but not this.
  void* const memory = ::operator new(sizeof(int));
  ::operator delete(memory);
  EXPECT_EQ(FT::AllocationCounter::Count(), 1u);
}

TEST(Allocations, TheCoreTypesDoNotAllocate)
{
  int64_t sum = 0;
  {
    const FT::AllocationCounter counter;
    FP::TickCount64 now(123'456'789);
    FP::TickCount32 now32 = FP::TickCount32::FromTickCount64(now);
    std::array<uint8_t, 16> bytes{};
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
      // What an application converts its clock with, and the other core types
      sum += FP::TickCount64::FromCounter(frame * 300, 3'000'000).Ticks() + FP::TickCount64::FromNanoseconds(frame * 150).Ticks();
      sum += step.Negate().Negate().Ticks();
      FP::ByteSpanUtil::WriteLE(bytes, 1, static_cast<uint32_t>(frame));
      FP::ByteSpanUtil::WriteLE(bytes, 8, 0.5);
      sum += FP::ByteSpanUtil::ReadLE<uint32_t>(bytes, 1) + static_cast<int64_t>(FP::ByteSpanUtil::ReadLE<double>(bytes, 8) * 2.0);
      const FP::Rectangle rect = FP::Rectangle::FromLeftTopRightBottom(1, 2, static_cast<int32_t>(frame) + 3, 40);
      const FP::Point point{rect.Right(), rect.Bottom()};
      sum += point.X + point.Y + (rect.Contains(1, 2) ? 1 : 0) + (rect == FP::Rectangle(1, 2, rect.Width(), 38) ? 1 : 0);
    }
    EXPECT_EQ(FT::AllocationCounter::Count(), 0u);
  }
  EXPECT_GT(sum, 0) << "the calls must actually have produced output";
}
