#ifndef MB_FRAMEPACINGDATA_MARKERLOCATION_HPP
#define MB_FRAMEPACINGDATA_MARKERLOCATION_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacingdata/DataRect.hpp>

namespace MB::FramePacingData
{
  //! Where a marker is: its bounds (including the quiet zone) and its module size, in stored pixels.
  struct MarkerLocation
  {
    DataRect Bounds{};
    double ModuleSizePx{0.0};

    constexpr bool operator==(const MarkerLocation&) const noexcept = default;
  };
}

#endif
