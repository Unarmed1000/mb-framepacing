// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// NanosecondTickCount: a point on a clock that counts in nanoseconds, stored unsigned so it wraps with defined behaviour and
// compares across the wrap, as TickCount64 does in ticks of 100 ns. To a TickCount64 it is the tick the point is in.
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <gtest/gtest.h>
#include <cstdint>
#include <limits>
#include <stdexcept>
#include <type_traits>

namespace FP = MB::FramePacing;

namespace
{
  constexpr int64_t MaxInt64 = std::numeric_limits<int64_t>::max();
  constexpr int64_t MinInt64 = std::numeric_limits<int64_t>::min();
}

TEST(NanosecondTickCount, HoldsACountOfNanosecondsStoredUnsigned)
{
  static_assert(std::is_trivially_copyable_v<FP::NanosecondTickCount> && std::is_standard_layout_v<FP::NanosecondTickCount> &&
                sizeof(FP::NanosecondTickCount) == 8);
  EXPECT_EQ(FP::NanosecondTickCount().Nanoseconds(), 0);
  EXPECT_EQ(FP::NanosecondTickCount(4'166'389).Nanoseconds(), 4'166'389);
  EXPECT_EQ(FP::NanosecondTickCount(4'166'389).UnsignedNanoseconds(), 4'166'389u);
  EXPECT_EQ(FP::NanosecondTickCount(-1).Nanoseconds(), -1);
  EXPECT_EQ(FP::NanosecondTickCount(-1).UnsignedNanoseconds(), std::numeric_limits<uint64_t>::max());
  EXPECT_EQ(FP::NanosecondTickCount::FromNanoseconds(-7).Nanoseconds(), -7);
  EXPECT_EQ(FP::NanosecondTickCount::FromUnsignedNanoseconds(std::numeric_limits<uint64_t>::max()).Nanoseconds(), -1);
  EXPECT_EQ(FP::NanosecondTickCount::FromUnsignedNanoseconds(12u), FP::NanosecondTickCount(12));
  // The point a span after the clock's epoch, and the span since it
  EXPECT_EQ(FP::NanosecondTickCount(FP::NanosecondTimeSpan(4'166'389)).Nanoseconds(), 4'166'389);
  EXPECT_EQ(FP::NanosecondTickCount(4'166'389).ToNanosecondTimeSpan(), FP::NanosecondTimeSpan(4'166'389));
  static_assert(FP::NanosecondTickCount(5) == FP::NanosecondTickCount::FromNanoseconds(5));
  static_assert(FP::NanosecondTickCount::NanosecondsPerTick == 100 && FP::NanosecondTickCount::NanosecondsPerMicrosecond == 1'000 &&
                FP::NanosecondTickCount::NanosecondsPerMillisecond == 1'000'000 && FP::NanosecondTickCount::NanosecondsPerSecond == 1'000'000'000);
}

TEST(NanosecondTickCount, IsMadeFromWholeUnitsAndThrowsOutsideItsRange)
{
  EXPECT_EQ(FP::NanosecondTickCount::FromSeconds(2).Nanoseconds(), 2'000'000'000);
  EXPECT_EQ(FP::NanosecondTickCount::FromMilliseconds(-16).Nanoseconds(), -16'000'000);
  EXPECT_EQ(FP::NanosecondTickCount::FromMicroseconds(4'166).Nanoseconds(), 4'166'000);
  EXPECT_EQ(FP::NanosecondTickCount::FromSeconds(FP::NanosecondTickCount::MaxSeconds).Nanoseconds(), 9'223'372'036'000'000'000);
  EXPECT_EQ(FP::NanosecondTickCount::FromSeconds(FP::NanosecondTickCount::MinSeconds).Nanoseconds(), -9'223'372'036'000'000'000);
  EXPECT_EQ(FP::NanosecondTickCount::FromMilliseconds(FP::NanosecondTickCount::MaxMilliseconds).Nanoseconds(), 9'223'372'036'854'000'000);
  EXPECT_EQ(FP::NanosecondTickCount::FromMicroseconds(FP::NanosecondTickCount::MinMicroseconds).Nanoseconds(), -9'223'372'036'854'775'000);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTickCount::FromSeconds(FP::NanosecondTickCount::MaxSeconds + 1)), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTickCount::FromSeconds(FP::NanosecondTickCount::MinSeconds - 1)), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTickCount::FromMilliseconds(FP::NanosecondTickCount::MaxMilliseconds + 1)), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTickCount::FromMicroseconds(FP::NanosecondTickCount::MinMicroseconds - 1)), std::overflow_error);
  static_assert(FP::NanosecondTickCount::FromMilliseconds(1) == FP::NanosecondTickCount(1'000'000));
}

