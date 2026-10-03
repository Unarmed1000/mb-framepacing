#ifndef MB_FRAMEPACING_PACER_FRAME_PRESENTFEEDBACK_HPP
#define MB_FRAMEPACING_PACER_FRAME_PRESENTFEEDBACK_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TickCount64.hpp>
#include <mb/framepacing/pacer/frame/PresentResult.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer.md). What the platform measured for a frame that was presented earlier, where it has
  //! present feedback (VK_EXT_present_timing, Wayland's presentation-time, Android's frame timestamps): given to
  //! FramePacer::AddPresentFeedback, a few frames after the frame it is about. Optional: a pacer that gets none measures the frames by
  //! their starts.
  struct PresentFeedback
  {
    //! The frame: its FrameSchedule::FrameId.
    uint64_t FrameId{0};
    PresentResult Result{PresentResult::Shown};
    //! When the frame was first shown (the start of its first refresh), on the steady clock BeginFrame gets. For Shown only.
    TickCount64 DisplayTime;
    //! PresentTime was given.
    bool HasPresentTime{false};
    //! When the frame was presented (the present call, or the time the platform took it over), on the same clock. A display time
    //! before it is refused. Without it the time EndFrame was given counts, so an application that waits between EndFrame and its
    //! present must give it.
    TickCount64 PresentTime;

    //! The frame was shown at displayTime.
    [[nodiscard]] static PresentFeedback Shown(uint64_t frameId, TickCount64 displayTime) noexcept;

    //! The frame was presented at presentTime and shown at displayTime.
    [[nodiscard]] static PresentFeedback Shown(uint64_t frameId, TickCount64 displayTime, TickCount64 presentTime) noexcept;

    //! The platform says the frame was never shown.
    [[nodiscard]] static PresentFeedback NotShown(uint64_t frameId) noexcept;
  };
}

#endif
