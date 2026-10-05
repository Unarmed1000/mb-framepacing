// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The marker's own QR encoder against the reference it must agree with: the vendored QR Code generator library (qrcodegen). Every
// symbol module by module, every mask's penalty score, and the pieces the encoder is made of.
#include <mb/framepacing/marker/geometry/ModuleMatrix.hpp>
#include <gtest/gtest.h>
#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <span>
#include <string>
#include <vector>
#include "QrcodegenReference.h"
#include "mb/framepacing/marker/detail/QrEncoder.hpp"
#include "mb/framepacing/marker/detail/QrMaskPatterns.hpp"
#include "mb/framepacing/marker/detail/QrReedSolomon.hpp"
#include "mb/framepacing/marker/detail/QrSymbol.hpp"
#include "mb/framepacing/marker/detail/QrVersionTables.hpp"

namespace FM = MB::FramePacing::Marker;
namespace QR = MB::FramePacing::Marker::QrEncoder;

namespace
{
  using ReferenceSymbol = std::array<uint8_t, qrcodegen_BUFFER_LEN_FOR_VERSION(6)>;

  constexpr std::array<int32_t, 2> Versions = {2, 6};

  //! The bytes each version holds in byte mode at level M.
  constexpr std::size_t CapacityOf(const int32_t version)
  {
    return version == 2 ? 26u : 106u;
  }

  //! A small deterministic generator (xorshift): the same payloads and symbols on every run.
  class Random
  {
    uint64_t m_state;

  public:
    explicit Random(const uint64_t seed)
      : m_state(seed)
    {
    }

    uint64_t Next()
    {
      m_state ^= m_state << 13u;
      m_state ^= m_state >> 7u;
      m_state ^= m_state << 17u;
      return m_state;
    }

    std::vector<uint8_t> Bytes(const std::size_t count)
    {
      std::vector<uint8_t> bytes(count);
      for (uint8_t& rByte : bytes)
      {
        rByte = static_cast<uint8_t>(Next() >> 32u);
      }
      return bytes;
    }
  };

  //! The reference's symbol of data, with its automatic mask or a given one.
  bool ReferenceEncode(const std::span<const uint8_t> data, const int32_t version, const qrcodegen_Mask mask, ReferenceSymbol& rSymbol)
  {
    // qrcodegen takes the data in a buffer it overwrites
    ReferenceSymbol dataAndTemp{};
    std::copy(data.begin(), data.end(), dataAndTemp.begin());
    return qrcodegen_encodeBinary(dataAndTemp.data(), data.size(), rSymbol.data(), qrcodegen_Ecc_MEDIUM, version, version, mask, false);
  }

  //! Both encoders hold the same symbol, and ours holds it the same as rows and as columns.
  ::testing::AssertionResult SameSymbol(const QR::QrSymbol& symbol, const ReferenceSymbol& reference)
  {
    const int32_t size = qrcodegen_getSize(reference.data());
    if (symbol.Size != size)
    {
      return ::testing::AssertionFailure() << "size " << symbol.Size << ", the reference's is " << size;
    }
    for (int32_t y = 0; y < size; ++y)
    {
      for (int32_t x = 0; x < size; ++x)
      {
        const bool expected = qrcodegen_getModule(reference.data(), x, y);
        const bool inColumn = (symbol.Columns[static_cast<std::size_t>(x)] & QR::QrSymbol::Bit(y)) != 0u;
        if (symbol.IsDark(x, y) != expected || inColumn != expected)
        {
          return ::testing::AssertionFailure() << "module (" << x << ", " << y << ") is " << (expected ? "dark" : "light") << " in the reference";
        }
      }
    }
    return ::testing::AssertionSuccess();
  }

  //! A symbol drawn module by module, in both encoders' forms.
  struct DrawnSymbol
  {
    QR::QrSymbol Ours;
    ReferenceSymbol Reference{};

    explicit DrawnSymbol(const int32_t size)
    {
      Ours.Size = size;
      QrcodegenReference_Clear(Reference.data(), size);
    }

    void SetDark(const int32_t x, const int32_t y)
    {
      Ours.SetDark(x, y);
      QrcodegenReference_SetModule(Reference.data(), x, y, true);
    }
  };

