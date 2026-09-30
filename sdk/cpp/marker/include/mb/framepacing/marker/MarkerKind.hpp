#ifndef MB_FRAMEPACING_MARKER_MARKERKIND_HPP
#define MB_FRAMEPACING_MARKER_MARKERKIND_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <cstdint>

namespace MB::FramePacing::Marker
{
  //! What a marker means. Frame markers are drawn every frame of a test run; the sequence markers bracket the run so the analyzer can cut
  //! the capture to exactly the measured window. See doc/marker-format.md "Test sequences". A sync marker is the small second marker for
  //! tearing checks and camera timing: it only carries the frame index.
  enum class MarkerKind : uint8_t
  {
    Frame = 0,
    SequenceStart = 1,
    SequenceEnd = 2,
    Sync = 3,
  };
}

#endif
