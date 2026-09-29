#ifndef MB_FRAMEPACINGDATA_SUMMARYCOUNTS_HPP
#define MB_FRAMEPACINGDATA_SUMMARYCOUNTS_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacingData
{
  //! What a run's captures held.
  struct SummaryCounts
  {
    int64_t Captures{0};
    int64_t Decoded{0};
    int64_t Undecodable{0};
    int64_t Torn{0};
    int64_t NotRecorded{0};
    //! Frames the capture source reported dropping.
    int64_t SourceDroppedFrames{0};
    //! Refreshes the capture's device clock says were missed.
    int64_t MissedCaptures{0};
    int64_t PresentedFrames{0};
    int64_t SkippedFrameIndices{0};
    //! Frames the target dropped: skipped frame indices over a capture without gaps that never came back out of order.
    int64_t DroppedFrames{0};
    int64_t OutOfOrderCaptures{0};
    int64_t Segments{0};

    bool operator==(const SummaryCounts&) const = default;
  };
}

#endif
