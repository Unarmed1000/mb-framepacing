//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Every frame's present timing (PresentTiming), from the markers' CPU start time and CPU busy: the present is the one plus the other, on the
//* application's clock. The absolute time from present to screen can not be measured (the clocks are tied only by the frames); what is worked
//* out is on one clock (the planned latency, the steps that say why a frame was late) or a difference in which the clocks' offset cancels
//* out (the extra delay over the run's quickest frame).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using MB.FramePacing.Analysis;

namespace MB.FramePacing.Charts
{
  public static class PresentTimings
  {
    /// <summary>The present timing of each of <paramref name="frames"/> of a run on a display of <paramref name="refresh"/>.</summary>
    public static PresentTiming[] Of(IReadOnlyList<PresentedFrame> frames, NanosecondTimeDuration refresh)
    {
      var timings = new PresentTiming[frames.Count];
      // The present on the application's clock, and the first sighting minus it on the two clocks: their lowest is the run's quickest frame
      var presents = new long?[frames.Count];
      long lowest = long.MaxValue;
      for (int i = 0; i < frames.Count; ++i)
      {
        var f = frames[i];
        if (f.CpuStartTime == default || f.CpuBusy == NanosecondTimeDuration.Zero)
          continue;
        presents[i] = f.CpuStartTime.Nanoseconds + f.CpuBusy.Nanoseconds;
        lowest = Math.Min(lowest, f.FirstSeenTime.Nanoseconds - presents[i]!.Value);
      }
      var dropped = DroppedFrames.Before(frames);
      long half = refresh.Nanoseconds / 2;
      for (int i = 0; i < frames.Count; ++i)
      {
        var f = frames[i];
        if (presents[i] is not { } present)
        {
          timings[i] = PresentTiming.None;
          continue;
        }
        NanosecondTimeSpan? planned = f.IntendedDisplayTime != default ? new NanosecondTimeSpan(f.IntendedDisplayTime.Nanoseconds - present) : null;
        var extra = new NanosecondTimeSpan(f.FirstSeenTime.Nanoseconds - present - lowest);
        var period = IntendedPeriods.Of(f, dropped[i], refresh);
        bool? workOver = period is { } p ? f.CpuBusy >= p : null;
        LateReason? reason = null;
        // A late frame's present step against its intended period, by the late rule's half refresh: its present came late too, or on time
        if (
          (f.Flags & PresentedFrameFlags.Late) != 0
          && period is { } intended
          && i > 0
          && frames[i - 1].Segment == f.Segment
          && presents[i - 1] is { } before
        )
        {
          long step = present - before;
          reason =
            step < intended.Nanoseconds + half ? LateReason.ShownLateAfterPresent
            : f.CpuBusy >= intended ? LateReason.SubmittedLateWork
            : LateReason.SubmittedLateStart;
        }
        timings[i] = new PresentTiming(planned, extra, reason, workOver);
      }
      return timings;
    }
  }
}
