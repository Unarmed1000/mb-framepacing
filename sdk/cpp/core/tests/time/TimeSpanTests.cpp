// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// TimeSpan is C#'s System.TimeSpan: every expected value and error here is what .NET 10 gives for the same call.
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <gtest/gtest.h>
#include <cmath>
#include <compare>
#include <cstdint>
#include <limits>
#include <stdexcept>
#include <type_traits>

namespace FP = MB::FramePacing;

namespace
{
  constexpr int64_t MaxTicks = std::numeric_limits<int64_t>::max();
  constexpr int64_t MinTicks = std::numeric_limits<int64_t>::min();
  constexpr int32_t MaxInt32 = std::numeric_limits<int32_t>::max();
  constexpr int32_t MinInt32 = std::numeric_limits<int32_t>::min();
  constexpr double NaN = std::numeric_limits<double>::quiet_NaN();
  constexpr double Infinity = std::numeric_limits<double>::infinity();
  // Read at run time: MSVC warns about a division by a constant 0 (C4723), and these tests divide by 0 on purpose
  volatile double g_zero = 0.0;

  int64_t Ticks(const FP::TimeSpan span)
  {
    return span.Ticks();
  }
}

TEST(TimeSpan, HasCSharpsUnitsAndRange)
{
  static_assert(FP::TimeSpan::NanosecondsPerTick == 100 && FP::TimeSpan::TicksPerMicrosecond == 10);
  static_assert(FP::TimeSpan::TicksPerMillisecond == 10'000 && FP::TimeSpan::TicksPerSecond == 10'000'000);
  static_assert(FP::TimeSpan::TicksPerMinute == 600'000'000 && FP::TimeSpan::TicksPerHour == 36'000'000'000);
  static_assert(FP::TimeSpan::TicksPerDay == 864'000'000'000);
  static_assert(std::is_trivially_copyable_v<FP::TimeSpan> && std::is_standard_layout_v<FP::TimeSpan> && sizeof(FP::TimeSpan) == 8);
  EXPECT_EQ(Ticks(FP::TimeSpan()), 0);
  EXPECT_EQ(Ticks(FP::TimeSpan::Zero()), 0);
  EXPECT_EQ(Ticks(FP::TimeSpan::MinValue()), MinTicks);
  EXPECT_EQ(Ticks(FP::TimeSpan::MaxValue()), MaxTicks);
  EXPECT_EQ(Ticks(FP::TimeSpan(-12)), -12);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromTicks(12)), 12);
}

TEST(TimeSpan, TheIntegerFactoriesTakeWholeUnitsWithinTheRange)
{
  EXPECT_EQ(Ticks(FP::TimeSpan::FromDays(10'675'199)), 9'223'371'936'000'000'000);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromDays(-10'675'199)), -9'223'371'936'000'000'000);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::FromDays(10'675'200)), std::out_of_range);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::FromDays(-10'675'200)), std::out_of_range);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromHours(256'204'778)), 9'223'372'008'000'000'000);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromHours(-256'204'778)), -9'223'372'008'000'000'000);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::FromHours(256'204'779)), std::out_of_range);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::FromHours(-256'204'779)), std::out_of_range);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromMinutes(int64_t{15'372'286'728})), 9'223'372'036'800'000'000);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromMinutes(int64_t{-15'372'286'728})), -9'223'372'036'800'000'000);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::FromMinutes(int64_t{15'372'286'729})), std::out_of_range);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::FromMinutes(int64_t{-15'372'286'729})), std::out_of_range);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromSeconds(int64_t{922'337'203'685})), 9'223'372'036'850'000'000);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromSeconds(int64_t{-922'337'203'685})), -9'223'372'036'850'000'000);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::FromSeconds(int64_t{922'337'203'686})), std::out_of_range);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::FromSeconds(int64_t{-922'337'203'686})), std::out_of_range);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromMilliseconds(int64_t{922'337'203'685'477})), 9'223'372'036'854'770'000);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromMilliseconds(int64_t{-922'337'203'685'477})), -9'223'372'036'854'770'000);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::FromMilliseconds(int64_t{922'337'203'685'478})), std::out_of_range);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::FromMilliseconds(int64_t{-922'337'203'685'478})), std::out_of_range);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromMicroseconds(int64_t{922'337'203'685'477'580})), 9'223'372'036'854'775'800);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromMicroseconds(int64_t{-922'337'203'685'477'580})), -9'223'372'036'854'775'800);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::FromMicroseconds(int64_t{922'337'203'685'477'581})), std::out_of_range);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::FromMicroseconds(int64_t{-922'337'203'685'477'581})), std::out_of_range);
  // Any integer type, compared by value
  EXPECT_EQ(Ticks(FP::TimeSpan::FromSeconds(5u)), 50'000'000);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromMilliseconds(int16_t{-3})), -30'000);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::FromSeconds(std::numeric_limits<uint64_t>::max())), std::out_of_range);
}

