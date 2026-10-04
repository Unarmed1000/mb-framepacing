#ifndef MB_FRAMEPACING_PACER_FRAME_PRESENTFEEDBACKSTATE_HPP
#define MB_FRAMEPACING_PACER_FRAME_PRESENTFEEDBACKSTATE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer.md). The statistics of the present feedback a pacer was given, counted since it was
  //! made: what the display did, for overlays and logs and to hold against the pacer's own count of late frames, and what became of
  //! the feedback, so an application sees that its platform's is of no use (nearly all of it refused: a wrong refresh period, a
  //! display with a variable refresh rate). The pacer paces by none of it.
  struct PresentFeedbackState
  {
    //! Display times that were counted from.
    uint64_t Used{0};
    //! Feedback that was not used: for a frame the pacer does not keep (more than FramesInFlight::Capacity frames old, from before a
    //! restart, never begun), not newer than the feedback before it, a display time before the frame's present, or one that is not a
    //! whole number of refreshes after the display time used before it.
    uint64_t Refused{0};
    //! Frames the platform reported as never shown.
    uint64_t NotShown{0};
    //! Frames no feedback came for: feedback for a newer frame came first, or the frame left as FramesInFlight::Capacity frames old.
    uint64_t Missing{0};
    //! Refreshes the display fell behind the frames' swap intervals, by its display times: what late frames cost on the display. A
    //! frame held a refresh longer right after one shown a refresh sooner lost none.
    uint64_t LateRefreshes{0};
  };
}

#endif
