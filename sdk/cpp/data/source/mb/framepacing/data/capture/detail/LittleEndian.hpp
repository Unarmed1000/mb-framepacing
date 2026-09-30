#ifndef MB_FRAMEPACING_DATA_CAPTURE_DETAIL_LITTLEENDIAN_HPP
#define MB_FRAMEPACING_DATA_CAPTURE_DETAIL_LITTLEENDIAN_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Private to the data module: the little-endian values of captures.mbcd (doc/capture-data-format.md).

#include <bit>
#include <cstddef>
#include <cstdint>
#include <span>

namespace MB::FramePacing::Data::Detail
{
  inline uint64_t ReadU64(const std::span<const uint8_t> bytes, const std::size_t offset) noexcept
  {
    uint64_t value = 0;
    for (std::size_t i = 0; i < 8; ++i)
    {
      value |= static_cast<uint64_t>(bytes[offset + i]) << (8u * i);
    }
    return value;
  }

  inline uint32_t ReadU32(const std::span<const uint8_t> bytes, const std::size_t offset) noexcept
  {
    return static_cast<uint32_t>(bytes[offset]) | (static_cast<uint32_t>(bytes[offset + 1]) << 8u) |
           (static_cast<uint32_t>(bytes[offset + 2]) << 16u) | (static_cast<uint32_t>(bytes[offset + 3]) << 24u);
  }

  inline uint16_t ReadU16(const std::span<const uint8_t> bytes, const std::size_t offset) noexcept
  {
    return static_cast<uint16_t>(static_cast<uint32_t>(bytes[offset]) | (static_cast<uint32_t>(bytes[offset + 1]) << 8u));
  }

  inline int32_t ReadI32(const std::span<const uint8_t> bytes, const std::size_t offset) noexcept
  {
    return static_cast<int32_t>(ReadU32(bytes, offset));
  }

  inline int64_t ReadI64(const std::span<const uint8_t> bytes, const std::size_t offset) noexcept
  {
    return static_cast<int64_t>(ReadU64(bytes, offset));
  }

  inline double ReadF64(const std::span<const uint8_t> bytes, const std::size_t offset) noexcept
  {
    return std::bit_cast<double>(ReadU64(bytes, offset));
  }
}

#endif
