#ifndef MB_FRAMEPACING_MARKER_DETAIL_BITCOUNT_HPP
#define MB_FRAMEPACING_MARKER_DETAIL_BITCOUNT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Private to the marker module: the bit counts the QR encoder and the drawing take of a word. C++20's <bit> in a library built for
// C++20; in one built for C++17, which has no <bit>, plain constexpr code that gives the same counts (no intrinsics).

#include <mb/framepacing/core/Config.hpp>
#include <cstdint>
#include <limits>
#include <type_traits>
#if MB_FRAMEPACING_CXX_STANDARD >= 20
#include <bit>
#endif

namespace MB::FramePacing::Marker::BitCount
{
  //! The number of bits that are 1: std::popcount.
  template <typename T>
  [[nodiscard]] constexpr int32_t PopCount(const T value) noexcept
  {
    static_assert(std::is_unsigned_v<T> && sizeof(T) <= sizeof(uint64_t), "PopCount counts an unsigned word of at most 64 bits");
#if MB_FRAMEPACING_CXX_STANDARD >= 20
    return std::popcount(value);
#else
    // Counted in pairs, then nibbles, then bytes, and the bytes added up by the multiplication (into the highest byte)
    uint64_t bits = value;
    bits = bits - ((bits >> 1u) & 0x5555555555555555u);
    bits = (bits & 0x3333333333333333u) + ((bits >> 2u) & 0x3333333333333333u);
    bits = (bits + (bits >> 4u)) & 0x0F0F0F0F0F0F0F0Fu;
    return static_cast<int32_t>((bits * 0x0101010101010101u) >> 56u);
#endif
  }

  //! The number of 0 bits before the highest 1 bit, all of them for 0: std::countl_zero.
  template <typename T>
  [[nodiscard]] constexpr int32_t CountLeadingZeros(const T value) noexcept
  {
    static_assert(std::is_unsigned_v<T> && sizeof(T) <= sizeof(uint64_t), "CountLeadingZeros counts an unsigned word of at most 64 bits");
#if MB_FRAMEPACING_CXX_STANDARD >= 20
    return std::countl_zero(value);
#else
    constexpr int32_t Digits = std::numeric_limits<T>::digits;
    if (value == 0u)
    {
      return Digits;
    }
    // The word at the top of 64 bits, halved: each step that finds the upper part empty counts it and moves the rest up
    uint64_t bits = static_cast<uint64_t>(value) << (64 - Digits);
    int32_t count = 0;
    for (int32_t width = 32; width > 0; width /= 2)
    {
      if ((bits >> (64 - width)) == 0u)
      {
        count += width;
        bits <<= width;
      }
    }
    return count;
#endif
  }

  //! The number of 1 bits before the highest 0 bit, all of them for all ones: std::countl_one.
  template <typename T>
  [[nodiscard]] constexpr int32_t CountLeadingOnes(const T value) noexcept
  {
    return CountLeadingZeros(static_cast<T>(~value));
  }
}

#endif
