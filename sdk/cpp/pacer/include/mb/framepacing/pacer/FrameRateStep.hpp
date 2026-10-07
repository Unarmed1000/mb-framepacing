#ifndef MB_FRAMEPACING_PACER_FRAMERATESTEP_HPP
#define MB_FRAMEPACING_PACER_FRAMERATESTEP_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTimeSpan.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer.md). A frame rate a display with a fixed refresh rate can show every frame at: a
  //! frame every so many refreshes. On 60 Hz the steps are 60, 30 and 20 frames a second; 50 is none, as no whole number of
  //! refreshes is 20 ms. FrameRateStepUtil lists a display's steps and gives the step a frame rate becomes.
  struct FrameRateStep
  {
    //! The refreshes a frame is on screen for: what PacerSettings::SetPreferredSwapInterval takes.
    uint32_t SwapInterval{1};
    //! The time of one frame, to the nanosecond: what PacerSettings::SetPreferredFrameTime takes.
    NanosecondTimeSpan FrameTime;
    //! The frame rate in millihertz, the nearest: 59'940 is 59.94 frames a second. A number to show, in a menu for one.
    uint32_t RateMillihertz{0};

    constexpr bool operator==(const FrameRateStep&) const noexcept = default;
  };
}

#endif
