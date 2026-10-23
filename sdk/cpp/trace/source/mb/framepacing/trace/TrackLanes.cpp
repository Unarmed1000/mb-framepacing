// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause
//
// Which of the tracks of one name a slice goes on (sdk/doc/trace-frame-record.md, "The tracks"): the first whose slices have all
// ended by the slice's start, a new one while there are fewer than MaxLaneCount, else the one whose last slice ends first.
#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <mb/framepacing/trace/TrackLanes.hpp>
#include <cassert>
#include <cstdint>

namespace MB::FramePacing::Trace
{
  uint32_t TrackLanes::Assign(const NanosecondTickCount startTime, const NanosecondTickCount endTime) noexcept
  {
    assert(startTime <= endTime);
    uint32_t lane = 0;
    while (lane < m_laneCount && startTime < m_endTimes[lane])
    {
      ++lane;
    }
    if (lane < m_laneCount)
    {
      // Free: its last slice ended by this one's start
      m_endTimes[lane] = endTime;
      return lane;
    }
    if (m_laneCount < MaxLaneCount)
    {
      m_endTimes[lane] = endTime;
      ++m_laneCount;
      return lane;
    }
    // Every lane is taken: the one free first, and the slice overlaps the last one on it, which stays taken while either is open
    lane = 0;
    for (uint32_t candidate = 1; candidate < MaxLaneCount; ++candidate)
    {
      if (m_endTimes[candidate] < m_endTimes[lane])
      {
        lane = candidate;
      }
    }
    if (m_endTimes[lane] < endTime)
    {
      m_endTimes[lane] = endTime;
    }
    ++m_overflows;
    return lane;
  }

  void TrackLanes::Clear() noexcept
  {
    m_laneCount = 0;
    m_overflows = 0;
  }
}
