#ifndef MB_FRAMEPACING_MARKER_TESTS_REFERENCE_QRCODEGENREFERENCE_H
#define MB_FRAMEPACING_MARKER_TESTS_REFERENCE_QRCODEGENREFERENCE_H
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// The reference the marker's QR encoder is compared with and measured against: the QR Code generator library as it is vendored
// (third_party/qrcodegen next to this file, unchanged), plus what the library keeps to itself and the tests need: the penalty score
// of its mask selection, and drawing a symbol module by module. Tests and benchmarks only; the marker library does not contain it.

#include <stdbool.h>
#include <stdint.h>
#include "qrcodegen.h"

#ifdef __cplusplus
extern "C"
{
#endif

  // The penalty score qrcodegen's mask selection gives a symbol.
  long QrcodegenReference_PenaltyScore(const uint8_t qrcode[]);

  // qrcode as an all light symbol of size modules per side (a buffer of qrcodegen_BUFFER_LEN_FOR_VERSION bytes for its version).
  void QrcodegenReference_Clear(uint8_t qrcode[], int size);

  // The colour of module (x, y).
  void QrcodegenReference_SetModule(uint8_t qrcode[], int x, int y, bool dark);

#ifdef __cplusplus
}
#endif

#endif
