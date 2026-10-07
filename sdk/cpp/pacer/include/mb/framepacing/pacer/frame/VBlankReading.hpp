#ifndef MB_FRAMEPACING_PACER_FRAME_VBLANKREADING_HPP
#define MB_FRAMEPACING_PACER_FRAME_VBLANKREADING_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/core/time/NanosecondTimeDuration.hpp>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: part of a redesign that is not built yet). Where the display's
  //! refreshes are, as the window system told the application (PacerCapability::VBlankTimes): the time of one vertical blank, the
  //! period between them, and when that was read. All on the application's steady clock.
  struct VBlankReading
  {
    //! The time of a vertical blank: a recent one, or the next
    NanosecondTickCount VBlankTime;
    //! The time from one vertical blank to the next, as the window system gives it; zero: it gave none, and the pacer keeps the
    //! period it has
    NanosecondTimeDuration Period;
    //! When the application read the two: a reading ages, as the clock and the display drift apart
    NanosecondTickCount ReadTime;
  };
}

#endif
