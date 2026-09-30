// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// The core module: the library version, the tick conversions and Rectangle.
#include <mb/framepacing/Core.hpp>
#include <mb/framepacing/core/Version.hpp>
#include <gtest/gtest.h>
#include <chrono>
#include <cstdint>
#include <limits>
#include <string>
#include <string_view>
#include "SteadyClock.hpp"

namespace FP = MB::FramePacing;

TEST(Version, MatchesTheVersionFile)
{
  EXPECT_EQ(FP::VersionString, std::string_view(MB_FRAMEPACING_EXPECTED_VERSION));
  const std::string numbers = std::to_string(FP::VersionMajor) + "." + std::to_string(FP::VersionMinor) + "." + std::to_string(FP::VersionPatch);
  EXPECT_EQ(FP::VersionPrerelease.empty() ? numbers : numbers + "-" + std::string(FP::VersionPrerelease), std::string(FP::VersionString));
}

TEST(Version, TheLinkedLibraryHasTheVersionOfTheHeader)
{
  const FP::LibraryVersion version = FP::GetLibraryVersion();
  EXPECT_EQ(version.Text, FP::VersionString);
  EXPECT_EQ(version.Major, FP::VersionMajor);
  EXPECT_EQ(version.Minor, FP::VersionMinor);
  EXPECT_EQ(version.Patch, FP::VersionPatch);
  EXPECT_EQ(version.Prerelease, FP::VersionPrerelease);
}

