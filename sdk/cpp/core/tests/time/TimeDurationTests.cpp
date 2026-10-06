// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// TimeDuration: a length of time that is never negative. It holds a TimeSpan that is zero or more, and what its arithmetic gives is
// a duration where the result can not be negative and a TimeSpan where it can.
#include <mb/framepacing/core/time/TimeDuration.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <gtest/gtest.h>
#include <compare>
#include <cstdint>
#include <limits>
#include <stdexcept>
#include <type_traits>

namespace FP = MB::FramePacing;

TEST(TimeDuration, IsNeverNegative)
{
  static_assert(std::is_trivially_copyable_v<FP::TimeDuration> && std::is_standard_layout_v<FP::TimeDuration> && sizeof(FP::TimeDuration) == 8);
  EXPECT_EQ(FP::TimeDuration().Ticks(), 0);
  EXPECT_EQ(FP::TimeDuration::Zero().Ticks(), 0);
  EXPECT_EQ(FP::TimeDuration(FP::TimeSpan(166'667)).Ticks(), 166'667);
  EXPECT_EQ(FP::TimeDuration(FP::TimeSpan()).Ticks(), 0);
  // A negative value becomes zero
  EXPECT_EQ(FP::TimeDuration(FP::TimeSpan(-1)), FP::TimeDuration::Zero());
  EXPECT_EQ(FP::TimeDuration(FP::TimeSpan::MinValue()), FP::TimeDuration::Zero());
  EXPECT_EQ(FP::TimeDuration(FP::TimeSpan::MaxValue()), FP::TimeDuration::MaxValue());
  EXPECT_EQ(FP::TimeDuration::MaxValue().Ticks(), std::numeric_limits<int64_t>::max());
  static_assert(FP::TimeDuration(FP::TimeSpan(-5)) == FP::TimeDuration());
}

TEST(TimeDuration, IsMadeFromTicksASpanOfTheMarkersAndUnchecked)
{
  EXPECT_EQ(FP::TimeDuration::FromTicks(80'000).Ticks(), 80'000);
  EXPECT_EQ(FP::TimeDuration::FromTicks(0).Ticks(), 0);
  EXPECT_EQ(FP::TimeDuration::FromTicks(-80'000).Ticks(), 0);
  EXPECT_EQ(FP::TimeDuration::FromTicks(std::numeric_limits<int64_t>::min()).Ticks(), 0);
  // Every 32-bit interval of the marker is a duration
  EXPECT_EQ(FP::TimeDuration::From(FP::TimeSpan32()).Ticks(), 0);
  EXPECT_EQ(FP::TimeDuration::From(FP::TimeSpan32(166'667u)), FP::TimeDuration::FromTicks(166'667));
  EXPECT_EQ(FP::TimeDuration::From(FP::TimeSpan32::MaxValue()).Ticks(), 4'294'967'295);
  // For a value that is known not to be negative
  EXPECT_EQ(FP::TimeDuration::UncheckedCreate(FP::TimeSpan(7)).Ticks(), 7);
  EXPECT_EQ(FP::TimeDuration::UncheckedCreate(FP::TimeSpan()), FP::TimeDuration::Zero());
  static_assert(FP::TimeDuration::UncheckedCreate(FP::TimeSpan(5)) == FP::TimeDuration::FromTicks(5));
  static_assert(FP::TimeDuration::From(FP::TimeSpan32(5u)) == FP::TimeDuration::FromTicks(5));
}

TEST(TimeDuration, GivesItsValueAsASpanAndAsTicks)
{
  const FP::TimeDuration duration = FP::TimeDuration::FromTicks(166'667);
  EXPECT_EQ(duration.Value(), FP::TimeSpan(166'667));
  EXPECT_EQ(duration.Ticks(), 166'667);
  EXPECT_EQ(duration.UnsignedTicks(), 166'667u);
  EXPECT_EQ(FP::TimeDuration::MaxValue().Value(), FP::TimeSpan::MaxValue());
  EXPECT_EQ(FP::TimeDuration::MaxValue().UnsignedTicks(), 9'223'372'036'854'775'807u);
  EXPECT_EQ(FP::TimeDuration().UnsignedTicks(), 0u);
}

TEST(TimeDuration, TwoDurationsAddUpToADuration)
{
  FP::TimeDuration duration = FP::TimeDuration::FromTicks(100);
  duration += FP::TimeDuration::FromTicks(23);
  EXPECT_EQ(duration.Ticks(), 123);
  duration += FP::TimeDuration::Zero();
  EXPECT_EQ(duration.Ticks(), 123);
  const FP::TimeDuration sum = FP::TimeDuration::FromTicks(5) + FP::TimeDuration::FromTicks(6);
  EXPECT_EQ(sum, FP::TimeDuration::FromTicks(11));
  // Outside the range, as a TimeSpan's sum
  EXPECT_THROW(duration += FP::TimeDuration::MaxValue(), std::overflow_error);
  EXPECT_EQ(duration.Ticks(), 123);
  EXPECT_THROW(static_cast<void>(FP::TimeDuration::MaxValue() + FP::TimeDuration::FromTicks(1)), std::overflow_error);
  EXPECT_EQ((FP::TimeDuration::MaxValue() + FP::TimeDuration::Zero()), FP::TimeDuration::MaxValue());
  static_assert(FP::TimeDuration::FromTicks(2) + FP::TimeDuration::FromTicks(3) == FP::TimeDuration::FromTicks(5));
}

TEST(TimeDuration, ADurationTakenFromItStopsAtZero)
{
  FP::TimeDuration duration = FP::TimeDuration::FromTicks(100);
  duration -= FP::TimeDuration::FromTicks(30);
  EXPECT_EQ(duration.Ticks(), 70);
  duration -= FP::TimeDuration::FromTicks(70);
  EXPECT_EQ(duration.Ticks(), 0);
  duration = FP::TimeDuration::FromTicks(5);
  duration -= FP::TimeDuration::FromTicks(6);
  EXPECT_EQ(duration, FP::TimeDuration::Zero());
  duration -= FP::TimeDuration::MaxValue();
  EXPECT_EQ(duration, FP::TimeDuration::Zero());
  duration = FP::TimeDuration::MaxValue();
  duration -= FP::TimeDuration::MaxValue();
  EXPECT_EQ(duration, FP::TimeDuration::Zero());
}

TEST(TimeDuration, TheDifferenceOfTwoDurationsIsASpanThatCanBeNegative)
{
  static_assert(std::is_same_v<decltype(FP::TimeDuration() - FP::TimeDuration()), FP::TimeSpan>);
  EXPECT_EQ(FP::TimeDuration::FromTicks(10) - FP::TimeDuration::FromTicks(4), FP::TimeSpan(6));
  EXPECT_EQ(FP::TimeDuration::FromTicks(4) - FP::TimeDuration::FromTicks(10), FP::TimeSpan(-6));
  EXPECT_EQ(FP::TimeDuration::FromTicks(4) - FP::TimeDuration::FromTicks(4), FP::TimeSpan());
  // The whole range fits: the largest duration from none, and none from the largest
  EXPECT_EQ(FP::TimeDuration::Zero() - FP::TimeDuration::MaxValue(), FP::TimeSpan(-std::numeric_limits<int64_t>::max()));
  EXPECT_EQ(FP::TimeDuration::MaxValue() - FP::TimeDuration::Zero(), FP::TimeSpan::MaxValue());
}

TEST(TimeDuration, ADurationAndASpanGiveASpan)
{
  static_assert(std::is_same_v<decltype(FP::TimeDuration() + FP::TimeSpan()), FP::TimeSpan>);
  static_assert(std::is_same_v<decltype(FP::TimeSpan() + FP::TimeDuration()), FP::TimeSpan>);
  static_assert(std::is_same_v<decltype(FP::TimeDuration() - FP::TimeSpan()), FP::TimeSpan>);
  static_assert(std::is_same_v<decltype(FP::TimeSpan() - FP::TimeDuration()), FP::TimeSpan>);
  const FP::TimeDuration duration = FP::TimeDuration::FromTicks(10);
  EXPECT_EQ(duration + FP::TimeSpan(5), FP::TimeSpan(15));
  EXPECT_EQ(duration + FP::TimeSpan(-25), FP::TimeSpan(-15));
  EXPECT_EQ(FP::TimeSpan(-25) + duration, FP::TimeSpan(-15));
  EXPECT_EQ(FP::TimeSpan(5) + duration, FP::TimeSpan(15));
  EXPECT_EQ(duration - FP::TimeSpan(25), FP::TimeSpan(-15));
  EXPECT_EQ(duration - FP::TimeSpan(-25), FP::TimeSpan(35));
  EXPECT_EQ(FP::TimeSpan(25) - duration, FP::TimeSpan(15));
  EXPECT_EQ(FP::TimeSpan(-25) - duration, FP::TimeSpan(-35));
  // Outside the range, as a TimeSpan's sum and difference
  EXPECT_THROW(static_cast<void>(FP::TimeDuration::MaxValue() + FP::TimeSpan(1)), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan(1) + FP::TimeDuration::MaxValue()), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::TimeDuration::MaxValue() - FP::TimeSpan(-1)), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::MinValue() - FP::TimeDuration::FromTicks(1)), std::overflow_error);
}

TEST(TimeDuration, TimesAndDividedByACountIsADuration)
{
  static_assert(std::is_same_v<decltype(FP::TimeDuration() * uint32_t{2}), FP::TimeDuration>);
  static_assert(std::is_same_v<decltype(FP::TimeDuration() / uint32_t{2}), FP::TimeDuration>);
  const FP::TimeDuration refresh = FP::TimeDuration::FromTicks(166'667);
  EXPECT_EQ(refresh * 3u, FP::TimeDuration::FromTicks(500'001));
  EXPECT_EQ(3u * refresh, FP::TimeDuration::FromTicks(500'001));
  EXPECT_EQ(refresh * 0u, FP::TimeDuration::Zero());
  EXPECT_EQ(refresh * 1u, refresh);
  EXPECT_EQ(FP::TimeDuration::FromTicks(500'001) / 3u, refresh);
  EXPECT_EQ(refresh / 1u, refresh);
  // As a TimeSpan divides: rounded to the nearest tick, a tie to the even one
  EXPECT_EQ(FP::TimeDuration::FromTicks(10) / 4u, FP::TimeDuration::FromTicks(2));
  EXPECT_EQ(FP::TimeDuration::FromTicks(14) / 4u, FP::TimeDuration::FromTicks(4));
  EXPECT_EQ(FP::TimeDuration::FromTicks(11) / 4u, FP::TimeDuration::FromTicks(3));
  EXPECT_EQ(FP::TimeDuration::Zero() / 7u, FP::TimeDuration::Zero());
  // Outside the range, and a divisor of zero
  EXPECT_THROW(static_cast<void>(FP::TimeDuration::MaxValue() * 2u), std::overflow_error);
  EXPECT_THROW(static_cast<void>(2u * FP::TimeDuration::MaxValue()), std::overflow_error);
  EXPECT_THROW(static_cast<void>(refresh / 0u), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::TimeDuration::Zero() / 0u), std::overflow_error);
  static_assert(FP::TimeDuration::FromTicks(7) * 3u == FP::TimeDuration::FromTicks(21));
  static_assert(FP::TimeDuration::FromTicks(21) / 3u == FP::TimeDuration::FromTicks(7));
}

TEST(TimeDuration, TheShorterAndTheLongerOfTwo)
{
  const FP::TimeDuration shorter = FP::TimeDuration::FromTicks(3);
  const FP::TimeDuration longer = FP::TimeDuration::FromTicks(4);
  EXPECT_EQ(FP::TimeDuration::Min(shorter, longer), shorter);
  EXPECT_EQ(FP::TimeDuration::Min(longer, shorter), shorter);
  EXPECT_EQ(FP::TimeDuration::Min(longer, longer), longer);
  EXPECT_EQ(FP::TimeDuration::Max(shorter, longer), longer);
  EXPECT_EQ(FP::TimeDuration::Max(longer, shorter), longer);
  EXPECT_EQ(FP::TimeDuration::Max(shorter, shorter), shorter);
}

TEST(TimeDuration, ComparesByTicks)
{
  const FP::TimeDuration shorter = FP::TimeDuration::FromTicks(3);
  const FP::TimeDuration longer = FP::TimeDuration::FromTicks(4);
  EXPECT_TRUE(shorter == FP::TimeDuration::FromTicks(3));
  EXPECT_TRUE(shorter != longer);
  EXPECT_TRUE(shorter < longer);
  EXPECT_TRUE(longer >= shorter);
  EXPECT_EQ(shorter <=> longer, std::strong_ordering::less);
  EXPECT_EQ(longer <=> shorter, std::strong_ordering::greater);
  EXPECT_EQ(longer <=> longer, std::strong_ordering::equal);
}