  //! A line (QrSymbol's layout) from its runs, the first one light: {2, 1, 3} is two light, one dark, three light modules.
  uint64_t LineOf(const std::vector<int32_t>& runs)
  {
    uint64_t line = 0;
    int32_t position = 0;
    bool dark = false;
    for (const int32_t run : runs)
    {
      for (int32_t i = 0; i < run; ++i, ++position)
      {
        if (dark)
        {
          line |= QR::QrSymbol::Bit(position);
        }
      }
      dark = !dark;
    }
    return line;
  }

  //! The penalty of a line as the reference scores a row: module by module, with its run history.
  int32_t LinePenaltyByModules(const uint64_t line, const int32_t size)
  {
    std::array<int32_t, 7> history{};
    const auto addRun = [&history, size](int32_t length)
    {
      if (history[0] == 0)
      {
        length += size;
      }
      std::copy_backward(history.begin(), history.end() - 1, history.end());
      history[0] = length;
    };
    const auto countPatterns = [&history]()
    {
      const int32_t n = history[1];
      const bool core = n > 0 && history[2] == n && history[3] == n * 3 && history[4] == n && history[5] == n;
      return (core && history[0] >= n * 4 && history[6] >= n ? 1 : 0) + (core && history[6] >= n * 4 && history[0] >= n ? 1 : 0);
    };

    int32_t result = 0;
    bool runColor = false;
    int32_t run = 0;
    for (int32_t x = 0; x < size; ++x)
    {
      const bool dark = (line & QR::QrSymbol::Bit(x)) != 0u;
      if (dark == runColor)
      {
        ++run;
        if (run == 5)
        {
          result += 3;
        }
        else if (run > 5)
        {
          ++result;
        }
      }
      else
      {
        addRun(run);
        if (!runColor)
        {
          result += countPatterns() * 40;
        }
        runColor = dark;
        run = 1;
      }
    }
    if (runColor)
    {
      addRun(run);
      run = 0;
    }
    addRun(run + size);
    return result + (countPatterns() * 40);
  }

  //! Lines with finder-like patterns of every unit that fits, with every amount of light on either side that matters, at the line's
  //! start, after a dark module, followed by alternating modules or by a long dark run.
  std::vector<uint64_t> FinderLikeLines(const int32_t size)
  {
    std::vector<uint64_t> lines;
    for (int32_t unit = 1; unit * 7 <= size; ++unit)
    {
      for (int32_t prefix = 0; prefix <= 1; ++prefix)
      {
        for (int32_t left = 0; left <= (unit * 4) + 1; ++left)
        {
          for (int32_t right = 0; right <= (unit * 4) + 1; ++right)
          {
            const int32_t used = prefix + left + (unit * 7) + right;
            if (used > size || (prefix == 1 && left == 0))
            {
              continue;
            }
            // light, [dark], light, the core, light
            std::vector<int32_t> runs = {0, prefix, left, unit, unit, unit * 3, unit, unit, right};
            if (prefix == 0)
            {
              runs = {left, unit, unit, unit * 3, unit, unit, right};
            }
            const std::vector<int32_t> core = runs;
            // ... then alternating single modules
            for (int32_t i = used; i < size && right > 0; ++i)
            {
              runs.push_back(1);
            }
            lines.push_back(LineOf(runs));
            // ... or one dark run to the end
            if (right > 0 && used < size)
            {
              runs = core;
              runs.push_back(size - used);
              lines.push_back(LineOf(runs));
            }
          }
        }
      }
    }
    return lines;
  }

  //! Random lines of several densities; every fourth one with a dark run of nine or more somewhere.
  std::vector<uint64_t> RandomLines(const int32_t size, const std::size_t count, Random& rRandom)
  {
    std::vector<uint64_t> lines;
    const uint64_t lineBits = ~uint64_t{0} << static_cast<uint32_t>(64 - size);
    for (std::size_t i = 0; i < count; ++i)
    {
      uint64_t line = rRandom.Next();
      if (i % 5 == 1)
      {
        line &= rRandom.Next();
      }
      else if (i % 5 == 2)
      {
        line |= rRandom.Next();
      }
      if (i % 4 == 3)
      {
        const auto start = static_cast<uint32_t>(rRandom.Next() % static_cast<uint64_t>(size - 8));
        const auto length = static_cast<uint32_t>(9u + (rRandom.Next() % 4u));
        line |= (~uint64_t{0} << (64u - length)) >> start;
      }
      lines.push_back(line & lineBits);
    }
    return lines;
  }
}

