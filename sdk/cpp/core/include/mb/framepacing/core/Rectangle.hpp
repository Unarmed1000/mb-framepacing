#ifndef MB_FRAMEPACING_CORE_RECTANGLE_HPP
#define MB_FRAMEPACING_CORE_RECTANGLE_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <algorithm>
#include <cassert>
#include <cstdint>
#include <limits>

namespace MB::FramePacing
{
  //! An integer pixel rectangle covering [X, X + Width) x [Y, Y + Height): [Left(),Right()) x [Top(),Bottom()). Origin at the top-left
  //! corner, +x to the right, +y down. Always valid: the constructor makes a negative width or height 0. Its edges must fit int32_t:
  //! asserted, not checked.
  class Rectangle
  {
    int32_t m_x{0};
    int32_t m_y{0};
    int32_t m_width{0};
    int32_t m_height{0};

    static constexpr bool FitsInt32(const int64_t value) noexcept
    {
      return value >= std::numeric_limits<int32_t>::min() && value <= std::numeric_limits<int32_t>::max();
    }

  public:
    //! The empty rectangle at (0, 0).
    constexpr Rectangle() noexcept = default;

    constexpr Rectangle(const int32_t x, const int32_t y, const int32_t width, const int32_t height) noexcept
      : m_x(x)
      , m_y(y)
      , m_width(std::max(width, 0))
      , m_height(std::max(height, 0))
    {
      assert(FitsInt32(int64_t{m_x} + m_width) && FitsInt32(int64_t{m_y} + m_height));
    }

    //! The rectangle between the edges: an edge before the opposite one gives a size of 0.
    static constexpr Rectangle FromLeftTopRightBottom(const int32_t left, const int32_t top, const int32_t right, const int32_t bottom) noexcept
    {
      assert(FitsInt32(int64_t{right} - left) && FitsInt32(int64_t{bottom} - top));
      return {left, top, right - left, bottom - top};
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
