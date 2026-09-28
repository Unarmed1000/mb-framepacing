//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Statistics of one run: display delta, animation delta, animation error (signed and absolute), drift and time on screen, and the
//* animation error summarised the way Gamers Nexus do: error per frame and percent error.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;

namespace MB.FramePacing.Analysis
{
  public sealed record RunStatistics(
    Statistics DisplayDeltaMs,
    Statistics AnimationDeltaMs,
    Statistics AnimationErrorMs,
    Statistics AbsoluteAnimationErrorMs,
    Statistics DriftMs,
    Statistics OnScreenMs,
    // Presented frames whose |animation error| exceeds the error threshold (TimelineResult.ErrorThresholdTicks)
    long FramesWithAnimationError,
    // Gamers Nexus's "error per frame": the mean |animation error| of the frames with one
    double ErrorPerFrameMs,
    // Gamers Nexus's "percent error": the sum of |animation error| as a percentage of the time those frames measure (their display times)
    double PercentError
  )
  {
    /// <summary><see cref="ErrorPerFrameMs"/> and <see cref="PercentError"/> of frames' animation errors and display times, in ticks.</summary>
    public static (double ErrorPerFrameMs, double PercentError) ErrorSummary(IReadOnlyCollection<(long ErrorTicks, long DisplayTicks)> frames)
    {
      if (frames.Count == 0)
        return (0, 0);
      long absolute = frames.Sum(f => Math.Abs(f.ErrorTicks));
      long display = frames.Sum(f => f.DisplayTicks);
      return (absolute / (double)frames.Count / TimeSpan.TicksPerMillisecond, display > 0 ? absolute * 100.0 / display : 0);
    }
  }
}
