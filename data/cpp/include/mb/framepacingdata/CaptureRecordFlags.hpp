#ifndef MB_FRAMEPACINGDATA_CAPTURERECORDFLAGS_HPP
#define MB_FRAMEPACINGDATA_CAPTURERECORDFLAGS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacingData
{
  //! What the capture source reported about a capture (bit flags).
  struct CaptureRecordFlags
  {
    //! The capture source reported dropping frames between the previous record and this one.
    static constexpr uint32_t SourceDropBefore = 1u;
  };
}

#endif
