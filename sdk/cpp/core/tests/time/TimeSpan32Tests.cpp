// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// TimeSpan32: the marker's 32-bit intervals, converted to and from a TimeSpan exactly.
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <gtest/gtest.h>
#include <compare>
#include <cstdint>
#include <limits>
#include <stdexcept>
#include <type_traits>

namespace FP = MB::FramePacing;

TEST(TimeSpan32, HoldsZeroToTheLargestUnsigned32BitTicks)
{
  static_assert(std::is_trivially_copyable_v<FP::TimeSpan32> && std::is_standard_layout_v<FP::TimeSpan32> && sizeof(FP::TimeSpan32) == 4);
  EXPECT_EQ(FP::TimeSpan32().Ticks(), 0u);
  EXPECT_EQ(FP::TimeSpan32::Zero().Ticks(), 0u);
  EXPECT_EQ(FP::TimeSpan32::MaxValue().Ticks(), std::numeric_limits<uint32_t>::max());
  EXPECT_EQ(FP::TimeSpan32(166'667u).Ticks(), 166'667u);
  EXPECT_EQ(FP::TimeSpan32::FromTicks(80'000u).Ticks(), 80'000u);
}

TEST(TimeSpan32, ConvertsFromATimeSpanExactlyOrThrows)
{
  EXPECT_EQ(FP::TimeSpan32::FromTimeSpan(FP::TimeSpan()).Ticks(), 0u);
  EXPECT_EQ(FP::TimeSpan32::FromTimeSpan(FP::TimeSpan::FromMilliseconds(16)).Ticks(), 160'000u);
  EXPECT_EQ(FP::TimeSpan32::FromTimeSpan(FP::TimeSpan(4'294'967'295)), FP::TimeSpan32::MaxValue());
  EXPECT_THROW(static_cast<void>(FP::TimeSpan32::FromTimeSpan(FP::TimeSpan(4'294'967'296))), std::out_of_range);
  EXPECT_THROW(static_cast<void>(FP::TimeSpan32::FromTimeSpan(FP::TimeSpan(-1))), std::out_of_range);
  EXPECT_EQ(FP::TimeSpan32::MaxValue().ToTimeSpan().Ticks(), 4'294'967'295);
  EXPECT_EQ(FP::TimeSpan32(166'667u).ToTimeSpan(), FP::TimeSpan(166'667));
}

TEST(TimeSpan32, ComparesByTicks)
{
  const FP::TimeSpan32 shorter(3u);
  const FP::TimeSpan32 longer(4u);
  EXPECT_TRUE(shorter == FP::TimeSpan32(3u));
  EXPECT_TRUE(shorter != longer);
  EXPECT_TRUE(shorter < longer);
  EXPECT_TRUE(longer >= shorter);
  EXPECT_EQ(shorter <=> longer, std::strong_ordering::less);
  static_assert(FP::TimeSpan32::FromTimeSpan(FP::TimeSpan(5)) == FP::TimeSpan32(5u));
}
