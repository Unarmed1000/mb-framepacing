//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Marks late frames and works out a run's pacing: the target frame time, the late share and which cause dominates the animation error.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;

namespace MB.FramePacing.Analysis
{
  internal static class PacingAnalyzer
  {
    // A target frame rate just below a whole number of refreshes (59.9 fps on 60 Hz) still means one refresh
    private const double TargetRoundingSlack = 0.05;

    /// <summary>
    /// Sets <see cref="PresentedFrameFlags.Late"/> on the frames (replacing them in <paramref name="frames"/>) and returns the run's pacing.
    /// Display times are whole refreshes, so everything is compared with half a refresh of slack.
    /// </summary>
    public static RunPacing Analyze(
      List<PresentedFrame> frames,
      long refreshTicks,
      bool refreshCalculated,
      double? targetFps,
      long errorThresholdTicks
    )
    {
      long targetRefreshes =
        targetFps is { } fps && fps > 0
          ? Math.Max(1, (long)Math.Ceiling((TimeSpan.TicksPerSecond / fps / refreshTicks) - TargetRoundingSlack))
          : Math.Max(1, (long)Math.Round(MedianDisplayTicks(frames) / refreshTicks));
      long target = targetRefreshes * refreshTicks;
      long half = refreshTicks / 2;

      long late = 0;
      long counted = 0;
      for (int i = 0; i < frames.Count; ++i)
      {
        if (frames[i].DisplayDeltaTicks is not { } display)
          continue;
        ++counted;
        if (display >= target + half)
        {
          frames[i] = frames[i] with { Flags = frames[i].Flags | PresentedFrameFlags.Late };
          ++late;
        }
      }

      // A capture card sees whole refreshes; a camera times a frame to about one camera period, so it tells smaller deviations (and tears,
      // presented mid-refresh with vsync off) from an even display
      long slack = Math.Min(half, 2 * errorThresholdTicks);
      bool Uneven(int i) =>
        frames[i].Flags.HasFlag(PresentedFrameFlags.Torn) || (frames[i].DisplayDeltaTicks is { } display && Math.Abs(display - target) >= slack);
      long uneven = 0;
      long even = 0;
      for (int i = 0; i < frames.Count; ++i)
      {
        if (frames[i].AnimationErrorTicks is not { } error || Math.Abs(error) <= errorThresholdTicks)
          continue;
        bool previousUneven = i > 0 && frames[i - 1].Segment == frames[i].Segment && Uneven(i - 1);
        if (Uneven(i) || previousUneven || frames[i].SkippedBefore > 0)
          ++uneven;
        else
          ++even;
      }

      return new RunPacing(
        refreshTicks / (double)TimeSpan.TicksPerMillisecond,
        refreshCalculated,
        target / (double)TimeSpan.TicksPerMillisecond,
        targetFps is > 0,
        late,
        counted > 0 ? late / (double)counted : 0,
        LateShare.Worst(frames, LateShare.WindowTicks),
        uneven,
        even,
        Verdict(uneven, even)
      );
    }

    private static PacingVerdict Verdict(long uneven, long even)
    {
      long total = uneven + even;
      if (total == 0)
        return PacingVerdict.None;
      if (uneven * 3 >= total * 2)
        return PacingVerdict.BadPacing;
      if (even * 3 >= total * 2)
        return PacingVerdict.DeltaTimeJitter;
      return PacingVerdict.Both;
    }

    private static double MedianDisplayTicks(List<PresentedFrame> frames)
    {
      var sorted = frames.Where(f => f.DisplayDeltaTicks.HasValue).Select(f => (double)f.DisplayDeltaTicks!.Value).ToArray();
      if (sorted.Length == 0)
        return 0;
      Array.Sort(sorted);
      return Statistics.Percentile(sorted, 0.5);
    }
  }
}