TEST(NanosecondTickCount, ATickCountIsExactInNanosecondsAndTheWayBackIsTheTickThePointIsIn)
{
  EXPECT_EQ(FP::NanosecondTickCount::FromTickCount64(FP::TickCount64(41'664)).Nanoseconds(), 4'166'400);
  EXPECT_EQ(FP::NanosecondTickCount::FromTickCount64(FP::TickCount64(-1)).Nanoseconds(), -100);
  EXPECT_EQ(FP::NanosecondTickCount::FromTickCount64(FP::TickCount64(FP::NanosecondTickCount::MaxTicks)).Nanoseconds(), (MaxInt64 / 100) * 100);
  EXPECT_EQ(FP::NanosecondTickCount::FromTickCount64(FP::TickCount64(FP::NanosecondTickCount::MinTicks)).Nanoseconds(), (MinInt64 / 100) * 100);
  // A TickCount64 reaches a hundred times as far
  EXPECT_THROW(static_cast<void>(FP::NanosecondTickCount::FromTickCount64(FP::TickCount64(FP::NanosecondTickCount::MaxTicks + 1))),
               std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTickCount::FromTickCount64(FP::TickCount64(FP::NanosecondTickCount::MinTicks - 1))),
               std::overflow_error);

  // Rounded down to the tick the point is in, before the epoch too
  EXPECT_EQ(FP::NanosecondTickCount(4'166'389).ToTickCount64(), FP::TickCount64(41'663));
  EXPECT_EQ(FP::NanosecondTickCount(99).ToTickCount64(), FP::TickCount64(0));
  EXPECT_EQ(FP::NanosecondTickCount(100).ToTickCount64(), FP::TickCount64(1));
  EXPECT_EQ(FP::NanosecondTickCount(-1).ToTickCount64(), FP::TickCount64(-1));
  EXPECT_EQ(FP::NanosecondTickCount(-100).ToTickCount64(), FP::TickCount64(-1));
  EXPECT_EQ(FP::NanosecondTickCount(-101).ToTickCount64(), FP::TickCount64(-2));
  static_assert(FP::NanosecondTickCount::FromTickCount64(FP::TickCount64(3)).ToTickCount64() == FP::TickCount64(3));
}

TEST(NanosecondTickCount, ACounterConvertsExactlyAtAnyValue)
{
  // A counter in nanoseconds is the count already
  EXPECT_EQ(FP::NanosecondTickCount::FromCounter(123'456'789, 1'000'000'000).Nanoseconds(), 123'456'789);
  // A 10 MHz counter (QueryPerformanceFrequency on current Windows) counts in ticks of 100 ns
  EXPECT_EQ(FP::NanosecondTickCount::FromCounter(123'456'789, 10'000'000).Nanoseconds(), 12'345'678'900);
  // A 3 GHz counter: one second and a third of a nanosecond, rounded down to the nanosecond it is in
  EXPECT_EQ(FP::NanosecondTickCount::FromCounter(3'000'000'001, 3'000'000'000).Nanoseconds(), 1'000'000'000);
  EXPECT_EQ(FP::NanosecondTickCount::FromCounter(3'000'000'003, 3'000'000'000).Nanoseconds(), 1'000'000'001);
  // Before the clock's epoch too: -1 of a 3 GHz counter is in the nanosecond -1, and whole seconds stay whole
  EXPECT_EQ(FP::NanosecondTickCount::FromCounter(-1, 3'000'000'000).Nanoseconds(), -1);
  EXPECT_EQ(FP::NanosecondTickCount::FromCounter(-6'000'000'000, 3'000'000'000).Nanoseconds(), -2'000'000'000);
  EXPECT_EQ(FP::NanosecondTickCount::FromCounter(-15, 10'000'000), FP::NanosecondTickCount::FromTickCount64(FP::TickCount64(-15)));
  // A counter near its limit does not overflow: 2^63 - 1 at 3 GHz is about 97 years
  constexpr int64_t Frequency = 3'000'000'000;
  constexpr int64_t PerSecond = FP::NanosecondTickCount::NanosecondsPerSecond;
  EXPECT_EQ(FP::NanosecondTickCount::FromCounter(MaxInt64, Frequency).Nanoseconds(),
            ((MaxInt64 / Frequency) * PerSecond) + (((MaxInt64 % Frequency) * PerSecond) / Frequency));
  // The fastest counter it takes, at its limit too
  constexpr int64_t Fastest = FP::NanosecondTickCount::MaxCounterFrequency;
  static_assert(Fastest == 9'223'372'036);
  EXPECT_EQ(FP::NanosecondTickCount::FromCounter(Fastest, Fastest).Nanoseconds(), PerSecond);
  EXPECT_EQ(FP::NanosecondTickCount::FromCounter(MaxInt64, Fastest).Nanoseconds(),
            ((MaxInt64 / Fastest) * PerSecond) + (((MaxInt64 % Fastest) * PerSecond) / Fastest));
  static_assert(FP::NanosecondTickCount::FromCounter(20'000'000, 10'000'000).Nanoseconds() == 2'000'000'000);
}

TEST(NanosecondTickCount, ASlowCounterPastTheRangeWrapsAsTheCountDoes)
{
  // A counter slower than the nanosecond can count more seconds than a NanosecondTickCount holds (2^63 - 1 seconds at 1 Hz): the count
  // wraps, as every NanosecondTickCount does, and the difference of two such counts is still their distance
  constexpr auto Wrapped =
    static_cast<int64_t>(static_cast<uint64_t>(MaxInt64) * static_cast<uint64_t>(FP::NanosecondTickCount::NanosecondsPerSecond));
  static_assert(FP::NanosecondTickCount::FromCounter(MaxInt64, 1).Nanoseconds() == Wrapped);
  EXPECT_EQ(FP::NanosecondTickCount::FromCounter(MaxInt64, 1).Nanoseconds(), Wrapped);
  EXPECT_EQ(FP::NanosecondTickCount::FromCounter(MaxInt64, 1) - FP::NanosecondTickCount::FromCounter(MaxInt64 - 3, 1),
            FP::NanosecondTimeSpan::FromSeconds(3));
  EXPECT_EQ(FP::NanosecondTickCount::FromCounter(MinInt64, 1000) - FP::NanosecondTickCount::FromCounter(MinInt64 + 1, 1000),
            FP::NanosecondTimeSpan::FromMilliseconds(-1));
}

TEST(NanosecondTickCount, ACounterFrequencyOutsideItsRangeThrows)
{
  EXPECT_THROW(static_cast<void>(FP::NanosecondTickCount::FromCounter(123, 0)), std::out_of_range);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTickCount::FromCounter(123, -10'000'000)), std::out_of_range);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTickCount::FromCounter(123, FP::NanosecondTickCount::MaxCounterFrequency + 1)), std::out_of_range);
}

TEST(NanosecondTickCount, GivesItsTotalInLargerUnits)
{
  const FP::NanosecondTickCount count(4'166'389);
  EXPECT_DOUBLE_EQ(count.TotalMicroseconds(), 4'166.389);
  EXPECT_DOUBLE_EQ(count.TotalMilliseconds(), 4.166389);
  EXPECT_DOUBLE_EQ(count.TotalSeconds(), 0.004166389);
  EXPECT_DOUBLE_EQ(FP::NanosecondTickCount(-1'500'000'000).TotalSeconds(), -1.5);
}

TEST(NanosecondTickCount, ASpanMovesItAndTwoCountsAreASpanApart)
{
  const FP::NanosecondTickCount count(1'000'000'000);
  EXPECT_EQ((count + FP::NanosecondTimeSpan(4'166'389)).Nanoseconds(), 1'004'166'389);
  EXPECT_EQ((count - FP::NanosecondTimeSpan(4'166'389)).Nanoseconds(), 995'833'611);
  EXPECT_EQ((count + FP::NanosecondTimeSpan(-389)).Nanoseconds(), 999'999'611);
  FP::NanosecondTickCount moved = count;
  moved += FP::NanosecondTimeSpan(500);
  moved -= FP::NanosecondTimeSpan(200);
  EXPECT_EQ(moved.Nanoseconds(), 1'000'000'300);
  EXPECT_EQ(moved - count, FP::NanosecondTimeSpan(300));
  EXPECT_EQ(count - moved, FP::NanosecondTimeSpan(-300));
  static_assert(FP::NanosecondTickCount(7) - FP::NanosecondTickCount(9) == FP::NanosecondTimeSpan(-2));
  static_assert(FP::NanosecondTickCount(7) + FP::NanosecondTimeSpan(2) == FP::NanosecondTickCount(9));
}

TEST(NanosecondTickCount, WrapsAroundAndComparesAcrossTheWrap)
{
  // One nanosecond past the highest signed value is the lowest: the count wraps, with defined behaviour
  const FP::NanosecondTickCount last(MaxInt64);
  const FP::NanosecondTickCount next = last + FP::NanosecondTimeSpan(1);
  EXPECT_EQ(next.Nanoseconds(), MinInt64);
  EXPECT_EQ(next - last, FP::NanosecondTimeSpan(1));
  EXPECT_EQ(last - next, FP::NanosecondTimeSpan(-1));
  EXPECT_TRUE(last < next);
  EXPECT_TRUE(last <= next);
  EXPECT_TRUE(next > last);
  EXPECT_TRUE(next >= last);
  EXPECT_FALSE(next < last);
  EXPECT_FALSE(last > next);
  // And across the unsigned wrap
  const FP::NanosecondTickCount before = FP::NanosecondTickCount::FromUnsignedNanoseconds(std::numeric_limits<uint64_t>::max());
  const FP::NanosecondTickCount after = before + FP::NanosecondTimeSpan(2);
  EXPECT_EQ(after.UnsignedNanoseconds(), 1u);
  EXPECT_TRUE(before < after);
  EXPECT_EQ(after - before, FP::NanosecondTimeSpan(2));
  EXPECT_EQ((after - FP::NanosecondTimeSpan(2)), before);

  // The plain order of counts close together
  const FP::NanosecondTickCount earlier(100);
  const FP::NanosecondTickCount later(200);
  EXPECT_TRUE(earlier < later);
  EXPECT_TRUE(earlier <= later);
  EXPECT_TRUE(earlier <= FP::NanosecondTickCount(100));
  EXPECT_TRUE(later > earlier);
  EXPECT_TRUE(later >= earlier);
  EXPECT_TRUE(later >= FP::NanosecondTickCount(200));
  EXPECT_TRUE(earlier != later);
  EXPECT_TRUE(earlier == FP::NanosecondTickCount(100));
  EXPECT_FALSE(earlier > later);
  EXPECT_FALSE(later <= earlier);
}
