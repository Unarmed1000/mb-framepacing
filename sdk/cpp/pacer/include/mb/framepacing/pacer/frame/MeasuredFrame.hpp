#ifndef MB_FRAMEPACING_PACER_FRAME_MEASUREDFRAME_HPP
#define MB_FRAMEPACING_PACER_FRAME_MEASUREDFRAME_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/TimeSpan.hpp>
#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer.md). A frame that present feedback has decided (FramesInFlight::TakeMeasured): what
  //! the swap interval rule is given about it.
  struct MeasuredFrame
  {
    //! The frame: its FrameSchedule::FrameId.
    uint64_t FrameId{0};
    //! The animation time it was paced for: its place on the display's clock.
    TimeSpan AnimationTime;
    //! How long it worked, as EndFrame was given it.
    TimeSpan Work;
    //! It was shown more refreshes after the display time used before it than the swap intervals between the two.
    bool Late{false};
  };
}

#endif
