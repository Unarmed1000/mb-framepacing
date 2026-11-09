#ifndef MB_FRAMEPACING_CORE_CRC32UTIL_HPP
#define MB_FRAMEPACING_CORE_CRC32UTIL_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The CRC-32 of the SDK's formats: every marker payload ends with it (doc/marker-format.md), and a frame log's block headers and
// records (doc/frame-log-format.md). It is the CRC-32 of zlib, PNG and Ethernet: the polynomial 0x04C11DB7 bit reversed (0xEDB88320),
// the register started at and XORed at the end with 0xFFFFFFFF. Two forms with the same result:
// - Compute and Continue take half a byte per step, so their table is 16 entries made at compile time (64 bytes): the small one,
//   which the marker uses (a payload is at most 85 bytes, once a frame, and its code size counts).
// - ComputeFast and ContinueFast take a byte per step with a table of 256 entries (1 KiB): the fast one, for the frame log's
//   records. An application that never calls them does not get the table.

#include <mb/framepacing/core/Span.hpp>
#include <array>
#include <cstddef>
#include <cstdint>

namespace MB::FramePacing::Crc32Util
{
  //! The polynomial, bit reversed: the register shifts right.
  inline constexpr uint32_t ReversedPolynomial = 0xEDB88320u;

  //! What four shifts make of each half byte.
  [[nodiscard]] constexpr std::array<uint32_t, 16> MakeTable() noexcept
  {
    std::array<uint32_t, 16> table{};
    for (uint32_t i = 0; i < 16u; ++i)
    {
      uint32_t value = i;
      for (int32_t bit = 0; bit < 4; ++bit)
      {
        value = (value >> 1u) ^ ((value & 1u) * ReversedPolynomial);
      }
      table[i] = value;
    }
    return table;
  }

  inline constexpr std::array<uint32_t, 16> Table = MakeTable();

  //! The CRC-32 of the bytes crc is the CRC-32 of, followed by data: a CRC over several spans is continued from one to the next,
  //! starting from 0 (the CRC-32 of no bytes), as zlib's crc32 does.
  [[nodiscard]] constexpr uint32_t Continue(const uint32_t crc, const Span<const uint8_t> data) noexcept
  {
    uint32_t value = crc ^ 0xFFFFFFFFu;
    for (const uint8_t byte : data)
    {
      value ^= static_cast<uint32_t>(byte);
      value = (value >> 4u) ^ Table[value & 15u];
      value = (value >> 4u) ^ Table[value & 15u];
    }
    return value ^ 0xFFFFFFFFu;
  }

  //! The CRC-32 of data.
  [[nodiscard]] constexpr uint32_t Compute(const Span<const uint8_t> data) noexcept
  {
    return Continue(0u, data);
  }

  //! What eight shifts make of each byte: the table of the fast form.
  [[nodiscard]] constexpr std::array<uint32_t, 256> MakeByteTable() noexcept
  {
    std::array<uint32_t, 256> table{};
    for (uint32_t i = 0; i < 256u; ++i)
    {
      uint32_t value = i;
      for (int32_t bit = 0; bit < 8; ++bit)
      {
        value = (value >> 1u) ^ ((value & 1u) * ReversedPolynomial);
      }
      table[i] = value;
    }
    return table;
  }

  inline constexpr std::array<uint32_t, 256> ByteTable = MakeByteTable();

  //! Continue with the table of 256 entries: the same result, a byte per step.
  [[nodiscard]] constexpr uint32_t ContinueFast(const uint32_t crc, const Span<const uint8_t> data) noexcept
  {
    uint32_t value = crc ^ 0xFFFFFFFFu;
    for (const uint8_t byte : data)
    {
      value = (value >> 8u) ^ ByteTable[(value ^ static_cast<uint32_t>(byte)) & 0xFFu];
    }
    return value ^ 0xFFFFFFFFu;
  }

  //! Compute with the table of 256 entries: the same result, a byte per step.
  [[nodiscard]] constexpr uint32_t ComputeFast(const Span<const uint8_t> data) noexcept
  {
    return ContinueFast(0u, data);
  }

  //! The check value every description of this CRC gives: the ASCII digits 1 to 9.
  inline constexpr std::array<uint8_t, 9> CheckInput{'1', '2', '3', '4', '5', '6', '7', '8', '9'};
  static_assert(Compute(CheckInput) == 0xCBF43926u);
  static_assert(Compute(Span<const uint8_t>()) == 0u);
  static_assert(Continue(Compute(Span<const uint8_t>(CheckInput).first(4)), Span<const uint8_t>(CheckInput).subspan(4)) == 0xCBF43926u);
  static_assert(ComputeFast(CheckInput) == 0xCBF43926u);
  static_assert(ComputeFast(Span<const uint8_t>()) == 0u);
}

#endif
