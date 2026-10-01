#ifndef MB_FRAMEPACING_CORE_BYTESPANUTIL_HPP
#define MB_FRAMEPACING_CORE_BYTESPANUTIL_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <bit>
#include <cassert>
#include <cstddef>
#include <cstdint>
#include <span>
#include <type_traits>

namespace MB::FramePacing::ByteSpanUtil
{
  //! A value ReadLE and WriteLE take: an integer (not bool) or a double.
  template <typename T>
  concept LittleEndianValue = (std::is_integral_v<T> && !std::is_same_v<T, bool>) || std::is_same_v<T, double>;

  //! Write value as sizeof(T) little-endian bytes at offset (a signed value as its two's complement, a double as its IEEE 754 bits, as
  //! C#'s BinaryPrimitives does). The byte count follows from the value's type. dst must hold them: asserted, not checked.
  template <LittleEndianValue T>
  constexpr void WriteLE(const std::span<uint8_t> dst, const std::size_t offset, const T value) noexcept
  {
    assert(offset <= dst.size() && dst.size() - offset >= sizeof(T));
    if constexpr (std::is_same_v<T, double>)
    {
      WriteLE(dst, offset, std::bit_cast<uint64_t>(value));
    }
    else
    {
      const auto bits = static_cast<uint64_t>(static_cast<std::make_unsigned_t<T>>(value));
      for (std::size_t i = 0; i < sizeof(T); ++i)
      {
        dst[offset + i] = static_cast<uint8_t>((bits >> (8u * i)) & 0xFFu);
      }
    }
  }

  //! Read a T from sizeof(T) little-endian bytes at offset: the counterpart of WriteLE. src must hold them: asserted, not checked.
  template <LittleEndianValue T>
  [[nodiscard]] constexpr T ReadLE(const std::span<const uint8_t> src, const std::size_t offset) noexcept
  {
    assert(offset <= src.size() && src.size() - offset >= sizeof(T));
    if constexpr (std::is_same_v<T, double>)
    {
      return std::bit_cast<double>(ReadLE<uint64_t>(src, offset));
    }
    else
    {
      uint64_t bits = 0;
      for (std::size_t i = 0; i < sizeof(T); ++i)
      {
        bits |= static_cast<uint64_t>(src[offset + i]) << (8u * i);
      }
      return static_cast<T>(static_cast<std::make_unsigned_t<T>>(bits));
    }
  }
}

#endif
