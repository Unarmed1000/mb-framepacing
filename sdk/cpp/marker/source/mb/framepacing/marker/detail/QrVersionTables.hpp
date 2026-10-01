#ifndef MB_FRAMEPACING_MARKER_DETAIL_QRVERSIONTABLES_HPP
#define MB_FRAMEPACING_MARKER_DETAIL_QRVERSIONTABLES_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Private to the marker module: what is fixed for a QR version, made at compile time for the two versions the marker uses (2 and 6,
// error correction level M): which modules carry data, the function patterns, where the format bits go and the order the codeword bits
// are placed in. The layout is the QR standard's, as the QR Code generator library draws it (reference/third_party/qrcodegen, the tests' reference).

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <span>
#include "QrSymbol.hpp"

namespace MB::FramePacing::Marker::QrEncoder
{
  //! Error correction codewords per block: 16 for versions 2 and 6 at level M.
  inline constexpr int32_t EccCodewordsPerBlock = 16;

  //! The format information is 15 bits, drawn twice.
  inline constexpr int32_t FormatBitCount = 15;

  struct QrVersionTables
  {
    int32_t Version{0};
    //! Modules per side.
    int32_t Size{0};
    //! Codewords that carry data (the rest are error correction).
    int32_t DataCodewordCount{0};
    //! Error correction blocks; all have the same length for versions 2 and 6.
    int32_t BlockCount{0};
    //! 1 = a data module (a mask applies to it), as rows and as columns (QrSymbol's layout).
    std::array<uint64_t, QrSymbol::MaxSize> DataRows{};
    std::array<uint64_t, QrSymbol::MaxSize> DataColumns{};
    //! The function patterns' dark modules: finders, timing, alignment and the always dark module. The format bits are light.
    QrSymbol FunctionPatterns;
    //! The modules of the format bits, both copies: ModuleOf(x, y) with the format bit's index above (FormatBitOf).
    std::array<uint16_t, 2 * FormatBitCount> FormatModules{};
    //! Codeword bit i (the first codeword's highest bit first) goes to module CodewordModules[i]: ModuleOf(x, y).
    std::span<const uint16_t> CodewordModules;
  };

  //! A module's position in one value.
  [[nodiscard]] constexpr uint16_t ModuleOf(const int32_t x, const int32_t y) noexcept
  {
    return static_cast<uint16_t>((static_cast<uint32_t>(y) << 6u) | static_cast<uint32_t>(x));
  }

  [[nodiscard]] constexpr int32_t ModuleX(const uint16_t module) noexcept
  {
    return static_cast<int32_t>(module & 63u);
  }

  [[nodiscard]] constexpr int32_t ModuleY(const uint16_t module) noexcept
  {
    return static_cast<int32_t>((static_cast<uint32_t>(module) >> 6u) & 63u);
  }

  //! The format bit a FormatModules entry carries.
  [[nodiscard]] constexpr uint32_t FormatBitOf(const uint16_t module) noexcept
  {
    return static_cast<uint32_t>(module) >> 12u;
  }

  //! How many rings (x, y) is away from a pattern's centre.
  [[nodiscard]] constexpr int32_t RingDistance(const int32_t x, const int32_t y, const int32_t centerX, const int32_t centerY) noexcept
  {
    const int32_t dx = x > centerX ? x - centerX : centerX - x;
    const int32_t dy = y > centerY ? y - centerY : centerY - y;
    return std::max(dx, dy);
  }

  //! Module (x, y) of a symbol of size modules belongs to a function pattern: timing, the three finders with their separators and
  //! format bits, or the alignment pattern (one, at size - 7, for versions 2 to 6).
  [[nodiscard]] constexpr bool IsFunctionModule(const int32_t size, const int32_t x, const int32_t y) noexcept
  {
    if (x == 6 || y == 6)
    {
      return true;
    }
    if ((x < 9 && y < 9) || (x >= size - 8 && y < 9) || (x < 9 && y >= size - 8))
    {
      return true;
    }
    return RingDistance(x, y, size - 7, size - 7) <= 2;
  }

  //! A function module's colour, before a mask's format bits are drawn (they are light until then).
  [[nodiscard]] constexpr bool IsDarkFunctionModule(const int32_t size, const int32_t x, const int32_t y) noexcept
  {
    // A finder: a dark 3x3 centre, a light ring, a dark ring and the light separator
    const int32_t farCenter = size - 4;
    const int32_t finder = std::min({RingDistance(x, y, 3, 3), RingDistance(x, y, farCenter, 3), RingDistance(x, y, 3, farCenter)});
    if (finder <= 4)
    {
      return finder != 2 && finder != 4;
    }
    // The alignment pattern: a dark centre, a light ring and a dark ring
    const int32_t alignment = RingDistance(x, y, size - 7, size - 7);
    if (alignment <= 2)
    {
      return alignment != 1;
    }
    // The timing patterns alternate, dark first
    if (x == 6)
    {
      return y % 2 == 0;
    }
    if (y == 6)
    {
      return x % 2 == 0;
    }
    // What is left are the format bits, and the module above the lower left finder that is always dark
    return x == 8 && y == size - 8;
  }

