#ifndef MB_FRAMEPACING_PACER_DISPLAY_DISPLAYERRORSTATE_HPP
#define MB_FRAMEPACING_PACER_DISPLAY_DISPLAYERRORSTATE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built). What the display times an application
  //! reported say of the frames (DisplayErrorCounter): the animation error where the application runs, for overlays and logs.
  //! Counted since the pacer was made, and again for about the last second. The pacer paces by none of it, and the times are the
  //! platform's word: no measurement of the display.
  //!
  //! A frame is judged when it and the frame before it were both reported as shown. Its animation error is its animation time
  //! step less the time between the two display times.
  struct DisplayErrorState
  {
    //! Reports that were taken: for a frame the counter keeps, newer than the report before.
    uint64_t Reports{0};
    //! Reports that were not: for a frame it does not keep (never begun, more than DisplayErrorCounter::Capacity frames old,
    //! from before a restart) or not newer than the report before.
    uint64_t Refused{0};
    //! Frames reported as never shown.
    uint64_t NotShown{0};
    //! Frames that were judged.
    uint64_t JudgedFrames{0};
    //! Judged frames with an animation error of more than DisplayErrorCounter::ErrorThreshold, either way.
    uint64_t ErrorFrames{0};
    //! Judged frames shown half a refresh or more off where their animation time step put them: at another refresh.
    uint64_t OffTargetFrames{0};
    //! Of those, the frames shown later: the frame before them was on screen a refresh or more longer than it was made for.
    uint64_t LateFrames{0};

    //! The same four of the frames shown in about the last second up to the newest display time (the newest
    //! DisplayErrorCounter::RecentBuckets eighths of a second).
    uint32_t RecentJudgedFrames{0};
    uint32_t RecentErrorFrames{0};
    uint32_t RecentOffTargetFrames{0};
    uint32_t RecentLateFrames{0};

    constexpr bool operator==(const DisplayErrorState&) const noexcept = default;
  };
}

#endif
