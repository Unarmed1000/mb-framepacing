// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// NanosecondTimeDuration: a length of time in nanoseconds that is never negative. It holds a NanosecondTimeSpan that is zero or more,
// and what its arithmetic gives is a duration where the result can not be negative and a NanosecondTimeSpan where it can.
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/core/time/TimeDuration.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <gtest/gtest.h>
#include <compare>
#include <cstdint>
#include <limits>
#include <stdexcept>
#include <type_traits>

namespace FP = MB::FramePacing;

TEST(NanosecondTimeDuration, IsNeverNegative)
{
  static_assert(std::is_trivially_copyable_v<FP::NanosecondTimeDuration> && std::is_standard_layout_v<FP::NanosecondTimeDuration> &&
                sizeof(FP::NanosecondTimeDuration) == 8);
  EXPECT_EQ(FP::NanosecondTimeDuration().Nanoseconds(), 0);
  EXPECT_EQ(FP::NanosecondTimeDuration::Zero().Nanoseconds(), 0);
  EXPECT_EQ(FP::NanosecondTimeDuration(FP::NanosecondTimeSpan(166'667)).Nanoseconds(), 166'667);
  EXPECT_EQ(FP::NanosecondTimeDuration(FP::NanosecondTimeSpan()).Nanoseconds(), 0);
  // A negative value becomes zero
  EXPECT_EQ(FP::NanosecondTimeDuration(FP::NanosecondTimeSpan(-1)), FP::NanosecondTimeDuration::Zero());
  EXPECT_EQ(FP::NanosecondTimeDuration(FP::NanosecondTimeSpan::MinValue()), FP::NanosecondTimeDuration::Zero());
  EXPECT_EQ(FP::NanosecondTimeDuration(FP::NanosecondTimeSpan::MaxValue()), FP::NanosecondTimeDuration::MaxValue());
  EXPECT_EQ(FP::NanosecondTimeDuration::MaxValue().Nanoseconds(), std::numeric_limits<int64_t>::max());
  static_assert(FP::NanosecondTimeDuration(FP::NanosecondTimeSpan(-5)) == FP::NanosecondTimeDuration());
}

TEST(NanosecondTimeDuration, IsMadeFromNanosecondsASpanOfTheMarkersAndUnchecked)
{
  EXPECT_EQ(FP::NanosecondTimeDuration::FromNanoseconds(80'000).Nanoseconds(), 80'000);
  EXPECT_EQ(FP::NanosecondTimeDuration::FromNanoseconds(0).Nanoseconds(), 0);
  EXPECT_EQ(FP::NanosecondTimeDuration::FromNanoseconds(-80'000).Nanoseconds(), 0);
  EXPECT_EQ(FP::NanosecondTimeDuration::FromNanoseconds(std::numeric_limits<int64_t>::min()).Nanoseconds(), 0);
  // For a value that is known not to be negative
  EXPECT_EQ(FP::NanosecondTimeDuration::UncheckedCreate(FP::NanosecondTimeSpan(7)).Nanoseconds(), 7);
  EXPECT_EQ(FP::NanosecondTimeDuration::UncheckedCreate(FP::NanosecondTimeSpan()), FP::NanosecondTimeDuration::Zero());
  static_assert(FP::NanosecondTimeDuration::UncheckedCreate(FP::NanosecondTimeSpan(5)) == FP::NanosecondTimeDuration::FromNanoseconds(5));
}

TEST(NanosecondTimeDuration, GivesItsValueAsASpanAndAsNanoseconds)
{
  const FP::NanosecondTimeDuration duration = FP::NanosecondTimeDuration::FromNanoseconds(166'667);
  EXPECT_EQ(duration.Value(), FP::NanosecondTimeSpan(166'667));
  EXPECT_EQ(duration.Nanoseconds(), 166'667);
  EXPECT_EQ(duration.UnsignedNanoseconds(), 166'667u);
  EXPECT_EQ(FP::NanosecondTimeDuration::MaxValue().Value(), FP::NanosecondTimeSpan::MaxValue());
  EXPECT_EQ(FP::NanosecondTimeDuration::MaxValue().UnsignedNanoseconds(), 9'223'372'036'854'775'807u);
  EXPECT_EQ(FP::NanosecondTimeDuration().UnsignedNanoseconds(), 0u);
}

TEST(NanosecondTimeDuration, TwoDurationsAddUpToADuration)
{
  FP::NanosecondTimeDuration duration = FP::NanosecondTimeDuration::FromNanoseconds(100);
  duration += FP::NanosecondTimeDuration::FromNanoseconds(23);
  EXPECT_EQ(duration.Nanoseconds(), 123);
  duration += FP::NanosecondTimeDuration::Zero();
  EXPECT_EQ(duration.Nanoseconds(), 123);
  const FP::NanosecondTimeDuration sum = FP::NanosecondTimeDuration::FromNanoseconds(5) + FP::NanosecondTimeDuration::FromNanoseconds(6);
  EXPECT_EQ(sum, FP::NanosecondTimeDuration::FromNanoseconds(11));
  // Outside the range, as a NanosecondTimeSpan's sum
  EXPECT_THROW(duration += FP::NanosecondTimeDuration::MaxValue(), std::overflow_error);
  EXPECT_EQ(duration.Nanoseconds(), 123);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeDuration::MaxValue() + FP::NanosecondTimeDuration::FromNanoseconds(1)), std::overflow_error);
  EXPECT_EQ((FP::NanosecondTimeDuration::MaxValue() + FP::NanosecondTimeDuration::Zero()), FP::NanosecondTimeDuration::MaxValue());
  static_assert(FP::NanosecondTimeDuration::FromNanoseconds(2) + FP::NanosecondTimeDuration::FromNanoseconds(3) ==
                FP::NanosecondTimeDuration::FromNanoseconds(5));
}

TEST(NanosecondTimeDuration, ADurationTakenFromItStopsAtZero)
{
  FP::NanosecondTimeDuration duration = FP::NanosecondTimeDuration::FromNanoseconds(100);
  duration -= FP::NanosecondTimeDuration::FromNanoseconds(30);
  EXPECT_EQ(duration.Nanoseconds(), 70);
  duration -= FP::NanosecondTimeDuration::FromNanoseconds(70);
  EXPECT_EQ(duration.Nanoseconds(), 0);
  duration = FP::NanosecondTimeDuration::FromNanoseconds(5);
  duration -= FP::NanosecondTimeDuration::FromNanoseconds(6);
  EXPECT_EQ(duration, FP::NanosecondTimeDuration::Zero());
  duration -= FP::NanosecondTimeDuration::MaxValue();
  EXPECT_EQ(duration, FP::NanosecondTimeDuration::Zero());
  duration = FP::NanosecondTimeDuration::MaxValue();
  duration -= FP::NanosecondTimeDuration::MaxValue();
  EXPECT_EQ(duration, FP::NanosecondTimeDuration::Zero());
}

TEST(NanosecondTimeDuration, TheDifferenceOfTwoDurationsIsASpanThatCanBeNegative)
{
  static_assert(std::is_same_v<decltype(FP::NanosecondTimeDuration() - FP::NanosecondTimeDuration()), FP::NanosecondTimeSpan>);
  EXPECT_EQ(FP::NanosecondTimeDuration::FromNanoseconds(10) - FP::NanosecondTimeDuration::FromNanoseconds(4), FP::NanosecondTimeSpan(6));
  EXPECT_EQ(FP::NanosecondTimeDuration::FromNanoseconds(4) - FP::NanosecondTimeDuration::FromNanoseconds(10), FP::NanosecondTimeSpan(-6));
  EXPECT_EQ(FP::NanosecondTimeDuration::FromNanoseconds(4) - FP::NanosecondTimeDuration::FromNanoseconds(4), FP::NanosecondTimeSpan());
  // The whole range fits: the largest duration from none, and none from the largest
  EXPECT_EQ(FP::NanosecondTimeDuration::Zero() - FP::NanosecondTimeDuration::MaxValue(),
            FP::NanosecondTimeSpan(-std::numeric_limits<int64_t>::max()));
  EXPECT_EQ(FP::NanosecondTimeDuration::MaxValue() - FP::NanosecondTimeDuration::Zero(), FP::NanosecondTimeSpan::MaxValue());
}

TEST(NanosecondTimeDuration, ADurationAndASpanGiveASpan)
{
  static_assert(std::is_same_v<decltype(FP::NanosecondTimeDuration() + FP::NanosecondTimeSpan()), FP::NanosecondTimeSpan>);
  static_assert(std::is_same_v<decltype(FP::NanosecondTimeSpan() + FP::NanosecondTimeDuration()), FP::NanosecondTimeSpan>);
  static_assert(std::is_same_v<decltype(FP::NanosecondTimeDuration() - FP::NanosecondTimeSpan()), FP::NanosecondTimeSpan>);
  static_assert(std::is_same_v<decltype(FP::NanosecondTimeSpan() - FP::NanosecondTimeDuration()), FP::NanosecondTimeSpan>);
  const FP::NanosecondTimeDuration duration = FP::NanosecondTimeDuration::FromNanoseconds(10);
  EXPECT_EQ(duration + FP::NanosecondTimeSpan(5), FP::NanosecondTimeSpan(15));
  EXPECT_EQ(duration + FP::NanosecondTimeSpan(-25), FP::NanosecondTimeSpan(-15));
  EXPECT_EQ(FP::NanosecondTimeSpan(-25) + duration, FP::NanosecondTimeSpan(-15));
  EXPECT_EQ(FP::NanosecondTimeSpan(5) + duration, FP::NanosecondTimeSpan(15));
  EXPECT_EQ(duration - FP::NanosecondTimeSpan(25), FP::NanosecondTimeSpan(-15));
  EXPECT_EQ(duration - FP::NanosecondTimeSpan(-25), FP::NanosecondTimeSpan(35));
  EXPECT_EQ(FP::NanosecondTimeSpan(25) - duration, FP::NanosecondTimeSpan(15));
  EXPECT_EQ(FP::NanosecondTimeSpan(-25) - duration, FP::NanosecondTimeSpan(-35));
  // Outside the range, as a NanosecondTimeSpan's sum and difference
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeDuration::MaxValue() + FP::NanosecondTimeSpan(1)), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeSpan(1) + FP::NanosecondTimeDuration::MaxValue()), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeDuration::MaxValue() - FP::NanosecondTimeSpan(-1)), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeSpan::MinValue() - FP::NanosecondTimeDuration::FromNanoseconds(1)), std::overflow_error);
}

TEST(NanosecondTimeDuration, TheShorterAndTheLongerOfTwo)
{
  const FP::NanosecondTimeDuration shorter = FP::NanosecondTimeDuration::FromNanoseconds(3);
  const FP::NanosecondTimeDuration longer = FP::NanosecondTimeDuration::FromNanoseconds(4);
  EXPECT_EQ(FP::NanosecondTimeDuration::Min(shorter, longer), shorter);
  EXPECT_EQ(FP::NanosecondTimeDuration::Min(longer, shorter), shorter);
  EXPECT_EQ(FP::NanosecondTimeDuration::Min(longer, longer), longer);
  EXPECT_EQ(FP::NanosecondTimeDuration::Max(shorter, longer), longer);
  EXPECT_EQ(FP::NanosecondTimeDuration::Max(longer, shorter), longer);
  EXPECT_EQ(FP::NanosecondTimeDuration::Max(shorter, shorter), shorter);
}

TEST(NanosecondTimeDuration, ComparesByNanoseconds)
{
  const FP::NanosecondTimeDuration shorter = FP::NanosecondTimeDuration::FromNanoseconds(3);
  const FP::NanosecondTimeDuration longer = FP::NanosecondTimeDuration::FromNanoseconds(4);
  EXPECT_TRUE(shorter == FP::NanosecondTimeDuration::FromNanoseconds(3));
  EXPECT_TRUE(shorter != longer);
  EXPECT_TRUE(shorter < longer);
  EXPECT_TRUE(longer >= shorter);
  EXPECT_EQ(shorter <=> longer, std::strong_ordering::less);
  EXPECT_EQ(longer <=> shorter, std::strong_ordering::greater);
  EXPECT_EQ(longer <=> longer, std::strong_ordering::equal);
}

TEST(NanosecondTimeDuration, IsExactFromTicksAndTruncatedToThem)
{
  // A tick is 100 ns
  EXPECT_EQ(FP::NanosecondTimeDuration::FromTimeDuration(FP::TimeDuration::FromTicks(166'667)).Nanoseconds(), 16'666'700);
  EXPECT_EQ(FP::NanosecondTimeDuration::FromTimeDuration(FP::TimeDuration()), FP::NanosecondTimeDuration::Zero());
  // More than nanoseconds can hold
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeDuration::FromTimeDuration(FP::TimeDuration::MaxValue())), std::out_of_range);
  // To ticks: the whole ticks in it
  EXPECT_EQ(FP::NanosecondTimeDuration::FromNanoseconds(4'166'389).ToTimeDuration(), FP::TimeDuration::FromTicks(41'663));
  EXPECT_EQ(FP::NanosecondTimeDuration::FromNanoseconds(99).ToTimeDuration(), FP::TimeDuration::Zero());
  EXPECT_EQ(FP::NanosecondTimeDuration::FromNanoseconds(100).ToTimeDuration(), FP::TimeDuration::FromTicks(1));
  EXPECT_EQ(FP::NanosecondTimeDuration::MaxValue().ToTimeDuration().Ticks(), std::numeric_limits<int64_t>::max() / 100);
  static_assert(FP::NanosecondTimeDuration::FromTimeDuration(FP::TimeDuration::FromTicks(3)) == FP::NanosecondTimeDuration::FromNanoseconds(300));
  static_assert(FP::NanosecondTimeDuration::FromNanoseconds(399).ToTimeDuration() == FP::TimeDuration::FromTicks(3));
}
