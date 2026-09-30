#ifndef MB_FRAMEPACING_DATA_CAPTUREDATASTATUS_HPP
#define MB_FRAMEPACING_DATA_CAPTUREDATASTATUS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Data
{
  //! What reading a capture's markers gave.
  enum class CaptureDataStatus : uint8_t
  {
    Undecodable = 0,
    Decoded = 1,
    //! The markers at different heights of the frame disagree (the capture shows parts of two frames).
    Torn = 2,
  };
}

#endif
