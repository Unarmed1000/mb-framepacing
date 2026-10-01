// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The marker's QR code encoder: QrEncoder.hpp. The symbol layout, the error correction and the penalty rules are those of the QR Code
// generator library, so both give the same symbols:
//
// Based on the QR Code generator library, https://www.nayuki.io/page/qr-code-generator-library
// Copyright (c) Project Nayuki. (MIT License)
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// - The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
// - The Software is provided "as is", without warranty of any kind, express or implied, including but not limited to the warranties of
//   merchantability, fitness for a particular purpose and noninfringement. In no event shall the authors or copyright holders be liable
//   for any claim, damages or other liability, whether in an action of contract, tort or otherwise, arising from, out of or in connection
//   with the Software or the use or other dealings in the Software.
#include "QrEncoder.hpp"
#include <algorithm>
#include <array>
#include <bit>
#include <cstddef>
#include <cstdint>
#include <span>
#include "QrMaskPatterns.hpp"
#include "QrReedSolomon.hpp"
#include "QrSymbol.hpp"
#include "QrVersionTables.hpp"

namespace MB::FramePacing::Marker::QrEncoder
{
  namespace
  {
    // The QR standard's penalty weights: runs, 2x2 blocks, finder-like patterns, balance
    constexpr int32_t PenaltyN1 = 3;
    constexpr int32_t PenaltyN2 = 3;
    constexpr int32_t PenaltyN3 = 40;
    constexpr int32_t PenaltyN4 = 10;

    // LinePenalty works on a line moved this many bits down: eight light modules above it (and at least fifteen below) stand for the
    // light border the standard gives every line
    constexpr uint32_t LinePad = 8;

    constexpr const QrVersionTables* TablesFor(const int32_t version) noexcept
    {
      if (version == Version6Tables.Version)
      {
        return &Version6Tables;
      }
      return version == Version2Tables.Version ? &Version2Tables : nullptr;
    }

    int32_t PopCount(const uint64_t value) noexcept
    {
      return std::popcount(value);
    }

    //! The symbol of data before a mask: the function patterns (format bits light) and the codewords. False when data does not fit.
    bool BuildUnmasked(const std::span<const uint8_t> data, const QrVersionTables& tables, QrSymbol& rSymbol) noexcept
    {
      const auto capacity = static_cast<std::size_t>(tables.DataCodewordCount);
      // Byte mode (4 bits), the byte count (8 bits) and the terminator (4 bits) take two codewords
      if (data.size() + 2u > capacity)
      {
        return false;
      }

      // The data codewords: the mode and the count put every byte half a byte further, then the pad bytes alternate
      std::array<uint8_t, Version6CodewordCount> codewords{};
      auto previous = static_cast<uint32_t>(data.size());
      codewords[0] = static_cast<uint8_t>(0x40u | (previous >> 4u));
      std::size_t count = 1;
      for (const uint8_t value : data)
      {
        codewords[count] = static_cast<uint8_t>((previous << 4u) | (static_cast<uint32_t>(value) >> 4u));
        previous = value;
        ++count;
      }
      codewords[count] = static_cast<uint8_t>(previous << 4u);
      ++count;
      for (uint8_t pad = 0xEC; count < capacity; pad = static_cast<uint8_t>(pad ^ 0xECu ^ 0x11u))
      {
        codewords[count] = pad;
        ++count;
      }

      // Every block's error correction, and the blocks interleaved: all first codewords, all second ones, ...
      std::array<uint8_t, Version6CodewordCount> interleaved{};
      const auto blockCount = static_cast<std::size_t>(tables.BlockCount);
      const std::size_t blockLength = capacity / blockCount;
      for (std::size_t block = 0; block < blockCount; ++block)
      {
        const std::span<const uint8_t> blockData = std::span<const uint8_t>(codewords).subspan(block * blockLength, blockLength);
        std::array<uint8_t, EccCodewordsPerBlock> errorCorrection{};
        ComputeErrorCorrection(blockData, errorCorrection);
        for (std::size_t i = 0; i < blockLength; ++i)
        {
          interleaved[(i * blockCount) + block] = blockData[i];
        }
        for (std::size_t i = 0; i < errorCorrection.size(); ++i)
        {
          interleaved[capacity + (i * blockCount) + block] = errorCorrection[i];
        }
      }

      // The codeword bits onto their modules
      rSymbol = tables.FunctionPatterns;
      const std::span<const uint16_t> modules = tables.CodewordModules;
      for (std::size_t i = 0; i < modules.size(); ++i)
      {
        const uint64_t bit = (static_cast<uint64_t>(interleaved[i >> 3u]) >> (7u - (i & 7u))) & 1u;
        const uint16_t module = modules[i];
        const auto x = static_cast<std::size_t>(ModuleX(module));
        const auto y = static_cast<std::size_t>(ModuleY(module));
        rSymbol.Rows[y] |= bit << (63u - x);
        rSymbol.Columns[x] |= bit << (63u - y);
      }
      return true;
    }

