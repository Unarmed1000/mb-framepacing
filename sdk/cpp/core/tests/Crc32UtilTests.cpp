// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
// Crc32Util: the CRC-32 of zlib, PNG and Ethernet, which the marker's payloads and a frame log's headers and records end with. The
// expected values come from Python's binascii.crc32, never from our own code.
#include <mb/framepacing/core/Crc32Util.hpp>
#include <gtest/gtest.h>
#include <array>
#include <cstddef>
#include <cstdint>
#include <span>
#include <string_view>
#include <vector>

namespace CRC = MB::FramePacing::Crc32Util;

TEST(Crc32Util, IsTheStandardOne)
{
  // The check value every description of the CRC-32 of zlib, PNG and Ethernet gives
  const std::string_view digits = "123456789";
  const std::vector<uint8_t> bytes(digits.begin(), digits.end());
  EXPECT_EQ(CRC::Compute(bytes), 0xCBF43926u);
  EXPECT_EQ(CRC::Compute({}), 0u);
  EXPECT_EQ(CRC::Compute(std::vector<uint8_t>(32, 0x00u)), 0x190A55ADu);
  EXPECT_EQ(CRC::Compute(std::vector<uint8_t>(32, 0xFFu)), 0xFF6CAB0Bu);

  // The table made at run time is the compile time one, and the one every half byte implementation of this CRC lists
  EXPECT_EQ(CRC::MakeTable(), CRC::Table);
  const std::array<uint32_t, 16> listed{0x00000000u, 0x1DB71064u, 0x3B6E20C8u, 0x26D930ACu, 0x76DC4190u, 0x6B6B51F4u, 0x4DB26158u, 0x5005713Cu,
                                        0xEDB88320u, 0xF00F9344u, 0xD6D6A3E8u, 0xCB61B38Cu, 0x9B64C2B0u, 0x86D3D2D4u, 0xA00AE278u, 0xBDBDF21Cu};
  EXPECT_EQ(CRC::Table, listed);
}

TEST(Crc32Util, ContinuesOverSeveralSpans)
{
  const std::string_view digits = "123456789";
  const std::vector<uint8_t> bytes(digits.begin(), digits.end());
  const std::span<const uint8_t> all(bytes);
  // Cut anywhere, the two parts give the CRC of the whole; no bytes change nothing
  for (std::size_t cut = 0; cut <= all.size(); ++cut)
  {
    EXPECT_EQ(CRC::Continue(CRC::Compute(all.first(cut)), all.subspan(cut)), 0xCBF43926u) << "cut at " << cut;
  }
  EXPECT_EQ(CRC::Continue(0u, all), 0xCBF43926u);
  EXPECT_EQ(CRC::Continue(0xCBF43926u, {}), 0xCBF43926u);

  // Byte by byte, as a writer that gets its bytes one at a time
  uint32_t crc = 0;
  for (std::size_t i = 0; i < all.size(); ++i)
  {
    crc = CRC::Continue(crc, all.subspan(i, 1));
  }
  EXPECT_EQ(crc, 0xCBF43926u);
  // binascii.crc32(b"56789", binascii.crc32(b"1234")) on the way: the CRC of "1234"
  EXPECT_EQ(CRC::Compute(all.first(4)), 0x9BE3E0A3u);
}

TEST(Crc32Util, TheFastFormGivesTheSameValues)
{
  // The check value, and the values Python's binascii.crc32 gives
  const std::string_view digits = "123456789";
  const std::vector<uint8_t> bytes(digits.begin(), digits.end());
  EXPECT_EQ(CRC::ComputeFast(bytes), 0xCBF43926u);
  EXPECT_EQ(CRC::ComputeFast({}), 0u);
  EXPECT_EQ(CRC::ComputeFast(std::vector<uint8_t>(32, 0x00u)), 0x190A55ADu);
  EXPECT_EQ(CRC::ComputeFast(std::vector<uint8_t>(32, 0xFFu)), 0xFF6CAB0Bu);
  EXPECT_EQ(CRC::ComputeFast(std::span<const uint8_t>(bytes).first(4)), 0x9BE3E0A3u);

  // Its table made at run time is the compile time one; its first entries are the ones every listing of it starts with, and every
  // sixteenth entry is the small table's
  EXPECT_EQ(CRC::MakeByteTable(), CRC::ByteTable);
  EXPECT_EQ(CRC::ByteTable[0], 0x00000000u);
  EXPECT_EQ(CRC::ByteTable[1], 0x77073096u);
  EXPECT_EQ(CRC::ByteTable[2], 0xEE0E612Cu);
  EXPECT_EQ(CRC::ByteTable[255], 0x2D02EF8Du);
  for (std::size_t i = 0; i < CRC::Table.size(); ++i)
  {
    EXPECT_EQ(CRC::ByteTable[i * 16u], CRC::Table[i]) << i;
  }

  // The same value as the small form on the same inputs: every length up to 300 bytes of a pattern with every byte value, whole
  // and continued from every third cut
  std::vector<uint8_t> pattern(300);
  uint32_t state = 12345u;
  for (uint8_t& rByte : pattern)
  {
    state = (state * 1664525u) + 1013904223u;
    rByte = static_cast<uint8_t>(state >> 24u);
  }
  const std::span<const uint8_t> all(pattern);
  for (std::size_t length = 0; length <= all.size(); ++length)
  {
    const std::span<const uint8_t> data = all.first(length);
    ASSERT_EQ(CRC::ComputeFast(data), CRC::Compute(data)) << "length " << length;
    for (std::size_t cut = 0; cut <= length; cut += 3u)
    {
      ASSERT_EQ(CRC::ContinueFast(CRC::ComputeFast(data.first(cut)), data.subspan(cut)), CRC::Compute(data)) << length << " cut at " << cut;
      ASSERT_EQ(CRC::ContinueFast(CRC::Compute(data.first(cut)), data.subspan(cut)), CRC::Compute(data)) << length << " cut at " << cut;
    }
  }
}
