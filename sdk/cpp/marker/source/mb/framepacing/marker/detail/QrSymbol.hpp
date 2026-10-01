#ifndef MB_FRAMEPACING_MARKER_DETAIL_QRSYMBOL_HPP
#define MB_FRAMEPACING_MARKER_DETAIL_QRSYMBOL_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Private to the marker module: a QR symbol as the encoder works on it, one 64-bit word per row and one per column.

#include <array>
#include <cstddef>
#include <cstdint>

namespace MB::FramePacing::Marker::QrEncoder
{
  //! A QR symbol of Size x Size modules (1 = dark), held twice: as rows and as columns, so a line of either direction is one word.
  //! Module (x, y) is bit 63 - x of Rows[y] and bit 63 - y of Columns[x]: the first module of a line is its word's highest bit, and the
  //! bits below the line are zero.
  struct QrSymbol
  {
    //! Modules per side of the largest symbol the marker uses (QR version 6).
    static constexpr int32_t MaxSize = 41;

    int32_t Size{0};
    std::array<uint64_t, MaxSize> Rows{};
    std::array<uint64_t, MaxSize> Columns{};

    //! The bit of module `position` of a line.
    [[nodiscard]] static constexpr uint64_t Bit(const int32_t position) noexcept
    {
      return uint64_t{1} << static_cast<uint32_t>(63 - position);
    }

    [[nodiscard]] constexpr bool IsDark(const int32_t x, const int32_t y) const noexcept
    {
      return (Rows[static_cast<std::size_t>(y)] & Bit(x)) != 0u;
    }

    //! Set module (x, y) dark, in its row and its column.
    constexpr void SetDark(const int32_t x, const int32_t y) noexcept
    {
      Rows[static_cast<std::size_t>(y)] |= Bit(x);
      Columns[static_cast<std::size_t>(x)] |= Bit(y);
    }
  };
}

#endif
