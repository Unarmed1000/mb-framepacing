//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Which of the tracks of one name a slice goes on, so that slices that overlap are never on one track. A trace's slices on one track must
//* follow each other or nest: a trace pairs a slice's end with the slice opened last, so two slices that overlap without nesting end each
//* other's, silently. With frames in flight (a pipelined engine starts a frame's CPU work before the frame before it is presented), the
//* frame records overlap. The lane is the track's id among the tracks of that name (FrameRecord.Lane); a trace viewer shows tracks of one
//* name as one track.
//*
//* A slice gets the first lane whose slices have all ended by its start, so a loop whose frames never overlap uses lane 0 only, one track.
//* With every lane taken it gets the lane whose last slice ends first, and Overflows counts it: that slice overlaps one on its lane. Values
//* in, values out: no clock read, and nothing allocated after it is made. Keep one for each name of track that is written.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Trace
{
  public sealed class TrackLanes
  {
    /// <summary>The most lanes a track name gets.</summary>
    public const int MaxLaneCount = 16;

    // Where each lane's last slice ends; the first LaneCount are taken
    private readonly NanosecondTickCount[] m_endTimes = new NanosecondTickCount[MaxLaneCount];

    /// <summary>How many lanes have been taken: the most slices that overlapped at once, up to <see cref="MaxLaneCount"/>.</summary>
    public int LaneCount { get; private set; }

    /// <summary>How many slices got a lane that was still taken, since every lane was: each overlaps a slice on its lane.</summary>
    public int Overflows { get; private set; }

    /// <summary>
    /// The lane of the slice from <paramref name="startTime"/> to <paramref name="endTime"/>, and the slice is that lane's last. Throws
    /// ArgumentOutOfRangeException for an end before the start.
    /// </summary>
    public int Assign(NanosecondTickCount startTime, NanosecondTickCount endTime)
    {
      if (endTime < startTime)
        throw new ArgumentOutOfRangeException(nameof(endTime), endTime, "A slice ends at or after its start");
      int lane = 0;
      while (lane < LaneCount && startTime < m_endTimes[lane])
        ++lane;
      if (lane < LaneCount)
      {
        // Free: its last slice ended by this one's start
        m_endTimes[lane] = endTime;
        return lane;
      }
      if (LaneCount < MaxLaneCount)
      {
        m_endTimes[lane] = endTime;
        ++LaneCount;
        return lane;
      }
      // Every lane is taken: the one free first, and the slice overlaps the last one on it, which stays taken while either is open
      lane = 0;
      for (int candidate = 1; candidate < MaxLaneCount; ++candidate)
      {
        if (m_endTimes[candidate] < m_endTimes[lane])
          lane = candidate;
      }
      if (m_endTimes[lane] < endTime)
        m_endTimes[lane] = endTime;
      ++Overflows;
      return lane;
    }

    /// <summary>Forget every slice: the next one gets lane 0.</summary>
    public void Clear()
    {
      LaneCount = 0;
      Overflows = 0;
    }
  }
}
