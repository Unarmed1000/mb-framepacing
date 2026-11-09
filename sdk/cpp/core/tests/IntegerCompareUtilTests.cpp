// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// IntegerCompareUtil: integers compared by value, whatever their signs (C++20's std::cmp_less and std::cmp_greater).
#include <mb/framepacing/core/IntegerCompareUtil.hpp>
#include <gtest/gtest.h>
#include <cstdint>
#include <limits>

namespace ICU = MB::FramePacing::IntegerCompareUtil;

static_assert(ICU::IsComparableInteger<int8_t> && ICU::IsComparableInteger<uint64_t> && ICU::IsComparableInteger<char>);
static_assert(!ICU::IsComparableInteger<bool> && !ICU::IsComparableInteger<double>);
static_assert(ICU::Less(-1, 0u) && ICU::Greater(0u, -1));

TEST(IntegerCompareUtil, ComparesTheSameSign)
{
  EXPECT_TRUE(ICU::Less(int8_t{-5}, int64_t{3}));
  EXPECT_FALSE(ICU::Less(int64_t{3}, int8_t{-5}));
  EXPECT_TRUE(ICU::Less(uint8_t{3}, uint64_t{4}));
  EXPECT_FALSE(ICU::Less(uint64_t{4}, uint64_t{4}));
}

TEST(IntegerCompareUtil, ComparesASignedLeftWithAnUnsignedRight)
{
  // A negative left is less; otherwise the values compare
  EXPECT_TRUE(ICU::Less(int32_t{-1}, uint32_t{0}));
  EXPECT_TRUE(ICU::Less(int32_t{3}, uint32_t{5}));
  EXPECT_FALSE(ICU::Less(int32_t{5}, uint32_t{3}));
  EXPECT_TRUE(ICU::Less(int32_t{-1}, std::numeric_limits<uint64_t>::max()));
  EXPECT_FALSE(ICU::Less(int32_t{2}, uint64_t{1}));
  EXPECT_TRUE(ICU::Less(int64_t{-1}, uint8_t{5}));
  EXPECT_TRUE(ICU::Less(int64_t{4}, uint8_t{5}));
  EXPECT_FALSE(ICU::Less(int64_t{5}, uint8_t{5}));
  EXPECT_TRUE(ICU::Less(int64_t{-3}, uint32_t{7}));
  EXPECT_FALSE(ICU::Less(std::numeric_limits<int64_t>::max(), uint32_t{7}));
}

TEST(IntegerCompareUtil, ComparesAnUnsignedLeftWithASignedRight)
{
  // A negative right is never greater; otherwise the values compare
  EXPECT_FALSE(ICU::Less(uint32_t{0}, int32_t{-1}));
  EXPECT_TRUE(ICU::Less(uint32_t{0}, int32_t{1}));
  EXPECT_FALSE(ICU::Less(uint32_t{2}, int32_t{1}));
  EXPECT_FALSE(ICU::Less(std::numeric_limits<uint64_t>::max(), std::numeric_limits<int64_t>::min()));
  EXPECT_TRUE(ICU::Less(uint64_t{1}, int64_t{2}));
  EXPECT_FALSE(ICU::Less(uint64_t{3}, int64_t{2}));
  EXPECT_TRUE(ICU::Less(uint8_t{4}, int64_t{5}));
  EXPECT_FALSE(ICU::Less(uint8_t{5}, int64_t{5}));
  EXPECT_FALSE(ICU::Less(uint8_t{1}, int64_t{-1}));
}

TEST(IntegerCompareUtil, GreaterIsLessTurnedAround)
{
  EXPECT_TRUE(ICU::Greater(uint32_t{0}, int32_t{-1}));
  EXPECT_FALSE(ICU::Greater(uint32_t{0}, int32_t{1}));
  EXPECT_TRUE(ICU::Greater(uint32_t{5}, int32_t{1}));
  EXPECT_FALSE(ICU::Greater(int32_t{-1}, uint32_t{0}));
  EXPECT_TRUE(ICU::Greater(int32_t{1}, uint32_t{0}));
  EXPECT_FALSE(ICU::Greater(int32_t{1}, uint32_t{4}));
  EXPECT_FALSE(ICU::Greater(int32_t{3}, int32_t{3}));
}
