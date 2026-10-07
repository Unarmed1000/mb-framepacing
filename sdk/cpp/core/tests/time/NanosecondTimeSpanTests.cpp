// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// NanosecondTimeSpan: a signed interval in nanoseconds, kept as a platform that counts in nanoseconds gives it. To and from a
// TimeSpan (ticks of 100 ns) it is exact one way and truncated the other, and out of range throws as TimeSpan does.
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <gtest/gtest.h>
#include <compare>
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

TEST(NanosecondTimeSpan, HoldsASignedCountOfNanoseconds)
{
  static_assert(std::is_trivially_copyable_v<FP::NanosecondTimeSpan> && std::is_standard_layout_v<FP::NanosecondTimeSpan> &&
                sizeof(FP::NanosecondTimeSpan) == 8);
  EXPECT_EQ(FP::NanosecondTimeSpan().Nanoseconds(), 0);
  EXPECT_EQ(FP::NanosecondTimeSpan::Zero().Nanoseconds(), 0);
  EXPECT_EQ(FP::NanosecondTimeSpan(4'166'389).Nanoseconds(), 4'166'389);
  EXPECT_EQ(FP::NanosecondTimeSpan(-1).Nanoseconds(), -1);
  EXPECT_EQ(FP::NanosecondTimeSpan::MinValue().Nanoseconds(), MinInt64);
  EXPECT_EQ(FP::NanosecondTimeSpan::MaxValue().Nanoseconds(), MaxInt64);
  EXPECT_EQ(FP::NanosecondTimeSpan::FromNanoseconds(-7).Nanoseconds(), -7);
  static_assert(FP::NanosecondTimeSpan::FromNanoseconds(5) == FP::NanosecondTimeSpan(5));
  static_assert(FP::NanosecondTimeSpan::NanosecondsPerTick == 100 && FP::NanosecondTimeSpan::NanosecondsPerMicrosecond == 1'000 &&
                FP::NanosecondTimeSpan::NanosecondsPerMillisecond == 1'000'000 && FP::NanosecondTimeSpan::NanosecondsPerSecond == 1'000'000'000);
}

TEST(NanosecondTimeSpan, IsMadeFromWholeUnitsAndThrowsOutsideItsRange)
{
  EXPECT_EQ(FP::NanosecondTimeSpan::FromMicroseconds(4'166).Nanoseconds(), 4'166'000);
  EXPECT_EQ(FP::NanosecondTimeSpan::FromMilliseconds(-16).Nanoseconds(), -16'000'000);
  EXPECT_EQ(FP::NanosecondTimeSpan::FromSeconds(2).Nanoseconds(), 2'000'000'000);
  // The ends of the range: about 292 years either way
  EXPECT_EQ(FP::NanosecondTimeSpan::FromSeconds(9'223'372'036).Nanoseconds(), 9'223'372'036'000'000'000);
  EXPECT_EQ(FP::NanosecondTimeSpan::FromSeconds(-9'223'372'036).Nanoseconds(), -9'223'372'036'000'000'000);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeSpan::FromSeconds(9'223'372'037)), std::out_of_range);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeSpan::FromSeconds(-9'223'372'037)), std::out_of_range);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeSpan::FromMilliseconds(MaxInt64)), std::out_of_range);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeSpan::FromMicroseconds(MinInt64)), std::out_of_range);
  static_assert(FP::NanosecondTimeSpan::FromMilliseconds(1) == FP::NanosecondTimeSpan(1'000'000));
}

TEST(NanosecondTimeSpan, ATimeSpanIsExactInNanosecondsAndTheWayBackTruncatesToATick)
{
  // 100 ns a tick, exactly
  EXPECT_EQ(FP::NanosecondTimeSpan::FromTimeSpan(FP::TimeSpan(41'664)).Nanoseconds(), 4'166'400);
  EXPECT_EQ(FP::NanosecondTimeSpan::FromTimeSpan(FP::TimeSpan(-1)).Nanoseconds(), -100);
  EXPECT_EQ(FP::NanosecondTimeSpan::FromTimeSpan(FP::TimeSpan()), FP::NanosecondTimeSpan::Zero());
  // A TimeSpan reaches a hundred times as far
  EXPECT_EQ(FP::NanosecondTimeSpan::FromTimeSpan(FP::TimeSpan(MaxInt64 / 100)).Nanoseconds(), (MaxInt64 / 100) * 100);
  EXPECT_EQ(FP::NanosecondTimeSpan::FromTimeSpan(FP::TimeSpan(MinInt64 / 100)).Nanoseconds(), (MinInt64 / 100) * 100);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeSpan::FromTimeSpan(FP::TimeSpan((MaxInt64 / 100) + 1))), std::out_of_range);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeSpan::FromTimeSpan(FP::TimeSpan((MinInt64 / 100) - 1))), std::out_of_range);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeSpan::FromTimeSpan(FP::TimeSpan::MaxValue())), std::out_of_range);

  // The refresh period of a 240.016 Hz mode: 4,166,389 ns is 41,663 ticks and 89 ns that a TimeSpan does not hold
  EXPECT_EQ(FP::NanosecondTimeSpan(4'166'389).ToTimeSpan(), FP::TimeSpan(41'663));
  EXPECT_EQ(FP::NanosecondTimeSpan(99).ToTimeSpan(), FP::TimeSpan(0));
  EXPECT_EQ(FP::NanosecondTimeSpan(100).ToTimeSpan(), FP::TimeSpan(1));
  // Toward zero, as every conversion to a TimeSpan
  EXPECT_EQ(FP::NanosecondTimeSpan(-99).ToTimeSpan(), FP::TimeSpan(0));
  EXPECT_EQ(FP::NanosecondTimeSpan(-199).ToTimeSpan(), FP::TimeSpan(-1));
  EXPECT_EQ(FP::NanosecondTimeSpan::MaxValue().ToTimeSpan(), FP::TimeSpan(MaxInt64 / 100));
  EXPECT_EQ(FP::NanosecondTimeSpan::MinValue().ToTimeSpan(), FP::TimeSpan(MinInt64 / 100));
  static_assert(FP::NanosecondTimeSpan::FromTimeSpan(FP::TimeSpan(3)).ToTimeSpan() == FP::TimeSpan(3));
}

TEST(NanosecondTimeSpan, GivesItsTotalInLargerUnits)
{
  const FP::NanosecondTimeSpan span(4'166'389);
  EXPECT_DOUBLE_EQ(span.TotalMicroseconds(), 4'166.389);
  EXPECT_DOUBLE_EQ(span.TotalMilliseconds(), 4.166389);
  EXPECT_DOUBLE_EQ(span.TotalSeconds(), 0.004166389);
  EXPECT_DOUBLE_EQ(FP::NanosecondTimeSpan(-1'500'000'000).TotalSeconds(), -1.5);
}

TEST(NanosecondTimeSpan, AddsSubtractsAndNegatesAndThrowsOutsideItsRange)
{
  const FP::NanosecondTimeSpan a(4'166'389);
  const FP::NanosecondTimeSpan b(-389);
  EXPECT_EQ((a + b).Nanoseconds(), 4'166'000);
  EXPECT_EQ((a - b).Nanoseconds(), 4'166'778);
  EXPECT_EQ((b - a).Nanoseconds(), -4'166'778);
  EXPECT_EQ((+a), a);
  EXPECT_EQ((-a).Nanoseconds(), -4'166'389);
  EXPECT_EQ(a.Negate(), -a);
  EXPECT_EQ(b.Duration().Nanoseconds(), 389);
  EXPECT_EQ(a.Duration(), a);
  EXPECT_EQ(FP::NanosecondTimeSpan().Duration(), FP::NanosecondTimeSpan::Zero());

  FP::NanosecondTimeSpan sum = a;
  sum += b;
  sum -= FP::NanosecondTimeSpan(1'000);
  EXPECT_EQ(sum.Nanoseconds(), 4'165'000);

  // The ends
  EXPECT_EQ(FP::NanosecondTimeSpan::MaxValue() + FP::NanosecondTimeSpan(), FP::NanosecondTimeSpan::MaxValue());
  EXPECT_EQ(FP::NanosecondTimeSpan::MaxValue() + FP::NanosecondTimeSpan(-1), FP::NanosecondTimeSpan(MaxInt64 - 1));
  EXPECT_EQ(FP::NanosecondTimeSpan::MinValue() - FP::NanosecondTimeSpan(-1), FP::NanosecondTimeSpan(MinInt64 + 1));
  EXPECT_EQ(FP::NanosecondTimeSpan::MinValue() + FP::NanosecondTimeSpan::MaxValue(), FP::NanosecondTimeSpan(-1));
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeSpan::MaxValue() + FP::NanosecondTimeSpan(1)), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeSpan::MinValue() + FP::NanosecondTimeSpan(-1)), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeSpan::MaxValue() - FP::NanosecondTimeSpan(-1)), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeSpan::MinValue() - FP::NanosecondTimeSpan(1)), std::overflow_error);
  // The lowest value has no positive counterpart
  EXPECT_THROW(static_cast<void>(-FP::NanosecondTimeSpan::MinValue()), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeSpan::MinValue().Negate()), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeSpan::MinValue().Duration()), std::overflow_error);
  EXPECT_EQ(-FP::NanosecondTimeSpan::MaxValue(), FP::NanosecondTimeSpan(MinInt64 + 1));
  static_assert(FP::NanosecondTimeSpan(5) + FP::NanosecondTimeSpan(-2) == FP::NanosecondTimeSpan(3));
  static_assert(FP::NanosecondTimeSpan(5) - FP::NanosecondTimeSpan(7) == FP::NanosecondTimeSpan(-2));
}

TEST(NanosecondTimeSpan, ComparesByItsCount)
{
  const FP::NanosecondTimeSpan shorter(-100);
  const FP::NanosecondTimeSpan longer(100);
  EXPECT_TRUE(shorter < longer);
  EXPECT_TRUE(shorter <= longer);
  EXPECT_TRUE(longer > shorter);
  EXPECT_TRUE(longer >= shorter);
  EXPECT_TRUE(shorter != longer);
  EXPECT_TRUE(shorter == FP::NanosecondTimeSpan(-100));
  EXPECT_TRUE(shorter <= FP::NanosecondTimeSpan(-100));
  EXPECT_EQ(shorter <=> longer, std::strong_ordering::less);
  EXPECT_TRUE(FP::NanosecondTimeSpan::MinValue() < FP::NanosecondTimeSpan::MaxValue());
}
