#ifndef MB_FRAMEPACING_PACER_FRAME_FRAMESCHEDULE_HPP
#define MB_FRAMEPACING_PACER_FRAME_FRAMESCHEDULE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <mb/framepacing/core/time/TimeSpan32.hpp>
#include <mb/framepacing/pacer/rule/SwapIntervalChange.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer.md). What the pacer plans for a frame (FramePacer::BeginFrame): the swap interval to
  //! hold it for, the time to render it for, and the marker's pacing fields.
  struct FrameSchedule
  {
    //! The frame's id: the pacer's own count of the frames it began, from 1. The application gives it back with the frame's present
    //! feedback (PresentFeedback), where its platform has any.
    uint64_t FrameId{0};
    //! Refreshes from the previous frame's display to this one's: DXGI's SyncInterval, eglSwapInterval, QualitySettings.vSyncCount.
    uint32_t SwapInterval{1};
    //! The animation time: the frame's predicted display time on the display's clock. Render the frame for it; it is the marker's
    //! animation time.
    TimeSpan AnimationTime;
    //! The step from the previous frame's animation time: the frame's delta time.
    TimeSpan AnimationStep;
    //! When the pacer aims for this frame to be shown, on the application's steady clock: the marker's intended display time. Without
    //! present feedback it is the frame's start plus its swap interval (NextFrameStartTime), as the frame starts when the previous
    //! one is shown. With present feedback it is the newest display time the platform reported plus the swap intervals of the frames
    //! since, this one included: the refresh the frame reaches when none of them is late, however many presents are queued. Unknown
    //! (TickCount64(), the marker's 0) while there is no display time to count from.
    TickCount64 IntendedDisplayTime;
    //! The frame's start plus its swap interval, on the application's steady clock. The application begins the next frame no
    //! earlier than this, at every swap interval: a present that waits for the display has passed it when it returns, and where the
    //! present does not wait this is what keeps the loop from running ahead of the display. A loop that paces by sleeping also
    //! presents no earlier than one refresh before it. Not a display time: with presents queued the frame is shown later.
    TickCount64 NextFrameStartTime;
    //! The marker's target frame time: SwapInterval refreshes, rounded to a tick.
    TimeSpan32 TargetFrameTime;
    //! The marker's preferred frame time: the preferred swap interval's refreshes (PacerSettings::PreferredSwapIntervalAt).
    TimeSpan32 PreferredFrameTime;
    //! What the swap interval rule decided from the previous frame; this frame is the first at the new interval.
    SwapIntervalChange Change{SwapIntervalChange::Unchanged};
  };
}

#endif
