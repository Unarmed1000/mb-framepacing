// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// The core's comparisons: operator== and operator!= are written by hand (C++17 has no defaulted ones), so every field is checked to
// take part, and the time types' ordering operators are checked both ways.
#include <mb/framepacing/core/Point.hpp>
#include <mb/framepacing/core/Rectangle.hpp>
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/core/time/TickCount32.hpp>
#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeDuration.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <mb/framepacing/testing/EqualityCheck.hpp>
#include <gtest/gtest.h>
#include <cstdint>

namespace FP = MB::FramePacing;
namespace EC = MB::FramePacing::Testing::EqualityCheck;

namespace
{
  //! Every ordering operator of a type whose values shorter < longer.
  template <typename T>
  void ExpectOrdered(const T shorter, const T longer)
  {
    EXPECT_TRUE(shorter < longer);
    EXPECT_FALSE(longer < shorter);
    EXPECT_FALSE(shorter < shorter);
    EXPECT_TRUE(shorter <= longer);
    EXPECT_TRUE(shorter <= shorter);
    EXPECT_FALSE(longer <= shorter);
    EXPECT_TRUE(longer > shorter);
    EXPECT_FALSE(shorter > longer);
    EXPECT_FALSE(longer > longer);
    EXPECT_TRUE(longer >= shorter);
    EXPECT_TRUE(longer >= longer);
    EXPECT_FALSE(shorter >= longer);
    EXPECT_TRUE(shorter != longer);
    EXPECT_FALSE(shorter == longer);
  }
}

TEST(Comparison, APointComparesEveryField)
{
  EXPECT_EQ(EC::FirstChangeNotSeen(FP::Point{1, 2}, [](FP::Point& point) { point.X = 3; }, [](FP::Point& point) { point.Y = 3; }), -1);
}

TEST(Comparison, ARectangleComparesEveryField)
{
  const FP::Rectangle rectangle(1, 2, 3, 4);
  EXPECT_EQ(EC::FirstChangeNotSeen(
              rectangle, [](FP::Rectangle& changed) { changed = FP::Rectangle(9, 2, 3, 4); }, [](FP::Rectangle& changed)
              { changed = FP::Rectangle(1, 9, 3, 4); }, [](FP::Rectangle& changed) { changed = FP::Rectangle(1, 2, 9, 4); },
              [](FP::Rectangle& changed) { changed = FP::Rectangle(1, 2, 3, 9); }),
            -1);
}

TEST(Comparison, TheTimeTypesCompareTheirValue)
{
  EXPECT_EQ(EC::FirstChangeNotSeen(FP::TimeSpan(5), [](FP::TimeSpan& value) { value = FP::TimeSpan(6); }), -1);
  EXPECT_EQ(EC::FirstChangeNotSeen(FP::TimeSpan32(5u), [](FP::TimeSpan32& value) { value = FP::TimeSpan32(6u); }), -1);
  EXPECT_EQ(EC::FirstChangeNotSeen(FP::TimeDuration::FromTicks(5), [](FP::TimeDuration& value) { value = FP::TimeDuration::FromTicks(6); }), -1);
  EXPECT_EQ(EC::FirstChangeNotSeen(FP::TickCount64(int64_t{5}), [](FP::TickCount64& value) { value = FP::TickCount64(int64_t{6}); }), -1);
  EXPECT_EQ(EC::FirstChangeNotSeen(FP::TickCount32(5u), [](FP::TickCount32& value) { value = FP::TickCount32(6u); }), -1);
  EXPECT_EQ(EC::FirstChangeNotSeen(FP::NanosecondTimeSpan(5), [](FP::NanosecondTimeSpan& value) { value = FP::NanosecondTimeSpan(6); }), -1);
  EXPECT_EQ(EC::FirstChangeNotSeen(FP::NanosecondTimeDuration::FromNanoseconds(5),
                                   [](FP::NanosecondTimeDuration& value) { value = FP::NanosecondTimeDuration::FromNanoseconds(6); }),
            -1);
  EXPECT_EQ(EC::FirstChangeNotSeen(FP::NanosecondTickCount::FromNanoseconds(5),
                                   [](FP::NanosecondTickCount& value) { value = FP::NanosecondTickCount::FromNanoseconds(6); }),
            -1);
}

TEST(Comparison, TheTimeIntervalsAndDurationsAreOrdered)
{
  ExpectOrdered(FP::TimeSpan(-2), FP::TimeSpan(3));
  ExpectOrdered(FP::TimeSpan32(2u), FP::TimeSpan32(3u));
  ExpectOrdered(FP::TimeDuration::FromTicks(2), FP::TimeDuration::FromTicks(3));
  ExpectOrdered(FP::NanosecondTimeSpan(-2), FP::NanosecondTimeSpan(3));
  ExpectOrdered(FP::NanosecondTimeDuration::FromNanoseconds(2), FP::NanosecondTimeDuration::FromNanoseconds(3));
}
