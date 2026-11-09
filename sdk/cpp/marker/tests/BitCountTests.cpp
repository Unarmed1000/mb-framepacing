// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// BitCount (private to the marker module): the counts of <bit> in a library built for C++20, plain code in one built for C++17. Both
// are held to counts made bit by bit.
#include <gtest/gtest.h>
#include <cstdint>
#include <limits>
#include "mb/framepacing/marker/detail/BitCount.hpp"

namespace BC = MB::FramePacing::Marker::BitCount;

namespace
{
  template <typename T>
  int32_t OnesBitByBit(const T value)
  {
    int32_t count = 0;
    for (int32_t bit = 0; bit < std::numeric_limits<T>::digits; ++bit)
    {
      count += static_cast<int32_t>((static_cast<uint64_t>(value) >> static_cast<uint32_t>(bit)) & 1u);
    }
    return count;
  }

  template <typename T>
  int32_t LeadingZerosBitByBit(const T value)
  {
    int32_t count = 0;
    for (int32_t bit = std::numeric_limits<T>::digits - 1; bit >= 0 && ((static_cast<uint64_t>(value) >> static_cast<uint32_t>(bit)) & 1u) == 0u;
         --bit)
    {
      ++count;
    }
    return count;
  }

  //! Words with every count of leading zeros and many of ones: each single bit, each run of ones from the top and from the bottom.
  template <typename T>
  void ExpectCountsOfWordsLike()
  {
    constexpr int32_t Digits = std::numeric_limits<T>::digits;
    const auto expect = [](const T value)
    {
      EXPECT_EQ(BC::PopCount(value), OnesBitByBit(value)) << static_cast<uint64_t>(value);
      EXPECT_EQ(BC::CountLeadingZeros(value), LeadingZerosBitByBit(value)) << static_cast<uint64_t>(value);
      EXPECT_EQ(BC::CountLeadingOnes(value), LeadingZerosBitByBit(static_cast<T>(~value))) << static_cast<uint64_t>(value);
    };
    expect(T{0});
    expect(std::numeric_limits<T>::max());
    for (int32_t bit = 0; bit < Digits; ++bit)
    {
      const auto single = static_cast<T>(T{1} << static_cast<uint32_t>(bit));
      expect(single);
      expect(static_cast<T>(~single));
      expect(static_cast<T>(single - 1u));
      expect(static_cast<T>(~static_cast<T>(single - 1u)));
      expect(static_cast<T>(single | 0x5Au));
    }
  }
}

TEST(BitCount, CountsEveryByte)
{
  for (uint32_t value = 0; value < 256u; ++value)
  {
    const auto byte = static_cast<uint8_t>(value);
    EXPECT_EQ(BC::PopCount(byte), OnesBitByBit(byte)) << value;
    EXPECT_EQ(BC::CountLeadingZeros(byte), LeadingZerosBitByBit(byte)) << value;
    EXPECT_EQ(BC::CountLeadingOnes(byte), LeadingZerosBitByBit(static_cast<uint8_t>(~byte))) << value;
  }
}

TEST(BitCount, CountsWordsOfEveryWidth)
{
  ExpectCountsOfWordsLike<uint16_t>();
  ExpectCountsOfWordsLike<uint32_t>();
  ExpectCountsOfWordsLike<uint64_t>();
}

TEST(BitCount, CountsAtCompileTime)
{
  static_assert(BC::PopCount(uint64_t{0xF0F0F0F0F0F0F0F0u}) == 32);
  static_assert(BC::CountLeadingZeros(uint8_t{1}) == 7);
  static_assert(BC::CountLeadingZeros(uint64_t{0}) == 64);
  static_assert(BC::CountLeadingOnes(uint64_t{0xFFFF000000000000u}) == 16);
  SUCCEED();
}
