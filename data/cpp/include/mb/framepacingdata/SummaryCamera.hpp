#ifndef MB_FRAMEPACINGDATA_SUMMARYCAMERA_HPP
#define MB_FRAMEPACINGDATA_SUMMARYCAMERA_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacingdata/ValueStatistics.hpp>
#include <cstdint>

namespace MB::FramePacingData
{
  //! EXPERIMENTAL camera captures: the scanout delay between the zones and the tears the camera saw.
  struct SummaryCamera
  {
    ValueStatistics ScanoutDelay;
    int64_t FramesSeenInBothZones{0};
    int64_t TornFrames{0};
    int64_t SecondZoneOnlyFrames{0};
  };
}

#endif