TEST(QrSymbol, AModuleIsInItsRowAndItsColumn)
{
  QR::QrSymbol symbol;
  symbol.Size = 25;
  EXPECT_FALSE(symbol.IsDark(3, 7));

  symbol.SetDark(3, 7);

  EXPECT_TRUE(symbol.IsDark(3, 7));
  EXPECT_FALSE(symbol.IsDark(7, 3));
  EXPECT_EQ(symbol.Rows[7], uint64_t{1} << 60u);
  EXPECT_EQ(symbol.Columns[3], uint64_t{1} << 56u);
}

TEST(QrVersionTables, MadeAtRunTimeEqualTheCompileTimeOnes)
{
  EXPECT_EQ(QR::MakeCodewordModules<QR::Version2CodewordCount * 8>(2), QR::Version2CodewordModules);
  EXPECT_EQ(QR::MakeCodewordModules<QR::Version6CodewordCount * 8>(6), QR::Version6CodewordModules);

  for (const QR::QrVersionTables* pExpected : {&QR::Version2Tables, &QR::Version6Tables})
  {
    const QR::QrVersionTables tables =
      QR::MakeVersionTables(pExpected->Version, pExpected->DataCodewordCount, pExpected->BlockCount, pExpected->CodewordModules);

    EXPECT_EQ(tables.Version, pExpected->Version);
    EXPECT_EQ(tables.Size, (4 * pExpected->Version) + 17);
    EXPECT_EQ(tables.DataCodewordCount, pExpected->DataCodewordCount);
    EXPECT_EQ(tables.BlockCount, pExpected->BlockCount);
    EXPECT_EQ(tables.DataRows, pExpected->DataRows);
    EXPECT_EQ(tables.DataColumns, pExpected->DataColumns);
    EXPECT_EQ(tables.FunctionPatterns.Size, pExpected->Size);
    EXPECT_EQ(tables.FunctionPatterns.Rows, pExpected->FunctionPatterns.Rows);
    EXPECT_EQ(tables.FunctionPatterns.Columns, pExpected->FunctionPatterns.Columns);
    EXPECT_EQ(tables.FormatModules, pExpected->FormatModules);
    EXPECT_EQ(tables.CodewordModules.data(), pExpected->CodewordModules.data());
  }
}

TEST(QrVersionTables, EveryDataModuleCarriesOneCodewordBitOrIsARemainderBit)
{
  for (const QR::QrVersionTables* pTables : {&QR::Version2Tables, &QR::Version6Tables})
  {
    QR::QrSymbol placed;
    for (const uint16_t module : pTables->CodewordModules)
    {
      const int32_t x = QR::ModuleX(module);
      const int32_t y = QR::ModuleY(module);
      EXPECT_FALSE(QR::IsFunctionModule(pTables->Size, x, y));
      EXPECT_FALSE(placed.IsDark(x, y)) << "a module used twice";
      placed.SetDark(x, y);
    }
    int32_t dataModules = 0;
    for (int32_t y = 0; y < pTables->Size; ++y)
    {
      for (int32_t x = 0; x < pTables->Size; ++x)
      {
        dataModules += QR::IsFunctionModule(pTables->Size, x, y) ? 0 : 1;
      }
    }
    // Versions 2 to 6 have seven data modules more than their codewords need
    EXPECT_EQ(dataModules, static_cast<int32_t>(pTables->CodewordModules.size()) + 7);
  }
}

TEST(QrMaskPatterns, MadeAtRunTimeEqualTheCompileTimeOnes)
{
  EXPECT_EQ(QR::MakeMaskRowPatterns(), QR::MaskRowPatterns);
  EXPECT_EQ(QR::MakeMaskColumnPatterns(), QR::MaskColumnPatterns);
  EXPECT_EQ(QR::MakeAllFormatBits(), QR::FormatBits);
  // Level M with mask 0 and mask 7, as the standard's table of format information has them
  EXPECT_EQ(QR::FormatBits[0], 0x5412u);
  EXPECT_EQ(QR::FormatBits[7], 0x4AA0u);
}