    //! unmasked with a mask applied to its data modules, and the mask's format bits drawn.
    void ApplyMask(const QrVersionTables& tables, const QrSymbol& unmasked, const int32_t mask, QrSymbol& rSymbol) noexcept
    {
      const auto& rowPatterns = MaskRowPatterns[static_cast<std::size_t>(mask)];
      const auto& columnPatterns = MaskColumnPatterns[static_cast<std::size_t>(mask)];
      const auto size = static_cast<std::size_t>(tables.Size);
      rSymbol.Size = tables.Size;
      for (std::size_t i = 0; i < size; ++i)
      {
        rSymbol.Rows[i] = unmasked.Rows[i] ^ (rowPatterns[i % MaskPeriod] & tables.DataRows[i]);
        rSymbol.Columns[i] = unmasked.Columns[i] ^ (columnPatterns[i % MaskPeriod] & tables.DataColumns[i]);
      }
      const uint32_t format = FormatBits[static_cast<std::size_t>(mask)];
      for (const uint16_t module : tables.FormatModules)
      {
        const uint64_t bit = (format >> FormatBitOf(module)) & 1u;
        const auto x = static_cast<std::size_t>(ModuleX(module));
        const auto y = static_cast<std::size_t>(ModuleY(module));
        rSymbol.Rows[y] |= bit << (63u - x);
        rSymbol.Columns[x] |= bit << (63u - y);
      }
    }
  }

  bool Encode(const std::span<const uint8_t> data, const int32_t version, QrSymbol& rSymbol) noexcept
  {
    const QrVersionTables* const pTables = TablesFor(version);
    QrSymbol unmasked;
    if (pTables == nullptr || !BuildUnmasked(data, *pTables, unmasked))
    {
      return false;
    }
    // The first mask with the lowest penalty. A mask's scoring stops when it reaches the best score so far: it can not win any more
    int32_t bestScore = NoLimit;
    QrSymbol candidate;
    for (int32_t mask = 0; mask < MaskCount; ++mask)
    {
      ApplyMask(*pTables, unmasked, mask, candidate);
      const int32_t score = PenaltyScore(candidate, bestScore);
      if (score < bestScore)
      {
        bestScore = score;
        rSymbol = candidate;
      }
    }
    return true;
  }

  bool EncodeWithMask(const std::span<const uint8_t> data, const int32_t version, const int32_t mask, QrSymbol& rSymbol) noexcept
  {
    const QrVersionTables* const pTables = TablesFor(version);
    QrSymbol unmasked;
    if (pTables == nullptr || mask < 0 || mask >= MaskCount || !BuildUnmasked(data, *pTables, unmasked))
    {
      return false;
    }
    ApplyMask(*pTables, unmasked, mask, rSymbol);
    return true;
  }

  int32_t FinderPatternsByRuns(uint64_t line, const int32_t size) noexcept
  {
    // The last seven runs, the newest first; a line starts and ends in a light border of its own length
    std::array<int32_t, 7> history{};
    const auto addRun = [&history, size](int32_t length) noexcept
    {
      if (history[0] == 0)
      {
        length += size;
      }
      std::copy_backward(history.begin(), history.end() - 1, history.end());
      history[0] = length;
    };
    // After a light run: dark, light, dark, light, dark runs of 1:1:3:1:1 before it, with four times the unit of light on one side and
    // at least the unit on the other
    const auto countPatterns = [&history]() noexcept
    {
      const int32_t unit = history[1];
      if (unit <= 0 || history[2] != unit || history[3] != unit * 3 || history[4] != unit || history[5] != unit)
      {
        return 0;
      }
      return (history[0] >= unit * 4 && history[6] >= unit ? 1 : 0) + (history[6] >= unit * 4 && history[0] >= unit ? 1 : 0);
    };

    int32_t patterns = 0;
    int32_t remaining = size;
    bool dark = false;
    for (;;)
    {
      // The first run is light, and empty when the line starts dark
      const int32_t length = std::min(dark ? std::countl_one(line) : std::countl_zero(line), remaining);
      if (length == remaining)
      {
        if (dark)
        {
          addRun(length);
          addRun(size);
        }
        else
        {
          addRun(length + size);
        }
        return patterns + countPatterns();
      }
      addRun(length);
      if (!dark)
      {
        patterns += countPatterns();
      }
      line <<= static_cast<uint32_t>(length);
      remaining -= length;
      dark = !dark;
    }
  }

