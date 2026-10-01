#ifndef MB_FRAMEPACING_MARKER_DETAIL_QRENCODER_HPP
#define MB_FRAMEPACING_MARKER_DETAIL_QRENCODER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Private to the marker module: the QR code encoder for the marker's symbols (versions 2 and 6, error correction level M, byte mode,
// the mask with the lowest penalty). It gives exactly the symbols of the QR Code generator library (reference/third_party/qrcodegen, which the
// tests compare it with), and scores the masks on whole rows and columns instead of module by module (doc/encoding-performance.md).

#include <cstddef>
#include <cstdint>
#include <limits>
#include <span>
#include "QrSymbol.hpp"

namespace MB::FramePacing::Marker::QrEncoder
{
  //! PenaltyScore's limit for the whole score.
  inline constexpr int32_t NoLimit = std::numeric_limits<int32_t>::max();

  //! The symbol of data in byte mode at error correction level M, with the mask that scores the lowest penalty (the lowest numbered one
  //! of equals). Returns false, leaving rSymbol unchanged, when version is not 2 or 6 or data does not fit it (26 and 106 bytes).
  [[nodiscard]] bool Encode(std::span<const uint8_t> data, int32_t version, QrSymbol& rSymbol) noexcept;

  //! As Encode, with a given mask (0 to 7) instead of the best one. Also false for a mask outside 0 to 7.
  [[nodiscard]] bool EncodeWithMask(std::span<const uint8_t> data, int32_t version, int32_t mask, QrSymbol& rSymbol) noexcept;

  //! The QR standard's penalty score of a symbol: runs of five or more modules of one colour and finder-like patterns in every row and
  //! column, 2x2 blocks of one colour, and the balance of dark and light. The scoring stops once the score reaches limit: the result is
  //! then at least limit, and no longer the whole score.
  [[nodiscard]] int32_t PenaltyScore(const QrSymbol& symbol, int32_t limit = NoLimit) noexcept;

  //! The penalty of one line (a row or a column, QrSymbol's layout) of size modules: its runs and its finder-like patterns.
  [[nodiscard]] int32_t LinePenalty(uint64_t line, int32_t size) noexcept;

  //! How many finder-like patterns a line has, found by walking its runs, as the reference does. LinePenalty uses it for a line with
  //! nine or more dark modules in a row, and finds the patterns of every other line with a few word operations.
  [[nodiscard]] int32_t FinderPatternsByRuns(uint64_t line, int32_t size) noexcept;

  //! The symbol's modules packed as ModuleMatrix holds them: row-major, most significant bit first, continuous across rows, the last
  //! byte zero padded. dst holds at least ModuleMatrix::PackedModuleByteCount(symbol.Size) bytes.
  void PackModules(const QrSymbol& symbol, std::span<uint8_t> dst) noexcept;
}

#endif
