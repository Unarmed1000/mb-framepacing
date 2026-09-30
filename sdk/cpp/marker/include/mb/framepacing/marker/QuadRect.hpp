#ifndef MB_FRAMEPACING_MARKER_QUADRECT_HPP
#define MB_FRAMEPACING_MARKER_QUADRECT_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Marker
{
  //! Axis aligned rectangle to fill, covering the pixels [X, X + Width) x [Y, Y + Height): [Left(),Right()) x [Top(),Bottom()).
  struct QuadRect
  {
    int32_t X{0};
    int32_t Y{0};
    int32_t Width{0};
    int32_t Height{0};
    //! true: draw black (luma 0), false: draw white (luma 255)
    bool Dark{false};

    [[nodiscard]] constexpr int32_t Left() const noexcept
    {
      return X;
    }

    [[nodiscard]] constexpr int32_t Top() const noexcept
    {
      return Y;
    }

    //! The first pixel column right of the rectangle
    [[nodiscard]] constexpr int32_t Right() const noexcept
    {
      return X + Width;
    }

    //! The first pixel row below the rectangle
    [[nodiscard]] constexpr int32_t Bottom() const noexcept
    {
      return Y + Height;
    }

    constexpr bool operator==(const QuadRect&) const noexcept = default;
  };
}

#endif
