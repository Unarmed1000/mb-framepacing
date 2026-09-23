#ifndef MB_FRAMEMARKER_MARKERKIND_HPP
#define MB_FRAMEMARKER_MARKERKIND_HPP
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FrameMarker
{
  //! What a marker means. Frame markers are drawn every frame of a test run; the sequence markers bracket the run so the analyzer can cut
  //! the capture to exactly the measured window. See doc/marker-format.md "Test sequences".
  enum class MarkerKind : uint8_t
  {
    Frame = 0,
    SequenceStart = 1,
    SequenceEnd = 2,
  };

  inline constexpr uint8_t MaxMarkerKindValue = static_cast<uint8_t>(MarkerKind::SequenceEnd);
}

#endif
