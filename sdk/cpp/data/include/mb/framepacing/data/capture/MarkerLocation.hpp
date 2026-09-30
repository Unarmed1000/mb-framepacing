#ifndef MB_FRAMEPACING_DATA_CAPTURE_MARKERLOCATION_HPP
#define MB_FRAMEPACING_DATA_CAPTURE_MARKERLOCATION_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/Rectangle.hpp>

namespace MB::FramePacing::Data
{
  //! Where a marker is: its bounds (including the quiet zone) and its module size, in stored pixels.
  struct MarkerLocation
  {
    Rectangle Bounds;
    double ModuleSizePx{0.0};

    constexpr bool operator==(const MarkerLocation&) const noexcept = default;
  };
}

#endif