TEST(TimeSpan, TheDoubleFactoriesTruncateTowardZeroToATick)
{
  EXPECT_EQ(Ticks(FP::TimeSpan::FromSeconds(1.0 / 60)), 166'666);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromSeconds(-1.0 / 60)), -166'666);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromSeconds(1.5)), 15'000'000);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromMilliseconds(0.00009)), 0);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromMilliseconds(-0.00009)), 0);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromMilliseconds(16.6666)), 166'666);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromMicroseconds(0.15)), 1);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromMicroseconds(1.99)), 19);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromDays(0.1)), 86'400'000'000);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromHours(1 / 3.0)), 12'000'000'000);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromMinutes(2.5)), 1'500'000'000);
  // At the ends of the range: MaxValue's ticks as a double are 2^63, which gives MaxValue
  EXPECT_EQ(Ticks(FP::TimeSpan::FromSeconds(922'337'203'685.4775807)), MaxTicks);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromSeconds(922'337'203'685.477)), 9'223'372'036'854'770'688);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromSeconds(-922'337'203'685.4775808)), MinTicks);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromMilliseconds(9'223'372'036'854'775'808.0 / 10'000)), MaxTicks);
  EXPECT_EQ(Ticks(FP::TimeSpan::FromMicroseconds(-9'223'372'036'854'775'808.0 / 10)), MinTicks);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::FromSeconds(1e12)), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::FromSeconds(-1e12)), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::FromSeconds(Infinity)), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::FromSeconds(-Infinity)), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::FromSeconds(NaN)), std::invalid_argument);
}

