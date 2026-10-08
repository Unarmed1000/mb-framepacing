#ifndef MB_FRAMEPACING_PACER_PLACEMENT_PRESENTTIMING_HPP
#define MB_FRAMEPACING_PACER_PLACEMENT_PRESENTTIMING_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built). The time a present is given, with
  //! which the display's side shows a frame at the refresh it is for (DisplayPlacementUtil).
  enum class PresentTiming : uint8_t
  {
    //! The present takes no time: the frame loop presents at the right moment
    Untimed = 0,
    //! A time the frame before it stays on screen at least (PacerCapability::PresentAfterDuration)
    AfterDuration = 1,
    //! A time before which the frame is not shown (PacerCapability::PresentAtTime)
    AtTime = 2,
  };
}

#endif
