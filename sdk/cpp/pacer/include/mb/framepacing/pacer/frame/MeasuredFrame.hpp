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
    //! It was late: the display fell behind the frames' swap intervals with it (a refresh was lost; a frame held longer after one
    //! shown as much sooner is not), the platform reported it as never shown, or its work took longer than its swap interval's time.
    bool Late{false};
  };
}

#endif
