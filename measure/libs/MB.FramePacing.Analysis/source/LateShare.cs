//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The share of late frames over a sliding time window.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;

namespace MB.FramePacing.Analysis
{
  public static class LateShare
  {
    public const double WindowSeconds = 2;

    public static readonly NanosecondTimeSpan Window = NanosecondTimeSpan.FromSeconds(WindowSeconds);

    /// <summary>
    /// Per presented frame: the share of late frames (0..1) among the frames with a display time step first seen in the window that ends at this
    /// frame. Frames without a display time step (the first of a segment) count in neither.
    /// </summary>
    public static double[] Rolling(IReadOnlyList<PresentedFrame> frames, NanosecondTimeSpan window)
    {
      var shares = new double[frames.Count];
      int start = 0;
      int counted = 0;
      int late = 0;
      for (int i = 0; i < frames.Count; ++i)
      {
        Add(frames[i], 1, ref counted, ref late);
        while (frames[i].FirstSeenTime - frames[start].FirstSeenTime >= window)
          Add(frames[start++], -1, ref counted, ref late);
        shares[i] = counted > 0 ? late / (double)counted : 0;
      }
      return shares;
    }

    /// <summary>The highest <see cref="Rolling"/> share over the windows that lie completely inside the run; the whole run's share if it is shorter.</summary>
    public static double Worst(IReadOnlyList<PresentedFrame> frames, NanosecondTimeSpan window)
    {
      if (frames.Count == 0)
        return 0;
      var shares = Rolling(frames, window);
      double worst = -1;
      for (int i = 0; i < frames.Count; ++i)
      {
        if (frames[i].FirstSeenTime - frames[0].FirstSeenTime >= window)
          worst = Math.Max(worst, shares[i]);
      }
      return worst >= 0 ? worst : Rolling(frames, NanosecondTimeSpan.MaxValue)[^1];
    }

    /// <summary>
    /// The frame counts in a late share: it has a display time step, and no capture gap made it uncertain (such a step gets no late verdict).
    /// </summary>
    public static bool Counts(PresentedFrame frame) => frame.DisplayDelta.HasValue && (frame.Flags & PresentedFrameFlags.UncertainStep) == 0;

    private static void Add(PresentedFrame frame, int sign, ref int counted, ref int late)
    {
      if (!Counts(frame))
        return;
      counted += sign;
      if ((frame.Flags & PresentedFrameFlags.Late) != 0)
        late += sign;
    }
  }
}
