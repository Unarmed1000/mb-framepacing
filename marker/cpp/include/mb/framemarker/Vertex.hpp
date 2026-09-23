#ifndef MB_FRAMEMARKER_VERTEX_HPP
#define MB_FRAMEMARKER_VERTEX_HPP
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FrameMarker
{
  //! A pixel aligned vertex: X and Y lie on pixel corners (top-left origin, +y down).
  struct Vertex
  {
    int32_t X{0};
    int32_t Y{0};
    //! 0 (dark) or 255 (light). Render it as the RGB color (Luma, Luma, Luma).
    uint8_t Luma{0};

    constexpr bool operator==(const Vertex&) const noexcept = default;
  };
}

#endif