TEST(QrMaskPatterns, RepeatEveryTwelveRowsAndColumns)
{
  for (int32_t mask = 0; mask < QR::MaskCount; ++mask)
  {
    for (int32_t y = 0; y < QR::QrSymbol::MaxSize; ++y)
    {
      for (int32_t x = 0; x < QR::QrSymbol::MaxSize; ++x)
      {
        const bool inverts = QR::MaskInverts(mask, x, y);
        const uint64_t row = QR::MaskRowPatterns[static_cast<std::size_t>(mask)][static_cast<std::size_t>(y) % QR::MaskPeriod];
        const uint64_t column = QR::MaskColumnPatterns[static_cast<std::size_t>(mask)][static_cast<std::size_t>(x) % QR::MaskPeriod];
        ASSERT_EQ((row & QR::QrSymbol::Bit(x)) != 0u, inverts) << "mask " << mask << " row " << y << " module " << x;
        ASSERT_EQ((column & QR::QrSymbol::Bit(y)) != 0u, inverts) << "mask " << mask << " column " << x << " module " << y;
      }
    }
  }
}

TEST(QrReedSolomon, TablesMadeAtRunTimeEqualTheCompileTimeOnes)
{
  EXPECT_EQ(QR::MakeGaloisExp(), QR::GaloisExp);
  EXPECT_EQ(QR::MakeGaloisLog(), QR::GaloisLog);
  EXPECT_EQ(QR::MakeDivisor(), QR::Divisor);
  EXPECT_EQ(QR::MakeDivisorLog(), QR::DivisorLog);
}

TEST(QrReedSolomon, TheLogarithmsMultiply)
{
  EXPECT_TRUE(std::ranges::none_of(QR::Divisor, [](const uint8_t value) { return value == 0; })) << "every coefficient has a logarithm";
  for (uint32_t x = 1; x < 256; ++x)
  {
    for (uint32_t y = 1; y < 256; ++y)
    {
      const std::size_t logSum = static_cast<std::size_t>(QR::GaloisLog[x]) + QR::GaloisLog[y];
      ASSERT_EQ(QR::GaloisExp[logSum], QR::GaloisMultiply(static_cast<uint8_t>(x), static_cast<uint8_t>(y))) << x << " * " << y;
    }
  }
  EXPECT_EQ(QR::GaloisMultiply(0, 0x53), 0);
  EXPECT_EQ(QR::GaloisMultiply(0x53, 0), 0);
}

TEST(QrReedSolomon, ZeroDataHasAZeroRemainder)
{
  const std::array<uint8_t, 27> data{};
  std::array<uint8_t, QR::EccCodewordsPerBlock> remainder{};
  remainder.fill(0xFF);

  QR::ComputeErrorCorrection(data, remainder);

  EXPECT_EQ(remainder, (std::array<uint8_t, QR::EccCodewordsPerBlock>{}));
}

TEST(QrEncoder, GivesTheReferencesSymbolForEveryLength)
{
  Random random(0x9E3779B97F4A7C15u);
  for (const int32_t version : Versions)
  {
    for (std::size_t length = 0; length <= CapacityOf(version); ++length)
    {
      // Random bytes, and the regular ones a marker is mostly made of
      const std::vector<std::vector<uint8_t>> payloads = {random.Bytes(length), std::vector<uint8_t>(length, 0x00),
                                                          std::vector<uint8_t>(length, 0xFF), std::vector<uint8_t>(length, 0x55),
                                                          std::vector<uint8_t>(length, 0x0F)};
      for (const std::vector<uint8_t>& payload : payloads)
      {
        ReferenceSymbol reference{};
        ASSERT_TRUE(ReferenceEncode(payload, version, qrcodegen_Mask_AUTO, reference));
        QR::QrSymbol symbol;

        ASSERT_TRUE(QR::Encode(payload, version, symbol));

        ASSERT_TRUE(SameSymbol(symbol, reference)) << "version " << version << ", " << length << " bytes of " << (payload.empty() ? 0 : payload[0]);
      }
    }
  }
}

