//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The share of late frames over a sliding time window: tells rare spikes from busy stretches.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;

namespace MB.FramePacing.Analysis
{
  public static class LateShare
  {
    public const double WindowSeconds = 2;

    public static readonly long WindowTicks = (long)(WindowSeconds * TimeSpan.TicksPerSecond);

    /// <summary>
    /// Per presented frame: the share of late frames (0..1) among the frames with a display time step first seen in the window that ends at this
    /// frame. Frames without a display time step (the first of a segment) count in neither.
    /// </summary>
    public static double[] Rolling(IReadOnlyList<PresentedFrame> frames, long windowTicks)
    {
      var shares = new double[frames.Count];
      int start = 0;
      int counted = 0;
      int late = 0;
      for (int i = 0; i < frames.Count; ++i)
      {
        Add(frames[i], 1, ref counted, ref late);
        while (frames[i].FirstSeenTicks - frames[start].FirstSeenTicks >= windowTicks)
          Add(frames[start++], -1, ref counted, ref late);
        shares[i] = counted > 0 ? late / (double)counted : 0;
      }
      return shares;
    }

    /// <summary>The highest <see cref="Rolling"/> share over the windows that lie completely inside the run; the whole run's share if it is shorter.</summary>
    public static double Worst(IReadOnlyList<PresentedFrame> frames, long windowTicks)
    {
      if (frames.Count == 0)
        return 0;
      var shares = Rolling(frames, windowTicks);
      double worst = -1;
      for (int i = 0; i < frames.Count; ++i)
      {
        if (frames[i].FirstSeenTicks - frames[0].FirstSeenTicks >= windowTicks)
          worst = Math.Max(worst, shares[i]);
      }
      return worst >= 0 ? worst : Rolling(frames, long.MaxValue)[^1];
    }

    private static void Add(PresentedFrame frame, int sign, ref int counted, ref int late)
    {
      if (!frame.DisplayDeltaTicks.HasValue)
        return;
      counted += sign;
      if ((frame.Flags & PresentedFrameFlags.Late) != 0)
        late += sign;
    }
  }
}
