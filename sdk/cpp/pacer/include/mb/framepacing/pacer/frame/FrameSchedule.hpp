#ifndef MB_FRAMEPACING_PACER_FRAME_FRAMESCHEDULE_HPP
#define MB_FRAMEPACING_PACER_FRAME_FRAMESCHEDULE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! What the pacer plans for a frame (FramePacer::BeginFrame): the values to apply, and the marker's pacing fields. Times are ticks on
  //! the steady clock of FrameInput.
  struct FrameSchedule
  {
    //! The pacer's count of frames, from 0.
    uint64_t FrameIndex{0};
    //! The refresh the pacer aims for this frame to be shown at: the marker's intended display time, and the present time for a
    //! scheduled present (VK_GOOGLE_display_timing's desiredPresentTime, VK_EXT_present_timing, EGL_ANDROID_presentation_time, Metal's
    //! present(at:)).
    int64_t IntendedDisplayTicks{0};
    //! The refresh before the intended one: with plain FIFO vsync, a frame presented at or after it (and before the intended one) is
    //! shown at the intended one. Sleep until it, then present.
    int64_t EarliestPresentTicks{0};
    //! Refreshes from the previous frame's display to this one's: DXGI's SyncInterval, eglSwapInterval, QualitySettings.vSyncCount.
    uint32_t SwapInterval{1};
    //! The marker's target frame time: SwapInterval refreshes, rounded to a tick.
    uint32_t TargetFrameTicks{0};
    //! The marker's preferred frame time: PacerSettings::PreferredSwapInterval refreshes, rounded to a tick.
    uint32_t PreferredFrameTicks{0};
    //! The marker's CPU start time: FrameInput::NowTicks.
    int64_t CpuStartTicks{0};
    //! What the swap interval rule decided on the previous frame; this frame is the first at the new interval.
    SwapIntervalChange Change{SwapIntervalChange::None};
  };
}

#endif
