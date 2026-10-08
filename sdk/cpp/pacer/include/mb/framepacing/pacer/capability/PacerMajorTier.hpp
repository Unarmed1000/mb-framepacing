#ifndef MB_FRAMEPACING_PACER_CAPABILITY_PACERMAJORTIER_HPP
#define MB_FRAMEPACING_PACER_CAPABILITY_PACERMAJORTIER_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Pacer
{
  //! EXPERIMENTAL (the pacer module, sdk/doc/pacer-design.md: the redesign, being built). The major tiers: who places a frame
  //! on its refresh, 1 the best. Each has four sub tiers (PacerTier), and a tier is written as the two: "3.1".
  enum class PacerMajorTier : uint8_t
  {
    //! The display's side, from a time on the present before which the frame is not shown (PresentAtTime), and of two frames
    //! that are both due it shows the later and never the earlier (PresentSkipsOverdue). After a frame that came late the frame
    //! on screen is the one made for that refresh, and frames can not pile up. Rated only: no pacer is built for it.
    DisplayPlacesAndSkips = 1,
    //! The display's side, from a time on the present before which the frame is not shown (PresentAtTime), every frame in the
    //! order it was presented.
    DisplayPlaces = 2,
    //! The frame loop: it has to make the present at the right moment.
    LoopPlaces = 3,
  };
}

#endif
