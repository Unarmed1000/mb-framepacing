// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// ByteSpanUtil: little-endian values in byte spans, the byte count from the value's type.
#include <mb/framepacing/core/ByteSpanUtil.hpp>
#include <gtest/gtest.h>
#include <array>
#include <cstdint>
#include <limits>
#include <span>

namespace BSU = MB::FramePacing::ByteSpanUtil;

TEST(ByteSpanUtil, WritesTheLeastSignificantByteFirst)
{
  std::array<uint8_t, 10> bytes{};
  BSU::WriteLE(bytes, 1, uint32_t{0x01020304u});
  EXPECT_EQ(bytes, (std::array<uint8_t, 10>{0x00u, 0x04u, 0x03u, 0x02u, 0x01u, 0x00u, 0x00u, 0x00u, 0x00u, 0x00u}));
  BSU::WriteLE(bytes, 2, uint64_t{0x0102030405060708u});
  EXPECT_EQ(bytes, (std::array<uint8_t, 10>{0x00u, 0x04u, 0x08u, 0x07u, 0x06u, 0x05u, 0x04u, 0x03u, 0x02u, 0x01u}));
  BSU::WriteLE(bytes, 0, uint16_t{0xA1B2u});
  BSU::WriteLE(bytes, 9, uint8_t{0xC3u});
  EXPECT_EQ(bytes, (std::array<uint8_t, 10>{0xB2u, 0xA1u, 0x08u, 0x07u, 0x06u, 0x05u, 0x04u, 0x03u, 0x02u, 0xC3u}));
}

TEST(ByteSpanUtil, WritesSignedValuesAsTwosComplementAndDoublesAsTheirBits)
{
  std::array<uint8_t, 8> bytes{};
  BSU::WriteLE(bytes, 0, int64_t{-2});
  EXPECT_EQ(bytes, (std::array<uint8_t, 8>{0xFEu, 0xFFu, 0xFFu, 0xFFu, 0xFFu, 0xFFu, 0xFFu, 0xFFu}));
  BSU::WriteLE(bytes, 0, int32_t{-2});
  BSU::WriteLE(bytes, 4, int16_t{0x0102});
  BSU::WriteLE(bytes, 6, int8_t{-1});
  BSU::WriteLE(bytes, 7, int8_t{0});
  EXPECT_EQ(bytes, (std::array<uint8_t, 8>{0xFEu, 0xFFu, 0xFFu, 0xFFu, 0x02u, 0x01u, 0xFFu, 0x00u}));
  // 1.0 is 0x3FF0000000000000
  BSU::WriteLE(bytes, 0, 1.0);
  EXPECT_EQ(bytes, (std::array<uint8_t, 8>{0x00u, 0x00u, 0x00u, 0x00u, 0x00u, 0x00u, 0xF0u, 0x3Fu}));
}

TEST(ByteSpanUtil, ReadsWhatItWrote)
{
  const std::array<uint8_t, 9> bytes{0x11u, 0x08u, 0x07u, 0x06u, 0x05u, 0x04u, 0x03u, 0x02u, 0x81u};
  EXPECT_EQ(BSU::ReadLE<uint8_t>(bytes, 0), 0x11u);
  EXPECT_EQ(BSU::ReadLE<uint16_t>(bytes, 1), 0x0708u);
  EXPECT_EQ(BSU::ReadLE<uint32_t>(bytes, 1), 0x05060708u);
  EXPECT_EQ(BSU::ReadLE<uint64_t>(bytes, 1), 0x8102030405060708u);
  EXPECT_EQ(BSU::ReadLE<int8_t>(bytes, 8), int8_t{-127});
  EXPECT_EQ(BSU::ReadLE<int16_t>(bytes, 7), int16_t{-32510});
  EXPECT_EQ(BSU::ReadLE<int32_t>(bytes, 5), int32_t{-2130574588});
  EXPECT_EQ(BSU::ReadLE<int64_t>(bytes, 1), std::numeric_limits<int64_t>::min() + 0x0102030405060708);

  std::array<uint8_t, 8> buffer{};
  for (const double value : {0.0, -0.5, 1.0 / 3.0, std::numeric_limits<double>::max(), std::numeric_limits<double>::lowest()})
  {
    BSU::WriteLE(buffer, 0, value);
    EXPECT_EQ(BSU::ReadLE<double>(buffer, 0), value);
  }
  for (const int64_t value : {int64_t{0}, int64_t{-1}, std::numeric_limits<int64_t>::min(), std::numeric_limits<int64_t>::max()})
  {
    BSU::WriteLE(buffer, 0, value);
    EXPECT_EQ(BSU::ReadLE<int64_t>(buffer, 0), value);
  }
}

TEST(ByteSpanUtil, AnOffsetAtTheEndFitsAValueThatEndsThere)
{
  std::array<uint8_t, 4> bytes{};
  BSU::WriteLE(std::span<uint8_t>(bytes), 2, uint16_t{0xBEEFu});
  EXPECT_EQ(BSU::ReadLE<uint16_t>(bytes, 2), 0xBEEFu);
  EXPECT_EQ(BSU::ReadLE<uint32_t>(bytes, 0), 0xBEEF0000u);
}

TEST(ByteSpanUtil, ABufferTooSmallIsAsserted)
{
#ifdef NDEBUG
  GTEST_SKIP() << "without asserts the caller's buffer must be large enough";
#elif GTEST_HAS_DEATH_TEST
  std::array<uint8_t, 4> bytes{};
  EXPECT_DEATH(BSU::WriteLE(bytes, 1, uint32_t{1u}), "");
  EXPECT_DEATH(BSU::WriteLE(bytes, 5, uint8_t{1u}), "");
  EXPECT_DEATH(static_cast<void>(BSU::ReadLE<uint64_t>(bytes, 0)), "");
  EXPECT_DEATH(static_cast<void>(BSU::ReadLE<uint8_t>(bytes, 4)), "");
#else
  GTEST_SKIP() << "asserts are on and death tests are not available";
#endif
}
