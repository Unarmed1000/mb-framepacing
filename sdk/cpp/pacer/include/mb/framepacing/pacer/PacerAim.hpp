#ifndef MB_FRAMEPACING_PACER_PACERAIM_HPP
#define MB_FRAMEPACING_PACER_PACERAIM_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md "Every tier's pacer has two aims"). What a pacer optimizes for. Every
  //! tier's pacer has both, and what trades the one against the other belongs to the aim, never to a pacer as such.
  enum class PacerAim : uint8_t
  {
    //! Not latency optimized: the display is kept supplied. Frames that wait to be shown are a reserve (PacerSettings::WaitingPresents
    //! less one, at one refresh per frame), so where in a refresh a present lands matters less and a frame that runs a little long is
    //! covered. A frame reaches the screen that many refreshes later, which is a constant delay and does not show in the motion
    Smoothness = 0,
    //! Latency optimized: the frames that wait to be shown are kept as few as the tier can, and a frame that the reserve would have
    //! covered is a repeated one
    LowLatency = 1,
  };
}

#endif
