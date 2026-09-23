#ifndef MB_FRAMEMARKER_MARKERSLOT_HPP
#define MB_FRAMEMARKER_MARKERSLOT_HPP
// SPDX-License-Identifier: BSD-3-Clause

namespace MB::FrameMarker
{
  //! Where RecommendedOrigin places a marker. Drawing the same marker in all three slots detects tearing.
  enum class MarkerSlot
  {
    TopLeft,
    MiddleLeft,
    BottomLeft,
  };
}

#endif
