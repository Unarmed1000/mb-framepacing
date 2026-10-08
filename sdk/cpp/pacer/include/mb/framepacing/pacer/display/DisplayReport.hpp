#ifndef MB_FRAMEPACING_PACER_DISPLAY_DISPLAYREPORT_HPP
#define MB_FRAMEPACING_PACER_DISPLAY_DISPLAYREPORT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built). What the platform says of a frame that
  //! was presented earlier, where it reports display times (PacerCapability::DisplayTimes): given to the pacer a few frames
  //! after the frame it is about, oldest first. Statistics only: no frame is paced by it. A reported display time is what the
  //! platform says, and no measurement of the display.
  struct DisplayReport
  {
    //! The frame: its FrameSchedule::FrameId.
    uint64_t FrameId{0};
    //! When the frame was first shown (the start of its first refresh), on the application's steady clock. For a frame that was
    //! shown only.
    NanosecondTickCount DisplayTime;
    //! false: the platform says the frame was never shown.
    bool Shown{true};
  };
}

#endif
