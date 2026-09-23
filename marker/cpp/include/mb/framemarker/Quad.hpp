#ifndef MB_FRAMEMARKER_QUAD_HPP
#define MB_FRAMEMARKER_QUAD_HPP
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FrameMarker
{
  //! Axis aligned rectangle covering the pixels [Left,Right) x [Top,Bottom).
  struct Quad
  {
    int32_t Left{0};
    int32_t Top{0};
    int32_t Right{0};
    int32_t Bottom{0};
    //! true: draw black (luma 0), false: draw white (luma 255)
    bool Dark{false};

    constexpr bool operator==(const Quad&) const noexcept = default;
  };
}

#endif