TEST(QrEncoder, GivesTheReferencesSymbolForRandomPayloads)
{
  Random random(0xD1B54A32D192ED03u);
  for (const int32_t version : Versions)
  {
    for (int32_t i = 0; i < 1500; ++i)
    {
      // Mostly the lengths the markers have: 20 bytes in version 2; 57 and 81 in version 6
      const std::size_t length =
        i % 3 == 0 ? static_cast<std::size_t>(random.Next() % (CapacityOf(version) + 1u)) : (version == 2 ? 20u : (i % 3 == 1 ? 57u : 81u));
      std::vector<uint8_t> payload = random.Bytes(length);
      if (i % 4 == 0)
      {
        // As a marker: mostly zero bytes
        for (std::size_t k = 0; k < payload.size(); ++k)
        {
          payload[k] = k % 5 == 0 ? payload[k] : uint8_t{0};
        }
      }
      ReferenceSymbol reference{};
      ASSERT_TRUE(ReferenceEncode(payload, version, qrcodegen_Mask_AUTO, reference));
      QR::QrSymbol symbol;

      ASSERT_TRUE(QR::Encode(payload, version, symbol));

      ASSERT_TRUE(SameSymbol(symbol, reference)) << "version " << version << ", payload " << i;
    }
  }
}

TEST(QrEncoder, EveryMaskGivesTheReferencesSymbolAndPenalty)
{
  Random random(0xA24BAED4963EE407u);
  for (const int32_t version : Versions)
  {
    for (int32_t i = 0; i < 300; ++i)
    {
      const std::vector<uint8_t> payload = random.Bytes(static_cast<std::size_t>(random.Next() % (CapacityOf(version) + 1u)));
      for (int32_t mask = 0; mask < QR::MaskCount; ++mask)
      {
        ReferenceSymbol reference{};
        ASSERT_TRUE(ReferenceEncode(payload, version, static_cast<qrcodegen_Mask>(mask), reference));
        QR::QrSymbol symbol;

        ASSERT_TRUE(QR::EncodeWithMask(payload, version, mask, symbol));

        ASSERT_TRUE(SameSymbol(symbol, reference)) << "version " << version << ", payload " << i << ", mask " << mask;
        ASSERT_EQ(QR::PenaltyScore(symbol), QrcodegenReference_PenaltyScore(reference.data()))
          << "version " << version << ", payload " << i << ", mask " << mask;
      }
    }
  }
}

TEST(QrEncoder, TheFirstOfEqualMasksWins)
{
  // Payloads whose two best masks score the same: the lower numbered one is taken, as the reference takes it
  Random random(0x2545F4914F6CDD1Du);
  int32_t ties = 0;
  for (int32_t i = 0; i < 20000 && ties < 5; ++i)
  {
    const std::vector<uint8_t> payload = random.Bytes(16);
    std::array<int32_t, QR::MaskCount> scores{};
    for (int32_t mask = 0; mask < QR::MaskCount; ++mask)
    {
      QR::QrSymbol masked;
      ASSERT_TRUE(QR::EncodeWithMask(payload, 2, mask, masked));
      scores[static_cast<std::size_t>(mask)] = QR::PenaltyScore(masked);
    }
    const auto best = static_cast<int32_t>(std::ranges::min_element(scores) - scores.begin());
    if (std::ranges::count(scores, scores[static_cast<std::size_t>(best)]) < 2)
    {
      continue;
    }
    ++ties;
    QR::QrSymbol expected;
    ASSERT_TRUE(QR::EncodeWithMask(payload, 2, best, expected));
    ReferenceSymbol reference{};
    ASSERT_TRUE(ReferenceEncode(payload, 2, qrcodegen_Mask_AUTO, reference));
    QR::QrSymbol symbol;

    ASSERT_TRUE(QR::Encode(payload, 2, symbol));

    EXPECT_EQ(symbol.Rows, expected.Rows);
    EXPECT_TRUE(SameSymbol(symbol, reference));
  }
  EXPECT_EQ(ties, 5) << "no payloads with equal best masks were found";
}

