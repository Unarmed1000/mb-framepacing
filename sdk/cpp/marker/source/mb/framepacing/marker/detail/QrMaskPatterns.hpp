#ifndef MB_FRAMEPACING_MARKER_DETAIL_QRMASKPATTERNS_HPP
#define MB_FRAMEPACING_MARKER_DETAIL_QRMASKPATTERNS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Private to the marker module: the QR code's eight masks as words. A mask inverts the data modules where its rule holds; every rule
// repeats after 12 rows and after 12 columns, so 12 words per mask and direction say which modules of any line it inverts. Applying a
// mask to a line is then one AND (with the line's data modules) and one XOR.

#include <array>
#include <cstddef>
#include <cstdint>
#include "QrSymbol.hpp"

namespace MB::FramePacing::Marker::QrEncoder
{
  inline constexpr int32_t MaskCount = 8;

  //! Every mask's rule repeats after this many rows, and after this many columns.
  inline constexpr std::size_t MaskPeriod = 12;

  //! The QR standard's mask rules: mask inverts module (x, y).
  [[nodiscard]] constexpr bool MaskInverts(const int32_t mask, const int32_t x, const int32_t y) noexcept
  {
    switch (mask)
    {
    case 0:
      return (x + y) % 2 == 0;
    case 1:
      return y % 2 == 0;
    case 2:
      return x % 3 == 0;
    case 3:
      return (x + y) % 3 == 0;
    case 4:
      return ((x / 3) + (y / 2)) % 2 == 0;
    case 5:
      return ((x * y) % 2) + ((x * y) % 3) == 0;
    case 6:
      return (((x * y) % 2) + ((x * y) % 3)) % 2 == 0;
    default:
      return (((x + y) % 2) + ((x * y) % 3)) % 2 == 0;
    }
  }

  using MaskPatterns = std::array<std::array<uint64_t, MaskPeriod>, MaskCount>;

  //! [mask][y % MaskPeriod]: the modules of row y the mask inverts (QrSymbol's layout: module x is bit 63 - x).
  [[nodiscard]] constexpr MaskPatterns MakeMaskRowPatterns() noexcept
  {
    MaskPatterns patterns{};
    for (int32_t mask = 0; mask < MaskCount; ++mask)
    {
      for (std::size_t y = 0; y < MaskPeriod; ++y)
      {
        for (int32_t x = 0; x < QrSymbol::MaxSize; ++x)
        {
          if (MaskInverts(mask, x, static_cast<int32_t>(y)))
          {
            patterns[static_cast<std::size_t>(mask)][y] |= QrSymbol::Bit(x);
          }
        }
      }
    }
    return patterns;
  }

  //! [mask][x % MaskPeriod]: the modules of column x the mask inverts (module y is bit 63 - y).
  [[nodiscard]] constexpr MaskPatterns MakeMaskColumnPatterns() noexcept
  {
    MaskPatterns patterns{};
    for (int32_t mask = 0; mask < MaskCount; ++mask)
    {
      for (std::size_t x = 0; x < MaskPeriod; ++x)
      {
        for (int32_t y = 0; y < QrSymbol::MaxSize; ++y)
        {
          if (MaskInverts(mask, static_cast<int32_t>(x), y))
          {
            patterns[static_cast<std::size_t>(mask)][x] |= QrSymbol::Bit(y);
          }
        }
      }
    }
    return patterns;
  }

  //! The 15 format bits of error correction level M with a mask: the level (00) and the mask, a BCH(15,5) code, and the standard's
  //! XOR pattern.
  [[nodiscard]] constexpr uint32_t MakeFormatBits(const int32_t mask) noexcept
  {
    const auto data = static_cast<uint32_t>(mask);
    uint32_t remainder = data;
    for (int32_t i = 0; i < 10; ++i)
    {
      remainder = (remainder << 1u) ^ ((remainder >> 9u) * 0x537u);
    }
    return ((data << 10u) | remainder) ^ 0x5412u;
  }

  [[nodiscard]] constexpr std::array<uint32_t, MaskCount> MakeAllFormatBits() noexcept
  {
    std::array<uint32_t, MaskCount> bits{};
    for (int32_t mask = 0; mask < MaskCount; ++mask)
    {
      bits[static_cast<std::size_t>(mask)] = MakeFormatBits(mask);
    }
    return bits;
  }

  inline constexpr MaskPatterns MaskRowPatterns = MakeMaskRowPatterns();
  inline constexpr MaskPatterns MaskColumnPatterns = MakeMaskColumnPatterns();
  inline constexpr std::array<uint32_t, MaskCount> FormatBits = MakeAllFormatBits();
}

#endif
