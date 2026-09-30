#ifndef MB_FRAMEPACING_MARKER_PAYLOAD_STARTMETADATA_HPP
#define MB_FRAMEPACING_MARKER_PAYLOAD_STARTMETADATA_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/marker/payload/SequenceId.hpp>
#include <cstdint>

namespace MB::FramePacing::Marker
{
  //! Extra data carried by a SequenceStart marker.
  struct StartMetadata
  {
    //! Wall clock start time as C# DateTime UTC ticks (100ns since 0001-01-01), 0 = unknown. See ToDateTimeTicks (core/time/ChronoConversion.hpp).
    int64_t UtcTicks{0};
    //! Identifies the capture sequence: 16 opaque bytes, any content as long as it is unique to it.
    SequenceId Id{};

    constexpr bool operator==(const StartMetadata&) const noexcept = default;
  };
}

#endif
