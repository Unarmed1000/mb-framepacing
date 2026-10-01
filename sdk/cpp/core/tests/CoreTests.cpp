// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// The core module: the library version, Point and Rectangle (the time types: tests/time).
#include <mb/framepacing/core/GetLibraryVersion.hpp>
#include <mb/framepacing/core/LibraryVersion.hpp>
#include <mb/framepacing/core/Point.hpp>
#include <mb/framepacing/core/Rectangle.hpp>
#include <mb/framepacing/core/Version.hpp>
#include <gtest/gtest.h>
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

TEST(Point, IsAPixelPositionThatComparesByValue)
{
  static_assert(FP::Point{} == FP::Point{0, 0});
  static_assert(FP::Point{3, -4}.X == 3 && FP::Point{3, -4}.Y == -4);
  static_assert(FP::Point{3, -4} != FP::Point{-4, 3});
  // The same at run time (coverage counts what runs)
  const FP::Point point{3, -4};
  EXPECT_EQ(point.X, 3);
  EXPECT_EQ(point.Y, -4);
  EXPECT_TRUE(point == (FP::Point{3, -4}));
  EXPECT_FALSE(point == (FP::Point{-4, 3}));
  EXPECT_TRUE(FP::Point{} == (FP::Point{0, 0}));
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
  EXPECT_EQ(FP::Rectangle(0, 0, 7, -1).Height(), 0);
}

TEST(Rectangle, EveryMemberAtRunTime)
{
  // The static_asserts above run at compile time; coverage counts what runs
  const FP::Rectangle rect(10, 20, 30, 40);
  EXPECT_EQ(rect.X(), 10);
  EXPECT_EQ(rect.Y(), 20);
  EXPECT_EQ(rect.Width(), 30);
  EXPECT_EQ(rect.Height(), 40);
  EXPECT_EQ(rect.Left(), 10);
  EXPECT_EQ(rect.Top(), 20);
  EXPECT_EQ(rect.Right(), 40);
  EXPECT_EQ(rect.Bottom(), 60);
  EXPECT_TRUE(rect.Contains(10, 20));
  EXPECT_TRUE(rect.Contains(39, 59));
  EXPECT_FALSE(rect.Contains(9, 20));
  EXPECT_FALSE(rect.Contains(40, 20));
  EXPECT_FALSE(rect.Contains(10, 19));
  EXPECT_FALSE(rect.Contains(10, 60));
  EXPECT_TRUE(FP::Rectangle::FromLeftTopRightBottom(10, 20, 40, 60) == rect);
  EXPECT_FALSE(FP::Rectangle() == rect);
  EXPECT_TRUE(FP::Rectangle() == FP::Rectangle(0, 0, 0, 0));
  // Always valid: a negative size is 0
  EXPECT_EQ(FP::Rectangle(5, 6, -3, -4), FP::Rectangle(5, 6, 0, 0));
  EXPECT_EQ(FP::Rectangle::FromLeftTopRightBottom(40, 60, 10, 20), FP::Rectangle(40, 60, 0, 0));
}

TEST(Rectangle, EdgesOutsideInt32AreAsserted)
{
#ifdef NDEBUG
  GTEST_SKIP() << "without asserts the caller's edges must fit int32_t";
#elif GTEST_HAS_DEATH_TEST
  const int32_t min = std::numeric_limits<int32_t>::min();
  const int32_t max = std::numeric_limits<int32_t>::max();
  EXPECT_DEATH(static_cast<void>(FP::Rectangle(max, 0, 1, 0)), "") << "a right edge past the last coordinate";
  EXPECT_DEATH(static_cast<void>(FP::Rectangle(0, max - 5, 0, 6)), "") << "a bottom edge past the last coordinate";
  EXPECT_DEATH(static_cast<void>(FP::Rectangle::FromLeftTopRightBottom(min, 0, max, 0)), "") << "a width no int32_t holds";
  EXPECT_DEATH(static_cast<void>(FP::Rectangle::FromLeftTopRightBottom(0, max, 0, min)), "") << "a height no int32_t holds";
  // The limits themselves are fine
  EXPECT_EQ(FP::Rectangle(max - 5, max - 6, 5, 6).Right(), max);
  EXPECT_EQ(FP::Rectangle(max - 5, max - 6, 5, 6).Bottom(), max);
  EXPECT_EQ(FP::Rectangle(min, min, max, max).Right(), -1);
  EXPECT_EQ(FP::Rectangle::FromLeftTopRightBottom(-1, -1, max - 1, max - 1), FP::Rectangle(-1, -1, max, max));
#else
  GTEST_SKIP() << "asserts are on and death tests are not available";
#endif
}
