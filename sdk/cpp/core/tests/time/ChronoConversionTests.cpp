// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// The optional std::chrono conversions (core/time/ChronoConversion.hpp).
#include <mb/framepacing/core/time/ChronoConversion.hpp>
#include <mb/framepacing/core/time/TickCount32.hpp>
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeDuration.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <gtest/gtest.h>
#include <chrono>
#include <cstdint>
#include <stdexcept>

namespace FP = MB::FramePacing;

TEST(ChronoConversion, ATickDurationIsInTicks)
{
  // Coarser durations convert exactly and implicitly
  EXPECT_EQ(FP::TickDuration{std::chrono::milliseconds{1}}.count(), FP::TimeSpan::TicksPerMillisecond);
  EXPECT_EQ(FP::TickDuration{std::chrono::seconds{-2}}.count(), -2 * FP::TimeSpan::TicksPerSecond);
  // Finer ones are rounded down to the tick they are in on purpose
  EXPECT_EQ(std::chrono::floor<FP::TickDuration>(std::chrono::nanoseconds{-1}).count(), -1);
}

TEST(ChronoConversion, SpansConvertExactly)
{
  EXPECT_EQ(FP::ToTimeSpan(std::chrono::milliseconds{16}), FP::TimeSpan::FromMilliseconds(16));
  EXPECT_EQ(FP::ToTickDuration(FP::TimeSpan(-7)), FP::TickDuration{-7});
  EXPECT_EQ(FP::ToTimeSpan32(FP::TickDuration{166'667}), FP::TimeSpan32(166'667u));
  EXPECT_THROW(static_cast<void>(FP::ToTimeSpan32(FP::TickDuration{-1})), std::out_of_range);
  EXPECT_THROW(static_cast<void>(FP::ToTimeSpan32(std::chrono::minutes{8})), std::out_of_range);
  EXPECT_EQ(FP::ToTickDuration(FP::TimeSpan32::MaxValue()), FP::TickDuration{4'294'967'295});
  // A duration is never negative: a negative std::chrono one becomes zero
  EXPECT_EQ(FP::ToTimeDuration(FP::TickDuration{166'667}), FP::TimeDuration::FromTicks(166'667));
  EXPECT_EQ(FP::ToTimeDuration(std::chrono::milliseconds{16}), FP::TimeDuration::FromTicks(160'000));
  EXPECT_EQ(FP::ToTimeDuration(FP::TickDuration{-1}), FP::TimeDuration::Zero());
  EXPECT_EQ(FP::ToTickDuration(FP::TimeDuration::FromTicks(166'667)), FP::TickDuration{166'667});
  EXPECT_EQ(FP::ToTickDuration(FP::TimeDuration::MaxValue()), FP::TickDuration::max());
  static_assert(FP::ToTimeSpan(FP::ToTickDuration(FP::TimeSpan(42))) == FP::TimeSpan(42));
}

TEST(ChronoConversion, TimePointsBecomeCountsRoundedDown)
{
  using SteadyNanoseconds = std::chrono::time_point<std::chrono::steady_clock, std::chrono::nanoseconds>;
  EXPECT_EQ(FP::ToTickCount64(SteadyNanoseconds{std::chrono::nanoseconds{150}}), FP::TickCount64(1));
  EXPECT_EQ(FP::ToTickCount64(SteadyNanoseconds{std::chrono::nanoseconds{-1}}), FP::TickCount64(-1));
  EXPECT_EQ(FP::ToTickCount32(SteadyNanoseconds{std::chrono::nanoseconds{((int64_t{1} << 32) + 3) * 100}}), FP::TickCount32(3u));
  // And back: a count as a time point of the clock it came from
  const FP::TickCount64 count(123'456);
  const auto timePoint = FP::ToTimePoint<std::chrono::steady_clock>(count);
  EXPECT_EQ(timePoint.time_since_epoch().count(), 123'456);
  EXPECT_EQ(FP::ToTickCount64(timePoint), count);
  // A real clock
  const FP::TickCount64 before = FP::ToTickCount64(std::chrono::steady_clock::now());
  EXPECT_GE(FP::ToTickCount64(std::chrono::steady_clock::now()), before);
}

TEST(ChronoConversion, ADateTimeIsTicksSinceYearOne)
{
  static_assert(FP::ToDateTimeTicks(std::chrono::system_clock::time_point{}) == 621'355'968'000'000'000);
  // 2026-01-01T00:00:00Z = unix 1767225600 s; C# new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks = 639028224000000000
  const auto newYear = std::chrono::system_clock::time_point{std::chrono::seconds{1'767'225'600}};
  EXPECT_EQ(FP::ToDateTimeTicks(newYear), 639'028'224'000'000'000);
}

TEST(ChronoConversion, ADateTimeIsRoundedDownToItsTick)
{
  // A clock finer than the tick (system_clock counts nanoseconds on Linux), before and after its epoch: the tick the time is in, as
  // ToTickCount64
  using SystemNanoseconds = std::chrono::time_point<std::chrono::system_clock, std::chrono::nanoseconds>;
  constexpr int64_t Epoch = 621'355'968'000'000'000;
  static_assert(FP::ToDateTimeTicks(SystemNanoseconds{std::chrono::nanoseconds{150}}) == Epoch + 1);
  EXPECT_EQ(FP::ToDateTimeTicks(SystemNanoseconds{std::chrono::nanoseconds{150}}), Epoch + 1);
  EXPECT_EQ(FP::ToDateTimeTicks(SystemNanoseconds{std::chrono::nanoseconds{-100}}), Epoch - 1);
  EXPECT_EQ(FP::ToDateTimeTicks(SystemNanoseconds{std::chrono::nanoseconds{-150}}), Epoch - 2) << "1969-12-31T23:59:59.99999985";
  EXPECT_EQ(FP::ToDateTimeTicks(SystemNanoseconds{std::chrono::nanoseconds{-1}}), Epoch - 1);
  // A coarser clock converts exactly
  using SystemSeconds = std::chrono::time_point<std::chrono::system_clock, std::chrono::seconds>;
  EXPECT_EQ(FP::ToDateTimeTicks(SystemSeconds{std::chrono::seconds{-1}}), Epoch - FP::TimeSpan::TicksPerSecond);
}
