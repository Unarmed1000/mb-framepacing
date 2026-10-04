#ifndef MB_FRAMEPACING_PACER_CLOCK_FRAMEMEASUREMENT_HPP
#define MB_FRAMEPACING_PACER_CLOCK_FRAMEMEASUREMENT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer.md). What a frame's start time says about the frame before it
  //! (PacerRefreshClock::Measure): in a loop paced by vsync a frame starts when the previous one is shown.
  struct FrameMeasurement
  {
    //! The clock started again with this frame (the first frame, a gap longer than its longest, a new refresh period): nothing was
    //! measured, and the previous frame counts as shown when it was meant to be.
    bool Restarted{true};
    //! The refreshes from the display of the frame before it to the previous frame's display, as measured: the time between the two
    //! frame starts rounded to whole refreshes, at least the previous frame's swap interval. 0 when restarted.
    uint32_t Refreshes{0};
    //! The previous frame was late: shown more refreshes after the frame before it than its swap interval, or its work took longer
    //! than its swap interval's time (it can not have made it then, whatever the frame starts say).
    bool Late{false};
    //! The display's clock: when the previous frame was shown, in the refreshes counted since the clock was made (exact: the
    //! refresh period's fraction is carried). Only differences mean anything.
    TimeSpan DisplayTime;
  };
}

#endif
