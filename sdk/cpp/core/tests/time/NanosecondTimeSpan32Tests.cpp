// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// NanosecondTimeSpan32: an interval of 0 to 4.294967295 s in nanoseconds, the form of a 32-bit field that holds one. A longer or a
// negative span does not fit and throws; nothing is cut.
#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <mb/framepacing/core/time/NanosecondTimeSpan32.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <gtest/gtest.h>
#include <compare>
#include <cstdint>
#include <limits>
#include <stdexcept>
#include <type_traits>

namespace FP = MB::FramePacing;

TEST(NanosecondTimeSpan32, HoldsAnUnsigned32BitCountOfNanoseconds)
{
  static_assert(std::is_trivially_copyable_v<FP::NanosecondTimeSpan32> && std::is_standard_layout_v<FP::NanosecondTimeSpan32> &&
                sizeof(FP::NanosecondTimeSpan32) == 4);
  EXPECT_EQ(FP::NanosecondTimeSpan32().Nanoseconds(), 0u);
  EXPECT_EQ(FP::NanosecondTimeSpan32::Zero().Nanoseconds(), 0u);
  EXPECT_EQ(FP::NanosecondTimeSpan32(4'166'389u).Nanoseconds(), 4'166'389u);
  EXPECT_EQ(FP::NanosecondTimeSpan32::FromNanoseconds(16'666'667u).Nanoseconds(), 16'666'667u);
  // 4.294967295 s: every refresh period a display has fits with room
  EXPECT_EQ(FP::NanosecondTimeSpan32::MaxValue().Nanoseconds(), std::numeric_limits<uint32_t>::max());
  static_assert(FP::NanosecondTimeSpan32::FromNanoseconds(5u) == FP::NanosecondTimeSpan32(5u));
}

TEST(NanosecondTimeSpan32, IsMadeFromASpanThatFitsAndThrowsForOneThatDoesNot)
{
  EXPECT_EQ(FP::NanosecondTimeSpan32::FromNanosecondTimeSpan(FP::NanosecondTimeSpan(4'166'389)).Nanoseconds(), 4'166'389u);
  EXPECT_EQ(FP::NanosecondTimeSpan32::FromNanosecondTimeSpan(FP::NanosecondTimeSpan()), FP::NanosecondTimeSpan32::Zero());
  EXPECT_EQ(FP::NanosecondTimeSpan32::FromNanosecondTimeSpan(FP::NanosecondTimeSpan(4'294'967'295)), FP::NanosecondTimeSpan32::MaxValue());
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeSpan32::FromNanosecondTimeSpan(FP::NanosecondTimeSpan(4'294'967'296))), std::out_of_range);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeSpan32::FromNanosecondTimeSpan(FP::NanosecondTimeSpan(-1))), std::out_of_range);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeSpan32::FromNanosecondTimeSpan(FP::NanosecondTimeSpan::MaxValue())), std::out_of_range);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeSpan32::FromNanosecondTimeSpan(FP::NanosecondTimeSpan::MinValue())), std::out_of_range);
  static_assert(FP::NanosecondTimeSpan32::FromNanosecondTimeSpan(FP::NanosecondTimeSpan(7)) == FP::NanosecondTimeSpan32(7u));

  // From ticks of 100 ns, exactly: 42,949,672 ticks is the longest that fits
  EXPECT_EQ(FP::NanosecondTimeSpan32::FromTimeSpan(FP::TimeSpan(41'664)).Nanoseconds(), 4'166'400u);
  EXPECT_EQ(FP::NanosecondTimeSpan32::FromTimeSpan(FP::TimeSpan()), FP::NanosecondTimeSpan32::Zero());
  EXPECT_EQ(FP::NanosecondTimeSpan32::FromTimeSpan(FP::TimeSpan(42'949'672)).Nanoseconds(), 4'294'967'200u);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeSpan32::FromTimeSpan(FP::TimeSpan(42'949'673))), std::out_of_range);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeSpan32::FromTimeSpan(FP::TimeSpan(-1))), std::out_of_range);
  EXPECT_THROW(static_cast<void>(FP::NanosecondTimeSpan32::FromTimeSpan(FP::TimeSpan::MaxValue())), std::out_of_range);
  static_assert(FP::NanosecondTimeSpan32::FromTimeSpan(FP::TimeSpan(3)) == FP::NanosecondTimeSpan32(300u));
}

TEST(NanosecondTimeSpan32, WidensToASpanExactlyAndToTicksTruncated)
{
  EXPECT_EQ(FP::NanosecondTimeSpan32(4'166'389u).ToNanosecondTimeSpan(), FP::NanosecondTimeSpan(4'166'389));
  EXPECT_EQ(FP::NanosecondTimeSpan32::MaxValue().ToNanosecondTimeSpan(), FP::NanosecondTimeSpan(4'294'967'295));
  EXPECT_EQ(FP::NanosecondTimeSpan32().ToNanosecondTimeSpan(), FP::NanosecondTimeSpan::Zero());
  EXPECT_EQ(FP::NanosecondTimeSpan32(4'166'389u).ToTimeSpan(), FP::TimeSpan(41'663));
  EXPECT_EQ(FP::NanosecondTimeSpan32(99u).ToTimeSpan(), FP::TimeSpan(0));
  EXPECT_EQ(FP::NanosecondTimeSpan32::MaxValue().ToTimeSpan(), FP::TimeSpan(42'949'672));
}

TEST(NanosecondTimeSpan32, ComparesByItsCount)
{
  const FP::NanosecondTimeSpan32 shorter(4'166'389u);
  const FP::NanosecondTimeSpan32 longer(16'666'667u);
  EXPECT_TRUE(shorter < longer);
  EXPECT_TRUE(shorter <= longer);
  EXPECT_TRUE(longer > shorter);
  EXPECT_TRUE(longer >= shorter);
  EXPECT_TRUE(shorter != longer);
  EXPECT_TRUE(shorter == FP::NanosecondTimeSpan32(4'166'389u));
  EXPECT_EQ(shorter <=> longer, std::strong_ordering::less);
  EXPECT_TRUE(FP::NanosecondTimeSpan32::Zero() < FP::NanosecondTimeSpan32::MaxValue());
}
