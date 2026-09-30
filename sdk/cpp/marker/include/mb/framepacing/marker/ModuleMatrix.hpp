#ifndef MB_FRAMEPACING_MARKER_MODULEMATRIX_HPP
#define MB_FRAMEPACING_MARKER_MODULEMATRIX_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/marker/Constants.hpp>
#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <span>

namespace MB::FramePacing::Marker
{
  //! The encoded marker: the QR symbol's modules, 1 bit each (1 = dark), packed row-major, most significant bit first, continuous across
  //! rows, the last byte zero padded (exactly test-data/markers/modules.csv's modulesHex). GenerateModules fills it once per marker; every
  //! drawing output (ModulesToQuads, ModulesToTriangles, ModulesToIndexed, ModulesToBitmap) is made from it. A plain value (211 bytes):
  //! it lives on the stack or as a member, never on the heap.
  class ModuleMatrix
  {
    int32_t m_size{0};
    std::array<uint8_t, MaxPackedModuleByteCount> m_bits{};

  public:
    //! Modules per side: 41 for the main marker, 25 for the sync marker, 0 before a successful GenerateModules.
    [[nodiscard]] constexpr int32_t Size() const noexcept
    {
      return m_size;
    }

    [[nodiscard]] constexpr bool IsDark(const int32_t x, const int32_t y) const noexcept
    {
      const auto index = (static_cast<std::size_t>(y) * static_cast<std::size_t>(m_size)) + static_cast<std::size_t>(x);
      return ((static_cast<uint32_t>(m_bits[index / 8u]) >> (7u - static_cast<uint32_t>(index % 8u))) & 1u) != 0u;
    }

    //! The packed bits: PackedModuleByteCount(Size()) bytes.
    [[nodiscard]] constexpr std::span<const uint8_t> Bits() const noexcept
    {
      return std::span<const uint8_t>(m_bits).first(PackedModuleByteCount(m_size));
    }

    //! A matrix of Size x Size modules from packed bits (at least PackedModuleByteCount(size) bytes; bits past the last module are
    //! ignored). Returns false, leaving rMatrix unchanged, for a size that is not a QR symbol's (21 to 41, in steps of 4) or too few bytes.
    static constexpr bool TryFromBits(const int32_t size, const std::span<const uint8_t> bits, ModuleMatrix& rMatrix) noexcept
    {
      const std::size_t byteCount = PackedModuleByteCount(size);
      if (size < 21 || size > QrModuleCount || (size - 17) % 4 != 0 || bits.size() < byteCount)
      {
        return false;
      }
      ModuleMatrix matrix;
      matrix.m_size = size;
      std::copy_n(bits.begin(), byteCount, matrix.m_bits.begin());
      // Zero the padding, so equal symbols compare equal
      const auto usedBits = static_cast<uint32_t>((static_cast<std::size_t>(size) * static_cast<std::size_t>(size)) % 8u);
      if (usedBits != 0u)
      {
        matrix.m_bits[byteCount - 1u] = static_cast<uint8_t>(matrix.m_bits[byteCount - 1u] & static_cast<uint8_t>(0xFFu << (8u - usedBits)));
      }
      rMatrix = matrix;
      return true;
    }

    constexpr bool operator==(const ModuleMatrix& other) const noexcept
    {
      return m_size == other.m_size && std::ranges::equal(Bits(), other.Bits());
    }
  };
}

#endif