TEST(Ticks, NanosecondsRoundDownToTheTickTheyAreIn)
{
  static_assert(FP::NanosecondsToTicks(0) == 0);
  static_assert(FP::NanosecondsToTicks(99) == 0);
  static_assert(FP::NanosecondsToTicks(100) == 1);
  static_assert(FP::NanosecondsToTicks(16'683'350) == 166'833);
  static_assert(FP::NanosecondsToTicks(-1) == -1);
  static_assert(FP::NanosecondsToTicks(-100) == -1);
  static_assert(FP::NanosecondsToTicks(-101) == -2);
  static_assert(FP::TicksToNanoseconds(166'667) == 16'666'700);
  EXPECT_EQ(FP::NanosecondsToTicks(std::numeric_limits<int64_t>::max()), std::numeric_limits<int64_t>::max() / 100);
}

TEST(Ticks, ACounterConvertsExactlyAtAnyValue)
{
  // A 10 MHz counter (QueryPerformanceFrequency on current Windows) is in ticks already
  static_assert(FP::CounterToTicks(123'456'789, 10'000'000) == 123'456'789);
  // A 3 GHz counter: one second and a third of a tick
  static_assert(FP::CounterToTicks(3'000'000'100, 3'000'000'000) == FP::TicksPerSecond);
  static_assert(FP::CounterToTicks(3'000'000'300, 3'000'000'000) == FP::TicksPerSecond + 1);
  // Nanoseconds as a counter agree with NanosecondsToTicks, negative values included
  static_assert(FP::CounterToTicks(-150, 1'000'000'000) == FP::NanosecondsToTicks(-150));
  // A counter near its limit does not overflow: 2^63 - 1 at 3 GHz is about 97 years
  constexpr int64_t Max = std::numeric_limits<int64_t>::max();
  constexpr int64_t Frequency = 3'000'000'000;
  EXPECT_EQ(FP::CounterToTicks(Max, Frequency), ((Max / Frequency) * FP::TicksPerSecond) + (((Max % Frequency) * FP::TicksPerSecond) / Frequency));
  // The fastest counter it takes, at its limit too
  static_assert(FP::CounterToTicks(FP::MaxCounterFrequency, FP::MaxCounterFrequency) == FP::TicksPerSecond);
  EXPECT_EQ(FP::CounterToTicks(Max, FP::MaxCounterFrequency), ((Max / FP::MaxCounterFrequency) * FP::TicksPerSecond) +
                                                                (((Max % FP::MaxCounterFrequency) * FP::TicksPerSecond) / FP::MaxCounterFrequency));
}

TEST(Ticks, ACounterFrequencyOutsideItsRangeIsAsserted)
{
#ifdef NDEBUG
  // Without asserts it gives 0, unknown
  EXPECT_EQ(FP::CounterToTicks(123, 0), 0);
  EXPECT_EQ(FP::CounterToTicks(123, -10'000'000), 0);
  EXPECT_EQ(FP::CounterToTicks(123, FP::MaxCounterFrequency + 1), 0);
#elif GTEST_HAS_DEATH_TEST
  EXPECT_DEATH(static_cast<void>(FP::CounterToTicks(123, 0)), "");
  EXPECT_DEATH(static_cast<void>(FP::CounterToTicks(123, -10'000'000)), "");
  EXPECT_DEATH(static_cast<void>(FP::CounterToTicks(123, FP::MaxCounterFrequency + 1)), "");
#else
  GTEST_SKIP() << "asserts are on and death tests are not available";
#endif
}

TEST(Ticks, ADateTimeIsTicksSinceYearOne)
{
  static_assert(FP::ToDateTimeTicks(std::chrono::system_clock::time_point{}) == FP::UnixEpochDateTimeTicks);
  // 2026-01-01T00:00:00Z = unix 1767225600 s; C# new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks = 639028224000000000
  const auto newYear = std::chrono::system_clock::time_point{std::chrono::seconds{1'767'225'600}};
  EXPECT_EQ(FP::ToDateTimeTicks(newYear), 639'028'224'000'000'000);
  EXPECT_EQ(FP::TickDuration{std::chrono::milliseconds{1}}.count(), FP::TicksPerMillisecond);
}

TEST(SteadyClock, NeverGoesBack)
{
  int64_t previous = FP::SteadyClock::NowTicks();
  for (int i = 0; i < 1000; ++i)
  {
    const int64_t now = FP::SteadyClock::NowTicks();
    EXPECT_GE(now, previous);
    previous = now;
  }
}

TEST(Rectangle, ItsEdgesFollowFromItsPositionAndSize)
{
  constexpr FP::Rectangle Rect(10, 20, 30, 40);
  static_assert(Rect.X() == 10 && Rect.Y() == 20 && Rect.Width() == 30 && Rect.Height() == 40);
  static_assert(Rect.Left() == 10 && Rect.Top() == 20 && Rect.Right() == 40 && Rect.Bottom() == 60);
  static_assert(Rect.Contains(10, 20) && Rect.Contains(39, 59));
  static_assert(!Rect.Contains(40, 20) && !Rect.Contains(10, 60) && !Rect.Contains(9, 20));
  static_assert(FP::Rectangle::FromLeftTopRightBottom(10, 20, 40, 60) == Rect);
  static_assert(FP::Rectangle() == FP::Rectangle(0, 0, 0, 0));
}

TEST(Rectangle, ItIsAlwaysValid)
{
  // A negative size is 0
  static_assert(FP::Rectangle(5, 6, -3, -4) == FP::Rectangle(5, 6, 0, 0));
  static_assert(FP::Rectangle::FromLeftTopRightBottom(40, 60, 10, 20) == FP::Rectangle(40, 60, 0, 0));
  // A size that would put an edge beyond int32 is cut to fit
  constexpr int32_t Max = std::numeric_limits<int32_t>::max();
  static_assert(FP::Rectangle(Max - 10, Max - 5, 100, 100).Right() == Max);
  static_assert(FP::Rectangle(Max - 10, Max - 5, 100, 100).Bottom() == Max);
  static_assert(FP::Rectangle(-Max, 0, Max, 1).Right() == 0);
  static_assert(FP::Rectangle::FromLeftTopRightBottom(std::numeric_limits<int32_t>::min(), 0, Max, 1).Width() == Max);
  EXPECT_EQ(FP::Rectangle(0, 0, 7, -1).Height(), 0);
}
