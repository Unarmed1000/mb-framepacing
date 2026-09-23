#ifndef MB_FRAMEMARKER_PAYLOAD_HPP
#define MB_FRAMEMARKER_PAYLOAD_HPP
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framemarker/MarkerKind.hpp>
#include <cstdint>

namespace MB::FrameMarker
{
  //! The data every marker carries.
  struct Payload
  {
    //! The application's own rendered-frame counter. Unrelated to the capture card's frame counter.
    uint64_t FrameIndex{0};
    //! Animation time in C# TimeSpan ticks (100ns).
    int64_t AnimationTicks{0};
    //! Identifies one test run. The start marker, every frame marker and the end marker of a run carry the same id.
    uint32_t RunId{0};
    MarkerKind Kind{MarkerKind::Frame};

    constexpr bool operator==(const Payload&) const noexcept = default;
  };
}

#endif
