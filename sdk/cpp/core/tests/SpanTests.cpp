// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// Span: std::span in a library built for C++20, the SDK's own in one built for C++17. The tests hold either to what the SDK uses of it.
#include <mb/framepacing/core/Config.hpp>
#include <mb/framepacing/core/Span.hpp>
#include <gtest/gtest.h>
#include <array>
#include <cstddef>
#include <cstdint>
#include <string>
#include <type_traits>
#include <vector>
#if MB_FRAMEPACING_CXX_STANDARD >= 20
#include <span>
#endif

using MB::FramePacing::DynamicExtent;
using MB::FramePacing::Span;

namespace
{
  std::size_t SizeOf(const Span<const uint8_t> bytes)
  {
    return bytes.size();
  }

  constexpr int32_t Sum(const Span<const int32_t> values) noexcept
  {
    int32_t sum = 0;
    for (const int32_t value : values)
    {
      sum += value;
    }
    return sum;
  }

  constexpr std::array<int32_t, 4> Values{1, 2, 3, 4};
}

#if MB_FRAMEPACING_CXX_STANDARD >= 20
static_assert(std::is_same_v<Span<const uint8_t>, std::span<const uint8_t>>);
static_assert(std::is_same_v<Span<int32_t, 6>, std::span<int32_t, 6>>);
static_assert(DynamicExtent == std::dynamic_extent);
#endif

// A constant expression, as the CRC's static_asserts need it
static_assert(Sum(Values) == 10);
static_assert(Sum(Span<const int32_t>(Values).first(2)) == 3);
static_assert(Sum(Span<const int32_t>(Values).last(1)) == 4);
static_assert(Sum(Span<const int32_t>(Values).subspan(1, 2)) == 5);
static_assert(Sum(Span<const int32_t>()) == 0);

TEST(Span, WorksAtRunTimeAsAtCompileTime)
{
  EXPECT_EQ(Sum(Values), 10);
  EXPECT_EQ(Sum(Span<const int32_t>(Values).first(2)), 3);
  EXPECT_EQ(Sum(Span<const int32_t>(Values).last(1)), 4);
  EXPECT_EQ(Sum(Span<const int32_t>(Values).subspan(1, 2)), 5);
  EXPECT_EQ(Sum(Span<const int32_t>()), 0);
}

TEST(Span, IsEmptyByDefault)
{
  const Span<const uint8_t> span;
  EXPECT_EQ(span.data(), nullptr);
  EXPECT_EQ(span.size(), 0u);
  EXPECT_TRUE(span.empty());
  EXPECT_EQ(span.begin(), span.end());
}

TEST(Span, ViewsAPointerAndCount)
{
  std::array<int32_t, 3> values{7, 8, 9};
  const Span<int32_t> span(values.data(), 2);
  EXPECT_EQ(span.data(), values.data());
  EXPECT_EQ(span.size(), 2u);
  EXPECT_EQ(span.size_bytes(), 2u * sizeof(int32_t));
  EXPECT_FALSE(span.empty());
  span[1] = 80;
  EXPECT_EQ(values[1], 80);
}

TEST(Span, ViewsArraysAndContainers)
{
  // A C array, which a Span takes as std::span does
  // NOLINTNEXTLINE(modernize-avoid-c-arrays)
  uint8_t cArray[3]{1, 2, 3};
  std::array<uint8_t, 4> array{};
  const std::array<uint8_t, 5> constArray{};
  std::vector<uint8_t> vector(6);
  const std::vector<uint8_t> constVector(7);
  const std::string text("12345678");

  EXPECT_EQ(SizeOf(cArray), 3u);
  EXPECT_EQ(SizeOf(array), 4u);
  EXPECT_EQ(SizeOf(constArray), 5u);
  EXPECT_EQ(SizeOf(vector), 6u);
  EXPECT_EQ(SizeOf(constVector), 7u);
  EXPECT_EQ(Span<const char>(text).size(), 8u);
  EXPECT_EQ(SizeOf(std::vector<uint8_t>(9)), 9u);

  const Span<uint8_t> writable(vector);
  writable[0] = 42;
  EXPECT_EQ(vector[0], 42);
  const Span<uint8_t> fromArray(array);
  fromArray[3] = 43;
  EXPECT_EQ(array[3], 43);
  const Span<uint8_t> fromCArray(cArray);
  EXPECT_EQ(fromCArray.data(), &cArray[0]);
}

TEST(Span, ConvertsToConstAndToADynamicExtent)
{
  std::array<int32_t, 6> values{1, 2, 3, 4, 5, 6};
  const Span<int32_t, 6> fixed(values);
  const Span<const int32_t> dynamic(fixed);
  EXPECT_EQ(dynamic.data(), values.data());
  EXPECT_EQ(dynamic.size(), 6u);
  EXPECT_EQ((Span<int32_t, 6>::extent), 6u);
  EXPECT_EQ(Span<int32_t>::extent, DynamicExtent);
  const Span<const int32_t, 6> fixedConst(fixed);
  EXPECT_EQ(fixedConst[5], 6);
}

TEST(Span, TakesPartsOfItself)
{
  std::array<int32_t, 6> values{1, 2, 3, 4, 5, 6};
  const Span<int32_t> span(values);
  EXPECT_EQ(span.first(2).data(), values.data());
  EXPECT_EQ(span.first(2).size(), 2u);
  EXPECT_EQ(span.last(2).data(), values.data() + 4);
  EXPECT_EQ(span.last(2).size(), 2u);
  EXPECT_EQ(span.subspan(1).data(), values.data() + 1);
  EXPECT_EQ(span.subspan(1).size(), 5u);
  EXPECT_EQ(span.subspan(1, 3).size(), 3u);
  EXPECT_EQ(span.subspan(6).size(), 0u);
  const Span<int32_t, 4> four = span.first<4>();
  EXPECT_EQ(four.data(), values.data());
  EXPECT_EQ(four.size(), 4u);
}

TEST(Span, IteratesItsElements)
{
  std::array<int32_t, 3> values{1, 2, 3};
  int32_t sum = 0;
  for (int32_t& value : Span<int32_t>(values))
  {
    value *= 2;
    sum += value;
  }
  EXPECT_EQ(sum, 12);
  EXPECT_EQ(Span<int32_t>(values).end() - Span<int32_t>(values).begin(), 3);
}
