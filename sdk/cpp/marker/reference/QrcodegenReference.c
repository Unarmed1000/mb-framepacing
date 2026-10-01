// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// QrcodegenReference.h: the vendored QR Code generator library, compiled here as it is (this file includes its source, so its
// private functions can be called), for the tests and the benchmarks.
#include "QrcodegenReference.h"
#include <string.h>
#include "qrcodegen.c"

long QrcodegenReference_PenaltyScore(const uint8_t qrcode[])
{
  return getPenaltyScore(qrcode);
}

void QrcodegenReference_Clear(uint8_t qrcode[], int size)
{
  memset(qrcode, 0, (size_t)(((size * size) + 7) / 8 + 1));
  qrcode[0] = (uint8_t)size;
}

void QrcodegenReference_SetModule(uint8_t qrcode[], int x, int y, bool dark)
{
  setModuleBounded(qrcode, x, y, dark);
}
