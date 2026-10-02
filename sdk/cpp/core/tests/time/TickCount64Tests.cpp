// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// TickCount64: a point on the application's steady clock, stored unsigned so its arithmetic wraps with defined behaviour.
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
  constexpr int64_t MaxTicks = std::numeric_limits<int64_t>::max();
  constexpr int64_t MinTicks = std::numeric_limits<int64_t>::min();
}

TEST(TickCount64, HasTheUnitsOfATimeSpan)
{
  static_assert(FP::TickCount64::NanosecondsPerTick == 100 && FP::TickCount64::TicksPerMicrosecond == 10);
  static_assert(FP::TickCount64::TicksPerMillisecond == 10'000 && FP::TickCount64::TicksPerSecond == 10'000'000);
  static_assert(FP::TickCount64::TicksPerMinute == 600'000'000 && FP::TickCount64::TicksPerHour == 36'000'000'000);
  static_assert(FP::TickCount64::TicksPerDay == 864'000'000'000);
  static_assert(FP::TickCount64::MinDays == -10'675'199 && FP::TickCount64::MaxDays == 10'675'199);
  static_assert(FP::TickCount64::MinHours == -256'204'778 && FP::TickCount64::MaxHours == 256'204'778);
  static_assert(FP::TickCount64::MinMinutes == -15'372'286'728 && FP::TickCount64::MaxMinutes == 15'372'286'728);
  static_assert(FP::TickCount64::MinSeconds == -922'337'203'685 && FP::TickCount64::MaxSeconds == 922'337'203'685);
  static_assert(FP::TickCount64::MinMilliseconds == -922'337'203'685'477 && FP::TickCount64::MaxMilliseconds == 922'337'203'685'477);
  static_assert(FP::TickCount64::MinMicroseconds == -922'337'203'685'477'580 && FP::TickCount64::MaxMicroseconds == 922'337'203'685'477'580);
  static_assert(std::is_trivially_copyable_v<FP::TickCount64> && std::is_standard_layout_v<FP::TickCount64> && sizeof(FP::TickCount64) == 8);
}

TEST(TickCount64, IsAnUnsignedCountWithASignedView)
{
  EXPECT_EQ(FP::TickCount64().Ticks(), 0);
  EXPECT_EQ(FP::TickCount64(-1).Ticks(), -1);
  EXPECT_EQ(FP::TickCount64(-1).UnsignedTicks(), std::numeric_limits<uint64_t>::max());
  EXPECT_EQ(FP::TickCount64(FP::TimeSpan(42)).Ticks(), 42);
  EXPECT_EQ(FP::TickCount64::FromTicks(MinTicks).Ticks(), MinTicks);
  EXPECT_EQ(FP::TickCount64::FromUnsignedTicks(std::numeric_limits<uint64_t>::max()).Ticks(), -1);
  EXPECT_EQ(FP::TickCount64::FromUnsignedTicks(5u).UnsignedTicks(), 5u);
  EXPECT_EQ(FP::TickCount64(123).ToTimeSpan(), FP::TimeSpan(123));
}

TEST(TickCount64, TheFactoriesThrowOutsideTheirRange)
{
  EXPECT_EQ(FP::TickCount64::FromDays(FP::TickCount64::MaxDays).Ticks(), 9'223'371'936'000'000'000);
  EXPECT_EQ(FP::TickCount64::FromDays(FP::TickCount64::MinDays).Ticks(), -9'223'371'936'000'000'000);
  EXPECT_THROW(static_cast<void>(FP::TickCount64::FromDays(FP::TickCount64::MaxDays + 1)), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::TickCount64::FromDays(FP::TickCount64::MinDays - 1)), std::overflow_error);
  EXPECT_EQ(FP::TickCount64::FromHours(2).Ticks(), 72'000'000'000);
  EXPECT_THROW(static_cast<void>(FP::TickCount64::FromHours(FP::TickCount64::MaxHours + 1)), std::overflow_error);
  EXPECT_EQ(FP::TickCount64::FromMinutes(-2).Ticks(), -1'200'000'000);
  EXPECT_THROW(static_cast<void>(FP::TickCount64::FromMinutes(FP::TickCount64::MinMinutes - 1)), std::overflow_error);
  EXPECT_EQ(FP::TickCount64::FromSeconds(3).Ticks(), 30'000'000);
  EXPECT_THROW(static_cast<void>(FP::TickCount64::FromSeconds(FP::TickCount64::MaxSeconds + 1)), std::overflow_error);
  EXPECT_EQ(FP::TickCount64::FromMilliseconds(16).Ticks(), 160'000);
  EXPECT_THROW(static_cast<void>(FP::TickCount64::FromMilliseconds(FP::TickCount64::MaxMilliseconds + 1)), std::overflow_error);
  EXPECT_EQ(FP::TickCount64::FromMicroseconds(FP::TickCount64::MaxMicroseconds).Ticks(), 9'223'372'036'854'775'800);
  EXPECT_THROW(static_cast<void>(FP::TickCount64::FromMicroseconds(FP::TickCount64::MinMicroseconds - 1)), std::overflow_error);
}

TEST(TickCount64, NanosecondsRoundDownToTheTickTheyAreIn)
{
  EXPECT_EQ(FP::TickCount64::FromNanoseconds(0).Ticks(), 0);
  EXPECT_EQ(FP::TickCount64::FromNanoseconds(99).Ticks(), 0);
  EXPECT_EQ(FP::TickCount64::FromNanoseconds(100).Ticks(), 1);
  EXPECT_EQ(FP::TickCount64::FromNanoseconds(150).Ticks(), 1);
  EXPECT_EQ(FP::TickCount64::FromNanoseconds(16'683'350).Ticks(), 166'833);
  EXPECT_EQ(FP::TickCount64::FromNanoseconds(-1).Ticks(), -1);
  EXPECT_EQ(FP::TickCount64::FromNanoseconds(-100).Ticks(), -1);
  EXPECT_EQ(FP::TickCount64::FromNanoseconds(-101).Ticks(), -2);
  EXPECT_EQ(FP::TickCount64::FromNanoseconds(MaxTicks).Ticks(), MaxTicks / 100);
  static_assert(FP::TickCount64::FromNanoseconds(-150).Ticks() == -2);
}

TEST(TickCount64, ACounterConvertsExactlyAtAnyValue)
{
  // A 10 MHz counter (QueryPerformanceFrequency on current Windows) is in ticks already
  EXPECT_EQ(FP::TickCount64::FromCounter(123'456'789, 10'000'000).Ticks(), 123'456'789);
  // A 3 GHz counter: one second and a third of a tick
  EXPECT_EQ(FP::TickCount64::FromCounter(3'000'000'100, 3'000'000'000).Ticks(), FP::TickCount64::TicksPerSecond);
  EXPECT_EQ(FP::TickCount64::FromCounter(3'000'000'300, 3'000'000'000).Ticks(), FP::TickCount64::TicksPerSecond + 1);
  // Nanoseconds as a counter agree with FromNanoseconds, negative values included
  EXPECT_EQ(FP::TickCount64::FromCounter(-150, 1'000'000'000), FP::TickCount64::FromNanoseconds(-150));
  // A counter near its limit does not overflow: 2^63 - 1 at 3 GHz is about 97 years
  constexpr int64_t Frequency = 3'000'000'000;
  constexpr int64_t TicksPerSecond = FP::TickCount64::TicksPerSecond;
  EXPECT_EQ(FP::TickCount64::FromCounter(MaxTicks, Frequency).Ticks(),
            ((MaxTicks / Frequency) * TicksPerSecond) + (((MaxTicks % Frequency) * TicksPerSecond) / Frequency));
  // The fastest counter it takes, at its limit too
  constexpr int64_t Fastest = FP::TickCount64::MaxCounterFrequency;
  static_assert(Fastest == 922'337'203'685);
  EXPECT_EQ(FP::TickCount64::FromCounter(Fastest, Fastest).Ticks(), TicksPerSecond);
  EXPECT_EQ(FP::TickCount64::FromCounter(MaxTicks, Fastest).Ticks(),
            ((MaxTicks / Fastest) * TicksPerSecond) + (((MaxTicks % Fastest) * TicksPerSecond) / Fastest));
  static_assert(FP::TickCount64::FromCounter(20'000'000, 10'000'000).Ticks() == 20'000'000);
}

TEST(TickCount64, ASlowCounterPastTheRangeWrapsAsTheCountDoes)
{
  // A counter slower than the tick can count more seconds than a TickCount64 holds (2^63 - 1 seconds at 1 Hz): the count wraps, as
  // every TickCount64 does, and the difference of two such counts is still their distance
  constexpr auto Wrapped = static_cast<int64_t>(static_cast<uint64_t>(MaxTicks) * static_cast<uint64_t>(FP::TickCount64::TicksPerSecond));
  static_assert(FP::TickCount64::FromCounter(MaxTicks, 1).Ticks() == Wrapped);
  EXPECT_EQ(FP::TickCount64::FromCounter(MaxTicks, 1).Ticks(), Wrapped);
  EXPECT_EQ(FP::TickCount64::FromCounter(MaxTicks, 1) - FP::TickCount64::FromCounter(MaxTicks - 3, 1), FP::TimeSpan::FromSeconds(3));
  EXPECT_EQ(FP::TickCount64::FromCounter(std::numeric_limits<int64_t>::min(), 1000) -
              FP::TickCount64::FromCounter(std::numeric_limits<int64_t>::min() + 1, 1000),
            FP::TimeSpan::FromMilliseconds(-1));
}

TEST(TickCount64, CountsExactlyHalfTheRangeApartAreEachLessThanTheOther)
{
  // The comparisons look at the distance across the wrap: at exactly 2^63 ticks apart it is the same either way
  const FP::TickCount64 a(0);
  const FP::TickCount64 b(MinTicks);
  EXPECT_TRUE(a < b);
  EXPECT_TRUE(b < a);
  EXPECT_FALSE(a > b);
  EXPECT_FALSE(b > a);
  EXPECT_FALSE(a == b);
  // One tick less and the order is plain again
  EXPECT_TRUE(a < FP::TickCount64(MaxTicks));
  EXPECT_FALSE(FP::TickCount64(MaxTicks) < a);
}

TEST(TickCount64, ACounterFrequencyOutsideItsRangeThrows)
{
  EXPECT_THROW(static_cast<void>(FP::TickCount64::FromCounter(123, 0)), std::out_of_range);
  EXPECT_THROW(static_cast<void>(FP::TickCount64::FromCounter(123, -10'000'000)), std::out_of_range);
  EXPECT_THROW(static_cast<void>(FP::TickCount64::FromCounter(123, FP::TickCount64::MaxCounterFrequency + 1)), std::out_of_range);
}

TEST(TickCount64, ComponentsAndTotalsReadTheSignedView)
{
  const FP::TickCount64 count(123'456'789'012'345);
  EXPECT_EQ(count.Days(), 142);
  EXPECT_EQ(count.Hours(), 21);
  EXPECT_EQ(count.Minutes(), 21);
  EXPECT_EQ(count.Seconds(), 18);
  EXPECT_EQ(count.Milliseconds(), 901);
  EXPECT_EQ(count.Microseconds(), 234);
  EXPECT_EQ(count.TotalDays(), 142.88980209762153);
  EXPECT_EQ(count.TotalHours(), 3429.355250342917);
  EXPECT_EQ(count.TotalMinutes(), 205761.315020575);
  EXPECT_EQ(count.TotalSeconds(), 12345678.9012345);
  EXPECT_EQ(count.TotalMilliseconds(), 12345678901.2345);
  EXPECT_EQ(count.TotalMicroseconds(), 12345678901234.5);
  EXPECT_EQ(count.TotalNanoseconds(), 12345678901234500.0);

  const FP::TickCount64 negative(-937'840'050'067);
  EXPECT_EQ(negative.Days(), -1);
  EXPECT_EQ(negative.Hours(), -2);
  EXPECT_EQ(negative.Minutes(), -3);
  EXPECT_EQ(negative.Seconds(), -4);
  EXPECT_EQ(negative.Milliseconds(), -5);
  EXPECT_EQ(negative.Microseconds(), -6);
  EXPECT_EQ(negative.TotalSeconds(), -93784.0050067);
}

TEST(TickCount64, ArithmeticWrapsAround)
{
  EXPECT_EQ(FP::TickCount64(100) + FP::TimeSpan(5), FP::TickCount64(105));
  EXPECT_EQ(FP::TickCount64(100) - FP::TimeSpan(5), FP::TickCount64(95));
  EXPECT_EQ(FP::TickCount64(100) - FP::TickCount64(30), FP::TimeSpan(70));
  EXPECT_EQ(FP::TickCount64(30) - FP::TickCount64(100), FP::TimeSpan(-70));
  // Past the end of the signed view, with defined behaviour (the sanitizer build checks it)
  EXPECT_EQ(FP::TickCount64(MaxTicks) + FP::TimeSpan(1), FP::TickCount64(MinTicks));
  EXPECT_EQ(FP::TickCount64(MinTicks) - FP::TimeSpan(1), FP::TickCount64(MaxTicks));
  EXPECT_EQ(FP::TickCount64(MinTicks) - FP::TickCount64(MaxTicks), FP::TimeSpan(1));

  FP::TickCount64 count(10);
  count += FP::TimeSpan(-15);
  EXPECT_EQ(count.Ticks(), -5);
  count -= FP::TimeSpan(-5);
  EXPECT_EQ(count.Ticks(), 0);
}

TEST(TickCount64, ComparesTheShorterWayRoundAcrossTheWrap)
{
  const FP::TickCount64 earlier(-1);
  const FP::TickCount64 later(0);
  EXPECT_TRUE(earlier == FP::TickCount64(-1));
  EXPECT_TRUE(earlier != later);
  EXPECT_TRUE(earlier < later);
  EXPECT_FALSE(later < earlier);
  EXPECT_TRUE(earlier <= later);
  EXPECT_TRUE(later <= later);
  EXPECT_FALSE(later <= earlier);
  EXPECT_TRUE(later > earlier);
  EXPECT_FALSE(earlier > later);
  EXPECT_TRUE(later >= earlier);
  EXPECT_TRUE(later >= later);
  EXPECT_FALSE(earlier >= later);
  // Across the wrap of the stored count: MaxTicks + 1 tick is MinTicks, and it is later
  const FP::TickCount64 beforeWrap(MaxTicks);
  const FP::TickCount64 afterWrap = beforeWrap + FP::TimeSpan(1);
  EXPECT_TRUE(beforeWrap < afterWrap);
  EXPECT_TRUE(afterWrap > beforeWrap);
  EXPECT_EQ(afterWrap - beforeWrap, FP::TimeSpan(1));
  const FP::TickCount64 unsignedEnd = FP::TickCount64::FromUnsignedTicks(std::numeric_limits<uint64_t>::max());
  EXPECT_TRUE(unsignedEnd < FP::TickCount64::FromUnsignedTicks(1u));
  EXPECT_EQ(FP::TickCount64::FromUnsignedTicks(1u) - unsignedEnd, FP::TimeSpan(2));
  static_assert(FP::TickCount64::FromSeconds(1) - FP::TickCount64() == FP::TimeSpan::FromSeconds(1));
}