TEST(QrEncoder, RefusesOtherVersionsAndDataThatDoesNotFit)
{
  const std::vector<uint8_t> payload(27, 0x11);
  QR::QrSymbol symbol;
  symbol.Size = -1;

  for (const int32_t version : {0, 1, 3, 5, 7, 40})
  {
    EXPECT_FALSE(QR::Encode(std::span<const uint8_t>(payload).first(4), version, symbol)) << version;
    EXPECT_FALSE(QR::EncodeWithMask(std::span<const uint8_t>(payload).first(4), version, 0, symbol)) << version;
  }
  EXPECT_FALSE(QR::Encode(payload, 2, symbol)) << "27 bytes in version 2";
  EXPECT_FALSE(QR::EncodeWithMask(payload, 2, 0, symbol));
  EXPECT_FALSE(QR::Encode(std::vector<uint8_t>(107, 0x11), 6, symbol)) << "107 bytes in version 6";
  EXPECT_EQ(symbol.Size, -1) << "a refused symbol is left as it was";

  EXPECT_TRUE(QR::Encode(std::span<const uint8_t>(payload).first(26), 2, symbol));
  EXPECT_EQ(symbol.Size, 25);
}

TEST(QrEncoder, RefusesAMaskOutsideZeroToSeven)
{
  const std::array<uint8_t, 4> payload = {1, 2, 3, 4};
  QR::QrSymbol symbol;

  EXPECT_FALSE(QR::EncodeWithMask(payload, 6, -1, symbol));
  EXPECT_FALSE(QR::EncodeWithMask(payload, 6, 8, symbol));
  EXPECT_EQ(symbol.Size, 0);
  EXPECT_TRUE(QR::EncodeWithMask(payload, 6, 7, symbol));
  EXPECT_EQ(symbol.Size, 41);
}

TEST(QrEncoder, LinePenaltyEqualsScoringModuleByModule)
{
  Random random(0x94D049BB133111EBu);
  for (const int32_t size : {25, 41})
  {
    std::vector<uint64_t> lines = FinderLikeLines(size);
    const std::vector<uint64_t> randomLines = RandomLines(size, 200000, random);
    lines.insert(lines.end(), randomLines.begin(), randomLines.end());
    // Nothing, everything, and single runs at either end
    lines.push_back(0);
    lines.push_back(~uint64_t{0} << static_cast<uint32_t>(64 - size));
    lines.push_back(LineOf({0, 9, size - 9}));
    lines.push_back(LineOf({size - 9, 9}));
    for (const uint64_t line : lines)
    {
      ASSERT_EQ(QR::LinePenalty(line, size), LinePenaltyByModules(line, size)) << "size " << size << ", line " << std::hex << line;
    }
  }
}

TEST(QrEncoder, FinderPatternsByRunsFindsEveryUnit)
{
  // 1:1:3:1:1 with four units of light on one side counts once, on both sides twice, with less than one unit on a side not at all
  EXPECT_EQ(QR::FinderPatternsByRuns(LineOf({4, 1, 1, 3, 1, 1, 4, 1, 1, 1, 1, 1, 1, 1, 1}), 25), 2);
  EXPECT_EQ(QR::FinderPatternsByRuns(LineOf({0, 1, 1, 3, 1, 1, 1, 1, 1, 1, 1, 1}), 25), 1) << "the border is light";
  EXPECT_EQ(QR::FinderPatternsByRuns(LineOf({0, 1, 3, 1, 1, 3, 1, 1, 3, 1, 1}), 25), 0);
  EXPECT_EQ(QR::FinderPatternsByRuns(LineOf({0, 3, 3, 9, 3, 3, 4}), 25), 2) << "unit 3, to both borders";
  EXPECT_EQ(QR::FinderPatternsByRuns(LineOf({0, 1, 2, 3, 3, 9, 3, 3, 12, 5}), 41), 0) << "two light modules before a unit of 3";
  EXPECT_EQ(QR::FinderPatternsByRuns(LineOf({0, 1, 3, 3, 3, 9, 3, 3, 12, 4}), 41), 1);
  EXPECT_EQ(QR::FinderPatternsByRuns(LineOf({12, 3, 3, 9, 3, 3, 2, 6}), 41), 0) << "two light modules after a unit of 3";
  EXPECT_EQ(QR::FinderPatternsByRuns(LineOf({0, 5, 5, 15, 5, 5, 6}), 41), 2) << "unit 5";
  EXPECT_EQ(QR::FinderPatternsByRuns(0, 41), 0);
  EXPECT_EQ(QR::FinderPatternsByRuns(~uint64_t{0} << 23u, 41), 0);
}

