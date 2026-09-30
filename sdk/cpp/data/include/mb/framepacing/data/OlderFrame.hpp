#ifndef MB_FRAMEPACING_DATA_OLDERFRAME_HPP
#define MB_FRAMEPACING_DATA_OLDERFRAME_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Data
{
  //! A capture that showed an older frame out of order, after the frame of its row was presented (frames CSV column olderFrames).
  struct OlderFrame
  {
    //! The older frame index the capture showed.
    uint64_t FrameIndex{0};
    //! When the capture was taken, in 100 ns ticks on the analysis's capture clock (as FrameRow::FirstSeenTicks).
    int64_t CaptureTicks{0};
  };
}

#endif
