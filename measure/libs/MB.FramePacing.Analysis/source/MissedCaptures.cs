//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Refreshes a capture missed without saying so: on the capture device's clock, consecutive captures (capture index + 1) are a capture period
//* apart, so a step of 1.5 periods or more means round(step / period) - 1 refreshes never arrived. Only on the device clock (device
//* timestamps, or exact times given for images): the host clock's arrival times bunch and spread, so a long step there proves nothing.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;

namespace MB.FramePacing.Analysis
{
  public static class MissedCaptures
  {
    /// <summary>
    /// <paramref name="rows"/> with <see cref="CaptureRow.MissedBefore"/> set from their times, which come from the device clock. A step
    /// across captures the recorder dropped (NotRecorded rows) is not counted: those are known already.
    /// </summary>
    public static List<CaptureRow> Mark(List<CaptureRow> rows)
    {
      var period = TimelineAnalyzer.EstimateCapturePeriod(rows);
      if (period <= NanosecondTimeSpan.Zero)
        return rows;
      for (int i = 1; i < rows.Count; ++i)
      {
        var previous = rows[i - 1];
        var row = rows[i];
        if (previous.Status == CaptureStatus.NotRecorded || row.Status == CaptureStatus.NotRecorded)
          continue;
        long missed = Before(row.CaptureTime - previous.CaptureTime, period);
        if (missed > 0)
          rows[i] = row with { MissedBefore = missed };
      }
      return rows;
    }

    /// <summary>The refreshes missed in a <paramref name="step"/> between two consecutive captures: 0 below 1.5 periods.</summary>
    public static long Before(NanosecondTimeSpan step, NanosecondTimeSpan period) =>
      period > NanosecondTimeSpan.Zero && 2 * step.Nanoseconds >= 3 * period.Nanoseconds
        ? (long)Math.Round(step.Nanoseconds / (double)period.Nanoseconds, MidpointRounding.AwayFromZero) - 1
        : 0;
  }
}