TEST(QrEncoder, PenaltyScoreEqualsTheReferencesForDrawnSymbols)
{
  Random random(0xBF58476D1CE4E5B9u);
  for (const int32_t size : {25, 41})
  {
    const std::vector<uint64_t> crafted = FinderLikeLines(size);
    for (int32_t i = 0; i < 4000; ++i)
    {
      DrawnSymbol drawn(size);
      const int32_t kind = i % 8;
      const auto first = static_cast<std::size_t>(random.Next() % crafted.size());
      for (int32_t y = 0; y < size; ++y)
      {
        const uint64_t randomBits = random.Next();
        for (int32_t x = 0; x < size; ++x)
        {
          const bool randomBit = ((randomBits >> static_cast<uint32_t>(x)) & 1u) != 0u;
          const uint64_t line = crafted[(first + static_cast<std::size_t>(kind == 6 ? y : x)) % crafted.size()];
          bool dark = randomBit;
          switch (kind)
          {
          case 0:    // sparse
            dark = randomBit && ((randomBits >> static_cast<uint32_t>(x + 20)) & 1u) != 0u;
            break;
          case 1:    // dense
            dark = randomBit || ((randomBits >> static_cast<uint32_t>(x + 20)) & 1u) != 0u;
            break;
          case 2:    // vertical stripes, five wide
            dark = ((x / 5) & 1) != 0;
            break;
          case 3:    // blocks
            dark = (((y / 4) + (x / 7)) & 1) != 0;
            break;
          case 4:    // all dark but a few
            dark = (x * y) % 11 != 0;
            break;
          case 6:    // finder-like rows
            dark = (line & QR::QrSymbol::Bit(x)) != 0u;
            break;
          case 7:    // finder-like columns
            dark = (line & QR::QrSymbol::Bit(y)) != 0u;
            break;
          default:    // random
            break;
          }
          if (dark)
          {
            drawn.SetDark(x, y);
          }
        }
      }

      ASSERT_EQ(QR::PenaltyScore(drawn.Ours), QrcodegenReference_PenaltyScore(drawn.Reference.data())) << "size " << size << ", symbol " << i;
    }
  }
}

TEST(QrEncoder, PenaltyScoreStopsAtTheLimit)
{
  const std::array<uint8_t, 5> payload = {0x4D, 0x46, 0x01, 0x00, 0x2A};
  QR::QrSymbol symbol;
  ASSERT_TRUE(QR::EncodeWithMask(payload, 6, 3, symbol));
  const int32_t whole = QR::PenaltyScore(symbol);
  ASSERT_GT(whole, 100);

  // Above the score nothing stops; at or below it the result is at least the limit, and not more than the whole score
  EXPECT_EQ(QR::PenaltyScore(symbol, whole + 1), whole);
  for (const int32_t limit : {0, 1, whole / 3, whole / 2, whole - 1})
  {
    const int32_t stopped = QR::PenaltyScore(symbol, limit);
    EXPECT_GE(stopped, limit) << limit;
    EXPECT_LE(stopped, whole) << limit;
  }
  EXPECT_LT(QR::PenaltyScore(symbol, 0), whole) << "a limit of 0 stops before the first line";
}

TEST(QrEncoder, PackModulesIsRowMajorMostSignificantBitFirst)
{
  Random random(0x3C6EF372FE94F82Bu);
  for (const int32_t version : Versions)
  {
    QR::QrSymbol symbol;
    ASSERT_TRUE(QR::Encode(random.Bytes(12), version, symbol));
    std::array<uint8_t, FM::ModuleMatrix::MaxPackedModuleByteCount> expected{};
    std::size_t index = 0;
    for (int32_t y = 0; y < symbol.Size; ++y)
    {
      for (int32_t x = 0; x < symbol.Size; ++x, ++index)
      {
        if (symbol.IsDark(x, y))
        {
          expected[index / 8u] = static_cast<uint8_t>(expected[index / 8u] | (0x80u >> (index % 8u)));
        }
      }
    }
    std::array<uint8_t, FM::ModuleMatrix::MaxPackedModuleByteCount> packed{};
    packed.fill(0xFF);

    QR::PackModules(symbol, packed);

    const std::size_t byteCount = FM::ModuleMatrix::PackedModuleByteCount(symbol.Size);
    EXPECT_TRUE(std::equal(packed.begin(), packed.begin() + static_cast<std::ptrdiff_t>(byteCount), expected.begin())) << "version " << version;
  }
}
