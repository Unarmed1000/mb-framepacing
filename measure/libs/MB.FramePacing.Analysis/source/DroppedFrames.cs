//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Frames the target dropped: rendered, but never on screen. A frame index skipped before a presented frame counts when the capture had no gap
//* there (a gap may have hidden it: the frame is not first seen with UncertainStart) and the frame never came back out of order later in the
//* segment (PresentedFrame.OlderFrames). The pacing analysis measures a frame after them against their frame times too, and the charts draw
//* them.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;
using System.Linq;

namespace MB.FramePacing.Analysis
{
  public static class DroppedFrames
  {
    /// <summary>Per frame, how many frames the target dropped just before it (0 after a capture gap: unknown).</summary>
    public static long[] Before(IReadOnlyList<PresentedFrame> frames)
    {
      var dropped = new long[frames.Count];
      int segmentStart = 0;
      for (int i = 0; i <= frames.Count; ++i)
      {
        if (i < frames.Count && frames[i].Segment == frames[segmentStart].Segment)
          continue;
        // The segment's frames segmentStart to i: the older frames it showed out of order, sorted, count out of the skipped indices
        var older = new List<ulong>();
        for (int k = segmentStart; k < i; ++k)
        {
          if (frames[k].OlderFrames is { } shown)
            older.AddRange(shown.Select(o => o.FrameIndex));
        }
        var shownOlder = older.Distinct().Order().ToArray();
        for (int k = segmentStart + 1; k < i; ++k)
        {
          var frame = frames[k];
          if (frame.SkippedBefore == 0 || (frame.Flags & PresentedFrameFlags.UncertainStart) != 0)
            continue;
          ulong from = frames[k - 1].FrameIndex + 1;
          int lo = LowerBound(shownOlder, from);
          int hi = LowerBound(shownOlder, frame.FrameIndex);
          dropped[k] = (long)frame.SkippedBefore - (hi - lo);
        }
        segmentStart = i;
      }
      return dropped;
    }

    private static int LowerBound(ulong[] sorted, ulong value)
    {
      int lo = 0;
      int hi = sorted.Length;
      while (lo < hi)
      {
        int mid = (lo + hi) / 2;
        if (sorted[mid] < value)
          lo = mid + 1;
        else
          hi = mid;
      }
      return lo;
    }
  }
}
