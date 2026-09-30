#ifndef MB_FRAMEPACING_PACER_FRAME_FRAMEEND_HPP
#define MB_FRAMEPACING_PACER_FRAME_FRAMEEND_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! What the application knows when it presents a frame (FramePacer::EndFrame).
  struct FrameEnd
  {
    //! When Present is called, on the steady clock of FrameInput (required).
    int64_t PresentTicks{0};
    //! How long the frame needed, as the swap interval rule should count it: the CPU's time, or the CPU's and the GPU's, or whatever the
    //! application measures. 0: the CPU busy time (PresentTicks - the frame's NowTicks).
    int64_t WorkTicks{0};
  };
}

#endif
