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
  //! corner, +x to the right, +y down. Always valid: the constructor makes a negative width or height 0 and cuts a size that would put
  //! Right() or Bottom() beyond int32_t, so every Rectangle has a size of at least 0 and edges that fit.
  class Rectangle
  {
    int32_t m_x{0};
    int32_t m_y{0};
    int32_t m_width{0};
    int32_t m_height{0};

    //! size, at least 0 and at most what keeps it and start + size within int32_t
    static constexpr int32_t ValidSize(const int32_t start, const int64_t size) noexcept
    {
      constexpr int64_t Max = std::numeric_limits<int32_t>::max();
      return static_cast<int32_t>(std::clamp(size, int64_t{0}, std::min(Max, Max - start)));
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

    //! The rectangle between the edges: an edge before the opposite one gives a size of 0.
    static constexpr Rectangle FromLeftTopRightBottom(const int32_t left, const int32_t top, const int32_t right, const int32_t bottom) noexcept
    {
      Rectangle rectangle;
      rectangle.m_x = left;
      rectangle.m_y = top;
      rectangle.m_width = ValidSize(left, int64_t{right} - left);
      rectangle.m_height = ValidSize(top, int64_t{bottom} - top);
      return rectangle;
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