TEST(TimeSpan, ThePartsAddUpAndMustFitTheRange)
{
  EXPECT_EQ(Ticks(FP::TimeSpan(1, 2, 3)), 37'230'000'000);
  EXPECT_EQ(Ticks(FP::TimeSpan(-1, 2, 3)), -34'770'000'000);
  EXPECT_EQ(Ticks(FP::TimeSpan(1, -2, 3, -4, 5, -6)), 793'760'049'940);
  EXPECT_EQ(Ticks(FP::TimeSpan(256'204'778, 48, 5)), 9'223'372'036'850'000'000);
  EXPECT_EQ(Ticks(FP::TimeSpan(-256'204'778, -48, -5)), -9'223'372'036'850'000'000);
  EXPECT_EQ(Ticks(FP::TimeSpan(10'675'199, 2, 48, 5)), 9'223'372'036'850'000'000);
  EXPECT_EQ(Ticks(FP::TimeSpan(10'675'199, 2, 48, 5, 477)), 9'223'372'036'854'770'000);
  EXPECT_EQ(Ticks(FP::TimeSpan(10'675'199, 2, 48, 5, 477, 580)), 9'223'372'036'854'775'800);
  EXPECT_EQ(Ticks(FP::TimeSpan(-10'675'199, -2, -48, -5, -477, -580)), -9'223'372'036'854'775'800);
  EXPECT_THROW(FP::TimeSpan(256'204'778, 48, 6), std::out_of_range);
  EXPECT_THROW(FP::TimeSpan(-256'204'778, -48, -6), std::out_of_range);
  EXPECT_THROW(FP::TimeSpan(MaxInt32, MaxInt32, MaxInt32), std::out_of_range);
  EXPECT_THROW(FP::TimeSpan(10'675'199, 2, 48, 6), std::out_of_range);
  EXPECT_THROW(FP::TimeSpan(10'675'199, 2, 48, 5, 478), std::out_of_range);
  EXPECT_THROW(FP::TimeSpan(10'675'199, 2, 48, 5, 477, 581), std::out_of_range);
  EXPECT_THROW(FP::TimeSpan(-10'675'199, -2, -48, -5, -477, -581), std::out_of_range);
  EXPECT_THROW(FP::TimeSpan(MaxInt32, MaxInt32, MaxInt32, MaxInt32, MaxInt32, MaxInt32), std::out_of_range);
  EXPECT_THROW(FP::TimeSpan(MinInt32, MinInt32, MinInt32, MinInt32, MinInt32, MinInt32), std::out_of_range);
}

TEST(TimeSpan, ComponentsAreTruncatedTowardZeroWithTheSignOfTheSpan)
{
  const FP::TimeSpan span(123'456'789'012'345);
  EXPECT_EQ(span.Days(), 142);
  EXPECT_EQ(span.Hours(), 21);
  EXPECT_EQ(span.Minutes(), 21);
  EXPECT_EQ(span.Seconds(), 18);
  EXPECT_EQ(span.Milliseconds(), 901);
  EXPECT_EQ(span.Microseconds(), 234);
  EXPECT_EQ(span.Nanoseconds(), 500);

  const FP::TimeSpan negative = FP::TimeSpan(-1, -2, -3, -4, -5, -6) - FP::TimeSpan(7);
  EXPECT_EQ(Ticks(negative), -937'840'050'067);
  EXPECT_EQ(negative.Days(), -1);
  EXPECT_EQ(negative.Hours(), -2);
  EXPECT_EQ(negative.Minutes(), -3);
  EXPECT_EQ(negative.Seconds(), -4);
  EXPECT_EQ(negative.Milliseconds(), -5);
  EXPECT_EQ(negative.Microseconds(), -6);
  EXPECT_EQ(negative.Nanoseconds(), -700);

  const FP::TimeSpan max = FP::TimeSpan::MaxValue();
  EXPECT_EQ(max.Days(), 10'675'199);
  EXPECT_EQ(max.Hours(), 2);
  EXPECT_EQ(max.Minutes(), 48);
  EXPECT_EQ(max.Seconds(), 5);
  EXPECT_EQ(max.Milliseconds(), 477);
  EXPECT_EQ(max.Microseconds(), 580);
  EXPECT_EQ(max.Nanoseconds(), 700);
  EXPECT_EQ(FP::TimeSpan::MinValue().Nanoseconds(), -800);
}

TEST(TimeSpan, TotalsAreTheSpanInOneUnit)
{
  // Bit for bit what .NET 10 gives
  const FP::TimeSpan span(123'456'789'012'345);
  EXPECT_EQ(span.TotalDays(), 142.88980209762153);
  EXPECT_EQ(span.TotalHours(), 3429.355250342917);
  EXPECT_EQ(span.TotalMinutes(), 205761.315020575);
  EXPECT_EQ(span.TotalSeconds(), 12345678.9012345);
  EXPECT_EQ(span.TotalMilliseconds(), 12345678901.2345);
  EXPECT_EQ(span.TotalMicroseconds(), 12345678901234.5);
  EXPECT_EQ(span.TotalNanoseconds(), 12345678901234500.0);

  const FP::TimeSpan negative(-937'840'050'067);
  EXPECT_EQ(negative.TotalDays(), -1.0854630209108795);
  EXPECT_EQ(negative.TotalHours(), -26.051112501861112);
  EXPECT_EQ(negative.TotalMinutes(), -1563.0667501116666);
  EXPECT_EQ(negative.TotalSeconds(), -93784.0050067);
  EXPECT_EQ(negative.TotalMilliseconds(), -93784005.0067);
  EXPECT_EQ(negative.TotalMicroseconds(), -93784005006.7);
  EXPECT_EQ(negative.TotalNanoseconds(), -93784005006700.0);

  // As C#, TotalMilliseconds stays within the whole milliseconds of the range
  EXPECT_EQ(FP::TimeSpan::MaxValue().TotalMilliseconds(), 922'337'203'685'477.0);
  EXPECT_EQ(FP::TimeSpan::MinValue().TotalMilliseconds(), -922'337'203'685'477.0);
  EXPECT_EQ(FP::TimeSpan::MaxValue().TotalNanoseconds(), 9.223372036854776e+20);
  EXPECT_EQ(FP::TimeSpan::MaxValue().TotalDays(), 10675199.116730064);
}

TEST(TimeSpan, AddingAndSubtractingThrowOutsideTheRange)
{
  EXPECT_EQ(Ticks(FP::TimeSpan(1) + FP::TimeSpan(2)), 3);
  EXPECT_EQ(Ticks(FP::TimeSpan(5) + FP::TimeSpan(-3)), 2);
  EXPECT_EQ(Ticks(FP::TimeSpan(5) + FP::TimeSpan()), 5);
  EXPECT_EQ(Ticks(FP::TimeSpan(5) - FP::TimeSpan(3)), 2);
  EXPECT_EQ(Ticks(FP::TimeSpan(5) - FP::TimeSpan(-3)), 8);
  EXPECT_EQ(Ticks(FP::TimeSpan(5) - FP::TimeSpan()), 5);
  EXPECT_EQ(Ticks(FP::TimeSpan(-1) - FP::TimeSpan::MinValue()), MaxTicks);
  EXPECT_EQ(Ticks(FP::TimeSpan::MaxValue() + FP::TimeSpan::MinValue()), -1);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::MaxValue() + FP::TimeSpan(1)), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::MinValue() + FP::TimeSpan(-1)), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::MinValue() - FP::TimeSpan(1)), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan() - FP::TimeSpan::MinValue()), std::overflow_error);

  FP::TimeSpan span(10);
  span += FP::TimeSpan(5);
  EXPECT_EQ(Ticks(span), 15);
  span -= FP::TimeSpan(20);
  EXPECT_EQ(Ticks(span), -5);
}

