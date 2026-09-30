#ifndef MB_FRAMEPACING_MARKER_MARKERQUAD_HPP
#define MB_FRAMEPACING_MARKER_MARKERQUAD_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/Rectangle.hpp>

namespace MB::FramePacing::Marker
{
  //! A rectangle of the marker to fill (ModulesToQuads): its pixels, and its colour.
  struct MarkerQuad
  {
    Rectangle Rect;
    //! true: draw black (luma 0), false: draw white (luma 255)
    bool Dark{false};

    constexpr bool operator==(const MarkerQuad&) const noexcept = default;
  };
}

#endif
