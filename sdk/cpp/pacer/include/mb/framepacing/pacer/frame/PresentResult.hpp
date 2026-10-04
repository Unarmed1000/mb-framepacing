#ifndef MB_FRAMEPACING_PACER_FRAME_PRESENTRESULT_HPP
#define MB_FRAMEPACING_PACER_FRAME_PRESENTRESULT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer.md). What the platform's present feedback says became of a frame (PresentFeedback).
  //! A frame the platform says nothing about, or gives no display time for, gets no feedback at all.
  enum class PresentResult : uint8_t
  {
    //! The frame was shown, at the display time given.
    Shown = 0,
    //! The platform says the frame was never shown: discarded, or replaced before the display took it (Wayland's "discarded"; a
    //! VK_EXT_present_timing result that is complete without a display time, on the one driver tried). It is counted
    //! (PresentFeedbackState::NotShown).
    NotShown = 1,
  };
}

#endif
