#ifndef MB_FRAMEPACING_MARKER_DETAIL_CRC32_HPP
#define MB_FRAMEPACING_MARKER_DETAIL_CRC32_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Private to the marker module: the CRC-32 every payload ends with (doc/marker-format.md). It is the CRC-32 of zlib, PNG and
// Ethernet: the polynomial 0x04C11DB7 bit reversed (0xEDB88320), the register started at and XORed at the end with 0xFFFFFFFF. Half
// a byte per step, so the table is 16 entries made at compile time (64 bytes, where a table for whole bytes is 1 KiB): a payload is
// at most 81 bytes, once a frame.

#include <array>
#include <cstddef>
#include <cstdint>
#include <span>

namespace MB::FramePacing::Marker::Crc32
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

  //! The CRC-32 of data.
  [[nodiscard]] constexpr uint32_t Compute(const std::span<const uint8_t> data) noexcept
  {
    uint32_t crc = 0xFFFFFFFFu;
    for (const uint8_t value : data)
    {
      crc ^= static_cast<uint32_t>(value);
      crc = (crc >> 4u) ^ Table[crc & 15u];
      crc = (crc >> 4u) ^ Table[crc & 15u];
    }
    return crc ^ 0xFFFFFFFFu;
  }

  //! The check value every description of this CRC gives: the ASCII digits 1 to 9.
  inline constexpr std::array<uint8_t, 9> CheckInput{'1', '2', '3', '4', '5', '6', '7', '8', '9'};
  static_assert(Compute(CheckInput) == 0xCBF43926u);
  static_assert(Compute(std::span<const uint8_t>()) == 0u);
}

#endif
