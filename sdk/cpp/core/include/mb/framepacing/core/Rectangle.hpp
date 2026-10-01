#ifndef MB_FRAMEPACING_CORE_RECTANGLE_HPP
#define MB_FRAMEPACING_CORE_RECTANGLE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <algorithm>
#include <cstdint>
#include <limits>

namespace MB::FramePacing
{
  //! An integer pixel rectangle covering [X, X + Width) x [Y, Y + Height): [Left(),Right()) x [Top(),Bottom()). Origin at the top-left
  //! corner, +x to the right, +y down. Always valid: the constructor makes a negative width or height 0, and cuts a size that would
  //! put the right or bottom edge past the last int32_t coordinate, so every edge fits int32_t.
  class Rectangle
  {
    int32_t m_x{0};
    int32_t m_y{0};
    int32_t m_width{0};
    int32_t m_height{0};

    //! The size kept within 0 and what the coordinates right of (or below) origin leave room for.
    static constexpr int32_t ValidSize(const int32_t origin, const int64_t size) noexcept
    {
      constexpr int64_t Last = std::numeric_limits<int32_t>::max();
      return static_cast<int32_t>(std::clamp<int64_t>(size, 0, std::min(Last, Last - origin)));
    }

  public:
    //! The empty rectangle at (0, 0).
    constexpr Rectangle() noexcept = default;

    constexpr Rectangle(const int32_t x, const int32_t y, const int32_t width, const int32_t height) noexcept
      : m_x(x)
      , m_y(y)
      , m_width(ValidSize(x, width))
      , m_height(ValidSize(y, height))
    {
    }

    //! The rectangle between the edges: an edge before the opposite one gives a size of 0, edges further apart than int32_t holds the
    //! largest size.
    static constexpr Rectangle FromLeftTopRightBottom(const int32_t left, const int32_t top, const int32_t right, const int32_t bottom) noexcept
    {
      return {left, top, ValidSize(left, int64_t{right} - left), ValidSize(top, int64_t{bottom} - top)};
    }

    [[nodiscard]] constexpr int32_t X() const noexcept
    {
      return m_x;
    }

    [[nodiscard]] constexpr int32_t Y() const noexcept
    {
      return m_y;
    }

    [[nodiscard]] constexpr int32_t Width() const noexcept
    {
      return m_width;
    }

    [[nodiscard]] constexpr int32_t Height() const noexcept
    {
      return m_height;
    }

    [[nodiscard]] constexpr int32_t Left() const noexcept
    {
      return m_x;
    }

    [[nodiscard]] constexpr int32_t Top() const noexcept
    {
      return m_y;
    }

    //! The first pixel column right of the rectangle
    [[nodiscard]] constexpr int32_t Right() const noexcept
    {
      return m_x + m_width;
    }

    //! The first pixel row below the rectangle
    [[nodiscard]] constexpr int32_t Bottom() const noexcept
    {
      return m_y + m_height;
    }

    //! Whether the pixel (x, y) is inside
    [[nodiscard]] constexpr bool Contains(const int32_t x, const int32_t y) const noexcept
    {
      return x >= Left() && x < Right() && y >= Top() && y < Bottom();
    }

    constexpr bool operator==(const Rectangle&) const noexcept = default;
  };
}

#endif
