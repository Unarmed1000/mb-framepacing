#ifndef MB_FRAMEMARKER_POINT_HPP
#define MB_FRAMEMARKER_POINT_HPP
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FrameMarker
{
  //! A pixel position: origin at the top-left corner, +x to the right, +y down.
  struct Point
  {
    int32_t X{0};
    int32_t Y{0};

    constexpr bool operator==(const Point&) const noexcept = default;
  };
}

#endif
