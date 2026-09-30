// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// TickCount32: a point on a 32-bit clock of 100 ns ticks that wraps every 429.4967296 s. Counts less than 2^31 ticks apart compare and
// subtract correctly across the wrap.
#include <mb/framepacing/core/time/TickCount32.hpp>
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <gtest/gtest.h>
#include <cstdint>
#include <limits>
#include <stdexcept>
#include <type_traits>

namespace FP = MB::FramePacing;

namespace
{
  constexpr uint32_t MaxCount = std::numeric_limits<uint32_t>::max();
  constexpr int64_t HalfRange = int64_t{1} << 31;
}

TEST(TickCount32, HasTheUnitsOfATimeSpanAndTheRangeOfAnInt32)
{
  static_assert(FP::TickCount32::NanosecondsPerTick == 100 && FP::TickCount32::TicksPerMicrosecond == 10);
  static_assert(FP::TickCount32::TicksPerMillisecond == 10'000 && FP::TickCount32::TicksPerSecond == 10'000'000);
  static_assert(FP::TickCount32::TicksPerMinute == 600'000'000 && FP::TickCount32::TicksPerHour == 36'000'000'000);
  static_assert(FP::TickCount32::TicksPerDay == 864'000'000'000);
  static_assert(FP::TickCount32::MinDays == 0 && FP::TickCount32::MaxDays == 0);
  static_assert(FP::TickCount32::MinHours == 0 && FP::TickCount32::MaxHours == 0);
  static_assert(FP::TickCount32::MinMinutes == -3 && FP::TickCount32::MaxMinutes == 3);
  static_assert(FP::TickCount32::MinSeconds == -214 && FP::TickCount32::MaxSeconds == 214);
  static_assert(FP::TickCount32::MinMilliseconds == -214'748 && FP::TickCount32::MaxMilliseconds == 214'748);
  static_assert(FP::TickCount32::MinMicroseconds == -214'748'364 && FP::TickCount32::MaxMicroseconds == 214'748'364);
  static_assert(std::is_trivially_copyable_v<FP::TickCount32> && std::is_standard_layout_v<FP::TickCount32> && sizeof(FP::TickCount32) == 4);
}

TEST(TickCount32, IsAnUnsignedCountWithASignedView)
{
  EXPECT_EQ(FP::TickCount32().UnsignedTicks(), 0u);
  EXPECT_EQ(FP::TickCount32(MaxCount).Ticks(), -1);
  EXPECT_EQ(FP::TickCount32(-2).UnsignedTicks(), MaxCount - 1u);
  EXPECT_EQ(FP::TickCount32::FromTicks(-3).UnsignedTicks(), MaxCount - 2u);
  EXPECT_EQ(FP::TickCount32::FromUnsignedTicks(7u).Ticks(), 7);
  // A span or a 64-bit count gives its low 32 bits: the same point on the 32-bit clock
  EXPECT_EQ(FP::TickCount32(FP::TimeSpan((int64_t{1} << 32) + 5)).UnsignedTicks(), 5u);
  EXPECT_EQ(FP::TickCount32(FP::TimeSpan(-1)).UnsignedTicks(), MaxCount);
  EXPECT_EQ(FP::TickCount32::FromTickCount64(FP::TickCount64((int64_t{3} << 32) + 9)).UnsignedTicks(), 9u);
  EXPECT_EQ(FP::TickCount32(-5).ToTimeSpan(), FP::TimeSpan(-5));
}

TEST(TickCount32, TheFactoriesThrowOutsideTheirRange)
{
  EXPECT_EQ(FP::TickCount32::FromDays(0).Ticks(), 0);
  EXPECT_THROW(static_cast<void>(FP::TickCount32::FromDays(1)), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::TickCount32::FromDays(-1)), std::overflow_error);
  EXPECT_EQ(FP::TickCount32::FromHours(0).Ticks(), 0);
  EXPECT_THROW(static_cast<void>(FP::TickCount32::FromHours(1)), std::overflow_error);
  EXPECT_EQ(FP::TickCount32::FromMinutes(3).Ticks(), 1'800'000'000);
  EXPECT_EQ(FP::TickCount32::FromMinutes(-3).Ticks(), -1'800'000'000);
  EXPECT_THROW(static_cast<void>(FP::TickCount32::FromMinutes(4)), std::overflow_error);
  EXPECT_EQ(FP::TickCount32::FromSeconds(214).Ticks(), 2'140'000'000);
  EXPECT_THROW(static_cast<void>(FP::TickCount32::FromSeconds(-215)), std::overflow_error);
  EXPECT_EQ(FP::TickCount32::FromMilliseconds(-214'748).Ticks(), -2'147'480'000);
  EXPECT_THROW(static_cast<void>(FP::TickCount32::FromMilliseconds(214'749)), std::overflow_error);
  EXPECT_EQ(FP::TickCount32::FromMicroseconds(214'748'364).Ticks(), 2'147'483'640);
  EXPECT_THROW(static_cast<void>(FP::TickCount32::FromMicroseconds(-214'748'365)), std::overflow_error);
  // Nanoseconds stay on the tick they are in; every int32_t of them fits
  EXPECT_EQ(FP::TickCount32::FromNanoseconds(150).Ticks(), 1);
  EXPECT_EQ(FP::TickCount32::FromNanoseconds(-1).Ticks(), -1);
  EXPECT_EQ(FP::TickCount32::FromNanoseconds(std::numeric_limits<int32_t>::min()).Ticks(), -21'474'837);
}

TEST(TickCount32, ComponentsAndTotalsReadTheSignedView)
{
  const FP::TickCount32 count(2'123'456'789);
  EXPECT_EQ(count.Days(), 0);
  EXPECT_EQ(count.Hours(), 0);
  EXPECT_EQ(count.Minutes(), 3);
  EXPECT_EQ(count.Seconds(), 32);
  EXPECT_EQ(count.Milliseconds(), 345);
  EXPECT_EQ(count.Microseconds(), 678);
  EXPECT_EQ(count.TotalNanoseconds(), 212345678900.0);
  EXPECT_EQ(count.TotalMicroseconds(), 212345678.9);
  EXPECT_EQ(count.TotalMilliseconds(), 212345.6789);
  EXPECT_EQ(count.TotalSeconds(), 212.3456789);
  EXPECT_EQ(count.TotalMinutes(), 2'123'456'789.0 / 600'000'000.0);
  EXPECT_EQ(count.TotalHours(), 2'123'456'789.0 / 36'000'000'000.0);
  EXPECT_EQ(count.TotalDays(), 2'123'456'789.0 / 864'000'000'000.0);

  // Past 2^31 ticks the signed view is negative
  const FP::TickCount32 wrapped(MaxCount - 99u);
  EXPECT_EQ(wrapped.Ticks(), -100);
  EXPECT_EQ(wrapped.Microseconds(), -10);
  EXPECT_EQ(wrapped.TotalMicroseconds(), -10.0);
}

TEST(TickCount32, ArithmeticWrapsAround)
{
  EXPECT_EQ(FP::TickCount32(MaxCount) + FP::TimeSpan(1), FP::TickCount32(0u));
  EXPECT_EQ(FP::TickCount32(0u) - FP::TimeSpan(1), FP::TickCount32(MaxCount));
  // A span is added modulo 2^32
  EXPECT_EQ(FP::TickCount32(5u) + FP::TimeSpan((int64_t{7} << 32) + 3), FP::TickCount32(8u));
  EXPECT_EQ(FP::TickCount32(5u) + FP::TimeSpan(-6), FP::TickCount32(MaxCount));

  FP::TickCount32 count(MaxCount - 1u);
  count += FP::TimeSpan(4);
  EXPECT_EQ(count.UnsignedTicks(), 2u);
  count -= FP::TimeSpan(4);
  EXPECT_EQ(count.UnsignedTicks(), MaxCount - 1u);
}

TEST(TickCount32, TwoCountsSubtractTheShorterWayRoundAcrossTheWrap)
{
  const FP::TickCount32 before(MaxCount - 1u);
  const FP::TickCount32 after(2u);
  EXPECT_EQ(after - before, FP::TimeSpan(4));
  EXPECT_EQ(before - after, FP::TimeSpan(-4));
  // The farthest apart that still subtracts correctly: 2^31 - 1 ticks (214.7483647 s)
  const FP::TickCount32 far = before + FP::TimeSpan(HalfRange - 1);
  EXPECT_EQ(far - before, FP::TimeSpan(HalfRange - 1));
  EXPECT_EQ(before - far, FP::TimeSpan(-(HalfRange - 1)));
}

TEST(TickCount32, ComparesTheShorterWayRoundAcrossTheWrap)
{
  const FP::TickCount32 before(MaxCount - 1u);
  const FP::TickCount32 after(2u);
  EXPECT_TRUE(before < after);
  EXPECT_FALSE(after < before);
  EXPECT_TRUE(before <= after);
  EXPECT_TRUE(before <= before);
  EXPECT_FALSE(after <= before);
  EXPECT_TRUE(after > before);
  EXPECT_FALSE(before > after);
  EXPECT_TRUE(after >= before);
  EXPECT_TRUE(after >= after);
  EXPECT_FALSE(before >= after);
  EXPECT_TRUE(before == FP::TickCount32(MaxCount - 1u));
  EXPECT_TRUE(before != after);
  // Exactly 2^31 ticks apart there is no shorter way round: each is "less" than the other (documented)
  const FP::TickCount32 opposite = before + FP::TimeSpan(HalfRange);
  EXPECT_TRUE(before < opposite);
  EXPECT_TRUE(opposite < before);
  EXPECT_EQ(opposite - before, FP::TimeSpan(-HalfRange));
  static_assert(FP::TickCount32(1u) < FP::TickCount32(2u));
}
