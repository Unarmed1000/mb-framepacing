#ifndef MB_FRAMEMARKER_STARTMETADATA_HPP
#define MB_FRAMEMARKER_STARTMETADATA_HPP
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>
#include <string_view>

namespace MB::FrameMarker
{
  //! Extra data carried by a SequenceStart marker.
  struct StartMetadata
  {
    //! Wall clock start time as C# DateTime UTC ticks (100ns since 0001-01-01), 0 = unknown. See ToDateTimeTicks.
    int64_t UtcTicks{0};
    //! Test name, UTF-8, at most MaxStartNameBytes bytes. Not copied: must stay valid while encoding.
    std::string_view Name;
  };
}

#endif