  //! The modules of the 15 format bits' two copies.
  [[nodiscard]] constexpr std::array<uint16_t, 2 * FormatBitCount> MakeFormatModules(const int32_t size) noexcept
  {
    std::array<uint16_t, 2 * FormatBitCount> modules{};
    for (int32_t i = 0; i < FormatBitCount; ++i)
    {
      const auto bit = static_cast<uint16_t>(static_cast<uint32_t>(i) << 12u);
      // The first copy goes around the upper left finder, skipping the timing patterns
      uint16_t first = ModuleOf(14 - i, 8);
      if (i < 6)
      {
        first = ModuleOf(8, i);
      }
      else if (i < 8)
      {
        first = ModuleOf(8, i + 1);
      }
      else if (i == 8)
      {
        first = ModuleOf(7, 8);
      }
      // The second copy is split: below the upper right finder, and beside the lower left one
      const uint16_t second = i < 8 ? ModuleOf(size - 1 - i, 8) : ModuleOf(8, size - 15 + i);
      modules[static_cast<std::size_t>(i)] = static_cast<uint16_t>(bit | first);
      modules[static_cast<std::size_t>(FormatBitCount) + static_cast<std::size_t>(i)] = static_cast<uint16_t>(bit | second);
    }
    return modules;
  }

  //! The modules the codeword bits are placed on, in order: the standard's zigzag, two columns at a time from the right, alternating
  //! upward and downward, skipping the function modules. TBitCount is the version's codewords times 8 (the data modules left over stay
  //! light).
  template <std::size_t TBitCount>
  [[nodiscard]] constexpr std::array<uint16_t, TBitCount> MakeCodewordModules(const int32_t version) noexcept
  {
    std::array<uint16_t, TBitCount> modules{};
    const int32_t size = (4 * version) + 17;
    std::size_t count = 0;
    for (int32_t right = size - 1; right >= 1; right -= 2)
    {
      if (right == 6)
      {
        // The vertical timing pattern's column is not part of a pair
        right = 5;
      }
      const bool upward = ((right + 1) & 2) == 0;
      for (int32_t step = 0; step < size; ++step)
      {
        const int32_t y = upward ? size - 1 - step : step;
        for (int32_t x = right; x >= right - 1; --x)
        {
          if (!IsFunctionModule(size, x, y) && count < TBitCount)
          {
            modules[count] = ModuleOf(x, y);
            ++count;
          }
        }
      }
    }
    return modules;
  }

  [[nodiscard]] constexpr QrVersionTables MakeVersionTables(const int32_t version, const int32_t dataCodewordCount, const int32_t blockCount,
                                                            const std::span<const uint16_t> codewordModules) noexcept
  {
    QrVersionTables tables;
    const int32_t size = (4 * version) + 17;
    tables.Version = version;
    tables.Size = size;
    tables.DataCodewordCount = dataCodewordCount;
    tables.BlockCount = blockCount;
    tables.FunctionPatterns.Size = size;
    for (int32_t y = 0; y < size; ++y)
    {
      for (int32_t x = 0; x < size; ++x)
      {
        if (!IsFunctionModule(size, x, y))
        {
          tables.DataRows[static_cast<std::size_t>(y)] |= QrSymbol::Bit(x);
          tables.DataColumns[static_cast<std::size_t>(x)] |= QrSymbol::Bit(y);
        }
        else if (IsDarkFunctionModule(size, x, y))
        {
          tables.FunctionPatterns.SetDark(x, y);
        }
      }
    }
    tables.FormatModules = MakeFormatModules(size);
    tables.CodewordModules = codewordModules;
    return tables;
  }

  //! Version 2-M: 44 codewords, 28 of them data, in one block. Version 6-M: 172 codewords, 108 of them data, in four blocks.
  inline constexpr int32_t Version2CodewordCount = 44;
  inline constexpr int32_t Version6CodewordCount = 172;
  inline constexpr std::array<uint16_t, Version2CodewordCount * 8> Version2CodewordModules = MakeCodewordModules<Version2CodewordCount * 8>(2);
  inline constexpr std::array<uint16_t, Version6CodewordCount * 8> Version6CodewordModules = MakeCodewordModules<Version6CodewordCount * 8>(6);
  inline constexpr QrVersionTables Version2Tables = MakeVersionTables(2, 28, 1, Version2CodewordModules);
  inline constexpr QrVersionTables Version6Tables = MakeVersionTables(6, 108, 4, Version6CodewordModules);

  static_assert(Version2Tables.DataCodewordCount + (Version2Tables.BlockCount * EccCodewordsPerBlock) == Version2CodewordCount);
  static_assert(Version6Tables.DataCodewordCount + (Version6Tables.BlockCount * EccCodewordsPerBlock) == Version6CodewordCount);
  static_assert(Version6Tables.DataCodewordCount % Version6Tables.BlockCount == 0, "every block has the same length");
  static_assert(Version6Tables.Size == QrSymbol::MaxSize);
}

#endif
