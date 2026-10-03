#ifndef MB_FRAMEPACING_PACER_FRAME_PRESENTFEEDBACKSTATE_HPP
#define MB_FRAMEPACING_PACER_FRAME_PRESENTFEEDBACKSTATE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer.md). What became of the present feedback a pacer was given, counted since it was
  //! made: for overlays and logs, and for an application to see that its platform's feedback is of no use (nearly all of it refused: a
  //! wrong refresh period, a display with a variable refresh rate) and switch it off.
  struct PresentFeedbackState
  {
    //! Display times the frames were measured by.
    uint64_t Used{0};
    //! Feedback that was not used: for a frame the pacer does not keep (more than FramesInFlight::Capacity frames old, from before a
    //! restart, never begun), not newer than the feedback before it, a display time before the frame's present, or one that is not a
    //! whole number of refreshes after the display time used before it.
    uint64_t Refused{0};
    //! Frames the platform reported as never shown.
    uint64_t NotShown{0};
    //! Frames that were counted as on time without any feedback for them.
    uint64_t Missing{0};
  };
}

#endif