TEST(TimeSpan, MinValueHasNoPositiveCounterpart)
{
  EXPECT_EQ(Ticks(FP::TimeSpan(5).Negate()), -5);
  EXPECT_EQ(Ticks(-FP::TimeSpan(-5)), 5);
  EXPECT_EQ(Ticks(+FP::TimeSpan::MinValue()), MinTicks);
  EXPECT_EQ(Ticks(FP::TimeSpan::MaxValue().Negate()), -MaxTicks);
  EXPECT_EQ(Ticks(FP::TimeSpan(-5).Duration()), 5);
  EXPECT_EQ(Ticks(FP::TimeSpan(5).Duration()), 5);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::MinValue().Negate()), std::overflow_error);
  EXPECT_THROW(static_cast<void>(-FP::TimeSpan::MinValue()), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::MinValue().Duration()), std::overflow_error);
}

TEST(TimeSpan, MultiplyingAndDividingRoundToTheNearestTickATieToTheEvenOne)
{
  EXPECT_EQ(Ticks(FP::TimeSpan(3) * 0.5), 2);
  EXPECT_EQ(Ticks(FP::TimeSpan(5) * 0.5), 2);
  EXPECT_EQ(Ticks(FP::TimeSpan(-3) * 0.5), -2);
  EXPECT_EQ(Ticks(FP::TimeSpan(-5) * 0.5), -2);
  EXPECT_EQ(Ticks(FP::TimeSpan(7) * 0.3), 2);
  EXPECT_EQ(Ticks(FP::TimeSpan(7) * 0.1), 1);
  EXPECT_EQ(Ticks(FP::TimeSpan(-7) * 0.1), -1);
  EXPECT_EQ(Ticks(0.5 * FP::TimeSpan(3)), 2);
  EXPECT_EQ(Ticks(FP::TimeSpan(3) / 2.0), 2);
  EXPECT_EQ(Ticks(FP::TimeSpan(5) / 2.0), 2);
  EXPECT_EQ(Ticks(FP::TimeSpan(7) / 2.0), 4);
  EXPECT_EQ(Ticks(FP::TimeSpan(-7) / 2.0), -4);
  EXPECT_EQ(Ticks(FP::TimeSpan(10) / 3.0), 3);
  EXPECT_EQ(Ticks(FP::TimeSpan(-10) / 3.0), -3);
  // Beyond 2^53 a double holds no fraction: the product is whole already
  EXPECT_EQ(Ticks(FP::TimeSpan((int64_t{1} << 53) + 1) * 1.0), int64_t{1} << 53);
  EXPECT_EQ(Ticks(FP::TimeSpan::MaxValue() * 1.0), MaxTicks);
  EXPECT_EQ(Ticks(FP::TimeSpan::MinValue() * 1.0), MinTicks);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::MaxValue() * 2.0), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan::MaxValue() / 0.5), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan(1) / g_zero), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan(-1) / g_zero), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan() / g_zero), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan() * Infinity), std::overflow_error);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan(1) * NaN), std::invalid_argument);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan(1) / NaN), std::invalid_argument);

  FP::TimeSpan span(10);
  span *= 2.5;
  EXPECT_EQ(Ticks(span), 25);
  span /= 10.0;
  EXPECT_EQ(Ticks(span), 2);
}

