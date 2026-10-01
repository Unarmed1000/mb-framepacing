#ifndef MB_FRAMEPACING_MARKER_DETAIL_QRREEDSOLOMON_HPP
#define MB_FRAMEPACING_MARKER_DETAIL_QRREEDSOLOMON_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Private to the marker module: the QR code's Reed-Solomon error correction over GF(2^8) modulo 0x11D, for blocks with 16 error
// correction codewords. The arithmetic is the QR Code generator library's (reference/third_party/qrcodegen, the tests' reference); the
// multiplications go through logarithm tables made at compile time instead of eight shift and add steps each.

#include <array>
#include <cstddef>
#include <cstdint>
#include <span>
#include "QrVersionTables.hpp"

namespace MB::FramePacing::Marker::QrEncoder
{
  //! The product of two field elements (shift and add; the tables below replace it at run time).
  [[nodiscard]] constexpr uint8_t GaloisMultiply(const uint8_t x, const uint8_t y) noexcept
  {
    uint32_t z = 0;
    for (int32_t i = 7; i >= 0; --i)
    {
      z = ((z << 1u) ^ ((z >> 7u) * 0x11Du)) & 0xFFu;
      z ^= ((static_cast<uint32_t>(y) >> static_cast<uint32_t>(i)) & 1u) * x;
    }
    return static_cast<uint8_t>(z);
  }

  //! Powers of the generator element 2, twice over: the sum of two logarithms indexes it without a modulo.
  inline constexpr std::size_t GaloisExpCount = 510;

  [[nodiscard]] constexpr std::array<uint8_t, GaloisExpCount> MakeGaloisExp() noexcept
  {
    std::array<uint8_t, GaloisExpCount> exp{};
    uint8_t value = 1;
    for (std::size_t i = 0; i < GaloisExpCount; ++i)
    {
      exp[i] = value;
      value = GaloisMultiply(value, 2);
    }
    return exp;
  }

  //! The logarithm of every element but 0 (which has none: its entry is 0 and never read).
  [[nodiscard]] constexpr std::array<uint8_t, 256> MakeGaloisLog() noexcept
  {
    std::array<uint8_t, 256> log{};
    const std::array<uint8_t, GaloisExpCount> exp = MakeGaloisExp();
    for (std::size_t i = 0; i < 255; ++i)
    {
      log[exp[i]] = static_cast<uint8_t>(i);
    }
    return log;
  }

  //! The generator polynomial of degree 16, (x - 2^0)(x - 2^1)...(x - 2^15), highest power first, without the leading 1.
  [[nodiscard]] constexpr std::array<uint8_t, EccCodewordsPerBlock> MakeDivisor() noexcept
  {
    std::array<uint8_t, EccCodewordsPerBlock> divisor{};
    divisor[EccCodewordsPerBlock - 1] = 1;
    uint8_t root = 1;
    for (std::size_t i = 0; i < EccCodewordsPerBlock; ++i)
    {
      for (std::size_t j = 0; j < EccCodewordsPerBlock; ++j)
      {
        divisor[j] = GaloisMultiply(divisor[j], root);
        if (j + 1 < EccCodewordsPerBlock)
        {
          divisor[j] = static_cast<uint8_t>(divisor[j] ^ divisor[j + 1]);
        }
      }
      root = GaloisMultiply(root, 2);
    }
    return divisor;
  }

  inline constexpr std::array<uint8_t, GaloisExpCount> GaloisExp = MakeGaloisExp();
  inline constexpr std::array<uint8_t, 256> GaloisLog = MakeGaloisLog();
  inline constexpr std::array<uint8_t, EccCodewordsPerBlock> Divisor = MakeDivisor();

  //! The divisor's coefficients as logarithms (none of them is 0).
  [[nodiscard]] constexpr std::array<uint8_t, EccCodewordsPerBlock> MakeDivisorLog() noexcept
  {
    std::array<uint8_t, EccCodewordsPerBlock> log{};
    for (std::size_t i = 0; i < EccCodewordsPerBlock; ++i)
    {
      log[i] = GaloisLog[Divisor[i]];
    }
    return log;
  }

  inline constexpr std::array<uint8_t, EccCodewordsPerBlock> DivisorLog = MakeDivisorLog();

  //! The 16 error correction codewords of a block: the remainder of data divided by the generator polynomial.
  constexpr void ComputeErrorCorrection(const std::span<const uint8_t> data, const std::span<uint8_t, EccCodewordsPerBlock> rRemainder) noexcept
  {
    std::array<uint8_t, EccCodewordsPerBlock> remainder{};
    for (const uint8_t value : data)
    {
      const auto factor = static_cast<uint8_t>(value ^ remainder[0]);
      for (std::size_t i = 0; i + 1 < EccCodewordsPerBlock; ++i)
      {
        remainder[i] = remainder[i + 1];
      }
      remainder[EccCodewordsPerBlock - 1] = 0;
      if (factor != 0)
      {
        const std::size_t factorLog = GaloisLog[factor];
        for (std::size_t i = 0; i < EccCodewordsPerBlock; ++i)
        {
          remainder[i] = static_cast<uint8_t>(remainder[i] ^ GaloisExp[DivisorLog[i] + factorLog]);
        }
      }
    }
    for (std::size_t i = 0; i < EccCodewordsPerBlock; ++i)
    {
      rRemainder[i] = remainder[i];
    }
  }
}

#endif