  int32_t LinePenalty(const uint64_t line, const int32_t size) noexcept
  {
    // Module i is bit 55 - i of padded
    const uint64_t padded = line >> LinePad;
    const uint64_t light = ~padded;

    // Runs of five or more of one colour: PenaltyN1 for the first five modules and 1 for each further one. A run of n modules has
    // n - 4 places where five in a row start, so it scores those places plus PenaltyN1 - 1
    const uint64_t pairs = ((uint64_t{1} << static_cast<uint32_t>(size - 1)) - 1u) << static_cast<uint32_t>(56 - size);
    const uint64_t same = ~(padded ^ (padded >> 1u)) & pairs;
    const uint64_t fives = same & (same >> 1u) & (same >> 2u) & (same >> 3u);
    const int32_t runs = PopCount(fives) + ((PenaltyN1 - 1) * PopCount(fives & ~(fives << 1u)));

    // Finder-like patterns of a larger unit than 2 have a dark run of nine or more: those lines are walked run by run
    const uint64_t dark2 = padded & (padded >> 1u);
    const uint64_t dark4 = dark2 & (dark2 >> 2u);
    const uint64_t dark8 = dark4 & (dark4 >> 4u);
    if ((dark8 & (padded >> 8u)) != 0u)
    {
      return runs + (FinderPatternsByRuns(line, size) * PenaltyN3);
    }

    // The light modules next to a pattern that starts at a bit: the 2, 4 and 8 below it
    const uint64_t below2 = (light << 1u) & (light << 2u);
    const uint64_t below4 = below2 & (below2 << 2u);
    const uint64_t below8 = below4 & (below4 << 4u);

    // Unit 1: dark, light, three dark, light, dark, between light modules; it counts once for four light modules on either side
    const uint64_t core1 = padded & (light >> 1u) & (dark2 >> 2u) & (padded >> 4u) & (light >> 5u) & (padded >> 6u) & (light << 1u) & (light >> 7u);
    const uint64_t above1x2 = (light >> 7u) & (light >> 8u);
    const uint64_t above1x4 = above1x2 & (above1x2 >> 2u);

    // Unit 2: every run twice as long, between two light modules on either side; eight on a side to count
    const uint64_t light2 = light & (light >> 1u);
    const uint64_t above2x2 = light2 >> 14u;
    const uint64_t above2x4 = above2x2 & (above2x2 >> 2u);
    const uint64_t above2x8 = above2x4 & (above2x4 >> 4u);
    const uint64_t core2 = dark2 & (light2 >> 2u) & (dark4 >> 4u) & (dark2 >> 8u) & (light2 >> 10u) & (dark2 >> 12u) & below2 & above2x2;

    const int32_t patterns = PopCount(core1 & below4) + PopCount(core1 & above1x4) + PopCount(core2 & below8) + PopCount(core2 & above2x8);
    return runs + (patterns * PenaltyN3);
  }

  int32_t PenaltyScore(const QrSymbol& symbol, const int32_t limit) noexcept
  {
    const auto size = static_cast<std::size_t>(symbol.Size);

    // 2x2 blocks of one colour, and the dark modules, from the rows
    const uint64_t pairs = ~uint64_t{0} << static_cast<uint32_t>(65 - symbol.Size);
    uint64_t previousRow = symbol.Rows[0];
    uint64_t previousSame = ~(previousRow ^ (previousRow << 1u)) & pairs;
    int32_t dark = PopCount(previousRow);
    int32_t blocks = 0;
    for (std::size_t y = 1; y < size; ++y)
    {
      const uint64_t row = symbol.Rows[y];
      const uint64_t same = ~(row ^ (row << 1u)) & pairs;
      blocks += PopCount(same & previousSame & ~(row ^ previousRow));
      dark += PopCount(row);
      previousRow = row;
      previousSame = same;
    }
    // The balance: 10 for every 5 % the dark modules are away from 45 % to 55 %
    const int32_t total = symbol.Size * symbol.Size;
    const int32_t imbalance = dark * 20 > total * 10 ? (dark * 20) - (total * 10) : (total * 10) - (dark * 20);
    int32_t score = (blocks * PenaltyN2) + ((((imbalance + total - 1) / total) - 1) * PenaltyN4);

    // The runs and the finder-like patterns of every row and column
    for (std::size_t i = 0; i < size; ++i)
    {
      if (score >= limit)
      {
        return score;
      }
      score += LinePenalty(symbol.Rows[i], symbol.Size) + LinePenalty(symbol.Columns[i], symbol.Size);
    }
    return score;
  }

  void PackModules(const QrSymbol& symbol, const std::span<uint8_t> dst) noexcept
  {
    const auto size = static_cast<uint32_t>(symbol.Size);
    uint64_t pending = 0;
    uint32_t pendingBits = 0;
    std::size_t count = 0;
    for (uint32_t y = 0; y < size; ++y)
    {
      // A row's modules are its word's highest bits: at most 7 bits wait from the rows before
      pending = (pending << size) | (symbol.Rows[y] >> (64u - size));
      pendingBits += size;
      while (pendingBits >= 8u)
      {
        pendingBits -= 8u;
        dst[count] = static_cast<uint8_t>(pending >> pendingBits);
        ++count;
      }
    }
    // A QR symbol's module count is odd: one module is left, in the last byte's highest bit
    dst[count] = static_cast<uint8_t>(pending << (8u - pendingBits));
  }
}
