#ifndef MB_FRAMEPACING_PACER_FRAME_SYSTEMWAITKIND_HPP
#define MB_FRAMEPACING_PACER_FRAME_SYSTEMWAITKIND_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built). A wait of the application's own, one the
  //! pacer did not ask for, in which the system held the frame loop before a frame started (SystemWaitReport).
  enum class SystemWaitKind : uint8_t
  {
    //! A wait for a place among the frames in flight: until the GPU was done with an earlier frame
    FrameSlot = 0,
    //! A wait for the image the frame is drawn into: until the swap chain had one free
    Acquire = 1,
  };
}

#endif
