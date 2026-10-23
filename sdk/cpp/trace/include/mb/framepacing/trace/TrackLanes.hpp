#ifndef MB_FRAMEPACING_TRACE_TRACKLANES_HPP
#define MB_FRAMEPACING_TRACE_TRACKLANES_HPP
// SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
// SPDX-License-Identifier: BSD-3-Clause

#include <mb/framepacing/core/time/NanosecondTickCount.hpp>
#include <array>
#include <cstdint>

namespace MB::FramePacing::Trace
{
  //! Which of the tracks of one name a slice goes on, so that slices that overlap are never on one track. A trace's slices on one
  //! track must follow each other or nest: a trace pairs a slice's end with the slice opened last, so two slices that overlap
  //! without nesting end each other's, silently. With frames in flight (a pipelined engine starts a frame's CPU work before the
  //! frame before it is presented), the frame records overlap. The lane is the track's id among the tracks of that name
  //! (FrameRecord::Lane); a trace viewer shows tracks of one name as one track.
  //!
  //! A slice gets the first lane whose slices have all ended by its start, so a loop whose frames never overlap uses lane 0 only,
  //! one track. With every lane taken it gets the lane whose last slice ends first, and Overflows() counts it: that slice overlaps
  //! one on its lane. Values in, values out: no clock read, no allocation. Keep one for each name of track that is written.
  class TrackLanes
  {
  public:
    //! The most lanes a track name gets.
    static constexpr uint32_t MaxLaneCount = 16;

    //! The lane of the slice from startTime to endTime (at or after startTime), and the slice is that lane's last.
    [[nodiscard]] uint32_t Assign(NanosecondTickCount startTime, NanosecondTickCount endTime) noexcept;

    //! How many lanes have been taken: the most slices that overlapped at once, up to MaxLaneCount.
    [[nodiscard]] constexpr uint32_t LaneCount() const noexcept
    {
      return m_laneCount;
    }

    //! How many slices got a lane that was still taken, since every lane was: each overlaps a slice on its lane.
    [[nodiscard]] constexpr uint32_t Overflows() const noexcept
    {
      return m_overflows;
    }

    //! Forget every slice: the next one gets lane 0.
    void Clear() noexcept;

  private:
    //! Where each lane's last slice ends; the first m_laneCount are taken.
    std::array<NanosecondTickCount, MaxLaneCount> m_endTimes{};
    uint32_t m_laneCount{0};
    uint32_t m_overflows{0};
  };
}

#endif