TEST(TimeSpan, TheRatioOfTwoSpansIsADouble)
{
  EXPECT_EQ(FP::TimeSpan(7) / FP::TimeSpan(2), 3.5);
  const FP::TimeSpan zero(static_cast<int64_t>(g_zero));
  EXPECT_EQ(FP::TimeSpan(1) / zero, Infinity);
  EXPECT_TRUE(std::isnan(FP::TimeSpan() / zero));
}

TEST(TimeSpan, ComparesByTicks)
{
  const FP::TimeSpan shorter(-3);
  const FP::TimeSpan longer(4);
  EXPECT_TRUE(shorter == FP::TimeSpan(-3));
  EXPECT_TRUE(shorter != longer);
  EXPECT_TRUE(shorter < longer);
  EXPECT_TRUE(shorter <= longer);
  EXPECT_TRUE(longer > shorter);
  EXPECT_TRUE(longer >= shorter);
  EXPECT_EQ(shorter <=> longer, std::strong_ordering::less);
  EXPECT_EQ(longer <=> longer, std::strong_ordering::equal);
}

TEST(TimeSpan, WorksInConstantExpressions)
{
  static_assert(FP::TimeSpan::FromMilliseconds(16).Ticks() == 160'000);
  static_assert(FP::TimeSpan::FromSeconds(0.5).Ticks() == 5'000'000);
  static_assert(FP::TimeSpan(0, 0, 1).Ticks() == FP::TimeSpan::TicksPerSecond);
  static_assert((FP::TimeSpan(3) * 0.5).Ticks() == 2 && (FP::TimeSpan(10) - FP::TimeSpan(4)).Duration().Ticks() == 6);
  static_assert(FP::TimeSpan(5) > FP::TimeSpan::Zero());
}
