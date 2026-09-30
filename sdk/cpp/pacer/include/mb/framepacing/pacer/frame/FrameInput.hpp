#ifndef MB_FRAMEPACING_PACER_FRAME_FRAMEINPUT_HPP
#define MB_FRAMEPACING_PACER_FRAME_FRAMEINPUT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! What the application knows when it starts a frame (FramePacer::BeginFrame). All times are ticks on the application's steady clock
  //! (the platform's clock converted with TickCount64::FromNanoseconds / FromCounter, or a std::chrono clock through core/time/ChronoConversion.hpp):
  //! the pacer never reads a clock. NowTicks is required; everything else is what better platforms report, 0 = unknown (so a steady clock must not
  //! give a real time of exactly 0; they count from boot).
  struct FrameInput
  {
    //! Now: the frame's CPU start time (required).
    int64_t NowTicks{0};
    //! A vsync the platform reported, the latest one it knows: DWM's qpcVBlank, Choreographer's frame time, CADisplayLink's timestamp.
    //! It puts the pacer's refresh grid on the display's.
    int64_t VsyncTicks{0};
    //! When the previous frame was really shown, from presentation feedback (VK_GOOGLE_display_timing, VK_EXT_present_timing, DXGI
    //! frame statistics, EGL frame timestamps, Wayland presentation-time). Without it the pacer infers it from the previous Present.
    int64_t PreviousDisplayTicks{0};
    //! The display time the platform predicts for this frame (Choreographer's expected presentation time, OpenXR's
    //! predictedDisplayTime, CADisplayLink's targetTimestamp): the frame aims no earlier.
    int64_t PredictedDisplayTicks{0};
    //! The display's refresh period as the platform reports it now, in nanoseconds (Choreographer's refresh rate callback,
    //! VK_GOOGLE_display_timing's refreshDuration, VK_EXT_present_timing, CADisplayLink's duration). One that rounds to another nanosecond
    //! than the pacer's period is a display mode change: the pacer starts again at it (FramePacer::SetRefreshPeriod).
    int64_t RefreshPeriodNanoseconds{0};
  };
}

#endif
