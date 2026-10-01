// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// libFuzzer target: the marker's QR encoder against the reference (the vendored qrcodegen), with inputs the fuzzer chooses to reach
// every part of the encoder. Any difference aborts, and the fuzzer keeps the input that caused it.
//
// The first byte says what the rest is:
//   bit 7 clear  a payload: bit 0 picks the version (2 or 6); it is encoded with the best mask and with each of the eight masks, and the
//                symbols and penalty scores are compared. A payload that does not fit must be refused by both.
//   bit 7 set    a drawn symbol: bit 0 picks its size (25 or 41 modules), the bytes are its rows (6 bytes each, missing ones light);
//                the penalty scores are compared.
//
// Built with -DMB_FRAMEPACING_BUILD_FUZZERS=ON (Clang): mb_framepacing_marker_qr_fuzz [corpus dir] -max_len=256 -max_total_time=60
#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <span>
#include "QrcodegenReference.h"
#include "mb/framepacing/marker/detail/QrEncoder.hpp"
#include "mb/framepacing/marker/detail/QrSymbol.hpp"

namespace QR = MB::FramePacing::Marker::QrEncoder;

namespace
{
  using ReferenceSymbol = std::array<uint8_t, qrcodegen_BUFFER_LEN_FOR_VERSION(6)>;

  [[noreturn]] void Fail(const char* const what)
  {
    std::fprintf(stderr, "DIFFERENCE: %s\n", what);
    std::abort();
  }

  bool ReferenceEncode(const std::span<const uint8_t> data, const int32_t version, const qrcodegen_Mask mask, ReferenceSymbol& rSymbol)
  {
    ReferenceSymbol dataAndTemp{};
    std::copy(data.begin(), data.end(), dataAndTemp.begin());
    return qrcodegen_encodeBinary(dataAndTemp.data(), data.size(), rSymbol.data(), qrcodegen_Ecc_MEDIUM, version, version, mask, false);
  }

  void ExpectSameSymbol(const QR::QrSymbol& symbol, const ReferenceSymbol& reference)
  {
    const int32_t size = qrcodegen_getSize(reference.data());
    if (symbol.Size != size)
    {
      Fail("the symbols' sizes");
    }
    for (int32_t y = 0; y < size; ++y)
    {
      for (int32_t x = 0; x < size; ++x)
      {
        const bool expected = qrcodegen_getModule(reference.data(), x, y);
        const bool inColumn = (symbol.Columns[static_cast<std::size_t>(x)] & QR::QrSymbol::Bit(y)) != 0u;
        if (symbol.IsDark(x, y) != expected || inColumn != expected)
        {
          Fail("a module");
        }
      }
    }
  }

  void FuzzPayload(const int32_t version, std::span<const uint8_t> payload)
  {
    // Up to a few bytes more than fit: both must refuse those
    payload = payload.first(std::min<std::size_t>(payload.size(), 110));
    ReferenceSymbol reference{};
    QR::QrSymbol symbol;
    const bool referenceEncoded = ReferenceEncode(payload, version, qrcodegen_Mask_AUTO, reference);
    if (QR::Encode(payload, version, symbol) != referenceEncoded)
    {
      Fail("only one encoder took the payload");
    }
    if (!referenceEncoded)
    {
      return;
    }
    ExpectSameSymbol(symbol, reference);
    for (int32_t mask = 0; mask < 8; ++mask)
    {
      if (!ReferenceEncode(payload, version, static_cast<qrcodegen_Mask>(mask), reference) || !QR::EncodeWithMask(payload, version, mask, symbol))
      {
        Fail("a mask was refused");
      }
      ExpectSameSymbol(symbol, reference);
      if (QR::PenaltyScore(symbol) != QrcodegenReference_PenaltyScore(reference.data()))
      {
        Fail("a masked symbol's penalty score");
      }
    }
  }

  void FuzzDrawnSymbol(const int32_t size, const std::span<const uint8_t> rows)
  {
    QR::QrSymbol symbol;
    symbol.Size = size;
    ReferenceSymbol reference{};
    QrcodegenReference_Clear(reference.data(), size);
    for (int32_t y = 0; y < size; ++y)
    {
      for (int32_t x = 0; x < size; ++x)
      {
        const std::size_t byte = (static_cast<std::size_t>(y) * 6u) + (static_cast<std::size_t>(x) / 8u);
        if (byte < rows.size() && ((rows[byte] >> (static_cast<uint32_t>(x) % 8u)) & 1u) != 0u)
        {
          symbol.SetDark(x, y);
          QrcodegenReference_SetModule(reference.data(), x, y, true);
        }
      }
    }
    if (QR::PenaltyScore(symbol) != QrcodegenReference_PenaltyScore(reference.data()))
    {
      Fail("a drawn symbol's penalty score");
    }
  }
}

extern "C" int LLVMFuzzerTestOneInput(const uint8_t* const pData, const std::size_t size)
{
  if (size == 0)
  {
    return 0;
  }
  const std::span<const uint8_t> input(pData, size);
  const uint8_t mode = input[0];
  if ((mode & 0x80u) == 0u)
  {
    FuzzPayload((mode & 1u) != 0u ? 2 : 6, input.subspan(1));
  }
  else
  {
    FuzzDrawnSymbol((mode & 1u) != 0u ? 25 : 41, input.subspan(1));
  }
  return 0;
}
