#ifndef MB_FRAMEPACING_DATA_ANALYSIS_OLDERFRAME_HPP
#define MB_FRAMEPACING_DATA_ANALYSIS_OLDERFRAME_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TickCount64.hpp>
#include <cstdint>

namespace MB::FramePacing::Data
{
  //! A capture that showed an older frame out of order, after the frame of its row was presented (frames CSV column olderFrames).
  struct OlderFrame
  {
    //! The older frame index the capture showed.
    uint64_t FrameIndex{0};
    //! When the capture was taken, on the capture's clock (as FrameRow::FirstSeenTime).
    TickCount64 CaptureTime;
  };
}

#endif
