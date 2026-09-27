//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Measures every frame against what the application aimed for: late frames, the pacer's pacing and prediction errors, the late share and
//* which cause dominates the animation error.
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
    // A target just below a whole number of refreshes (59.9 fps on 60 Hz) still means one refresh
    private const double TargetRoundingSlack = 0.05;

    // The on-time frames set where the pacer's clock lines up with the capture clock: the earliest group of at least this share of the frames.
    // Frames shown early (the one after a dropped frame, say) are rarer than that, and frames that stay late after a hitch come after it
    private const double OnTimeShare = 0.25;

    /// <summary>
    /// Measures the frames (replacing them in <paramref name="frames"/> with their targets, errors and <see cref="PresentedFrameFlags.Late"/>)
    /// and returns the run's pacing. Display times are whole refreshes, so everything is compared with half a refresh of slack.
    /// <list type="bullet">
    /// <item>
    /// With intended display times (<see cref="PacingSource.Schedule"/>) every frame gets a pacing error (display step minus intended step) and a
    /// prediction error (animation step minus intended step); the animation error is prediction minus pacing error. A frame is late when it is
    /// shown at least half a refresh after its intended time, measured from the run's on-time frames: that also finds frames that stay late
    /// after a hitch.
    /// </item>
    /// <item>Otherwise a frame is late when it is shown at least one refresh later than its target frame time after the previous frame.</item>
    /// </list>
    /// </summary>
    public static RunPacing Analyze(
      List<PresentedFrame> frames,
      long refreshTicks,
      bool refreshCalculated,
      double? targetFps,
      long errorThresholdTicks
    )
    {
      long half = refreshTicks / 2;
      bool schedule = frames.Count(f => f.IntendedDisplayTicks != 0) >= Math.Max(2, frames.Count / 2);
      var source =
        schedule ? PacingSource.Schedule
        : frames.Any(f => f.MarkerTargetFrameTicks != 0) ? PacingSource.TargetFrameTime
        : targetFps is > 0 ? PacingSource.GivenTarget
        : PacingSource.NativeRefresh;
      long givenTarget = targetFps is { } fps && fps > 0 ? WholeRefreshes(TimeSpan.TicksPerSecond / fps, refreshTicks) : refreshTicks;

      // How far after its intended time each frame appeared, relative to the run's on-time frames (the pacer and capture clocks differ)
      long? scheduleOffset = null;
      if (schedule)
      {
        var offsets = frames.Where(f => f.IntendedDisplayTicks != 0).Select(f => f.FirstSeenTicks - f.IntendedDisplayTicks).Order().ToArray();
        scheduleOffset = OnTimeOffset(offsets, half);
      }

      var pacingErrors = new List<long>();
      var predictionErrors = new List<long>();
      long late = 0;
      long counted = 0;
      for (int i = 0; i < frames.Count; ++i)
      {
        var frame = frames[i];
        var previous = i > 0 && frames[i - 1].Segment == frame.Segment ? frames[i - 1] : null;
        long? intendedStep =
          schedule && previous != null && frame.IntendedDisplayTicks != 0 && previous.IntendedDisplayTicks != 0
            ? frame.IntendedDisplayTicks - previous.IntendedDisplayTicks
            : null;
        long target =
          intendedStep is { } step ? WholeRefreshes(step, refreshTicks)
          : frame.MarkerTargetFrameTicks != 0 ? WholeRefreshes(frame.MarkerTargetFrameTicks, refreshTicks)
          : givenTarget;

        long? pacingError = null;
        long? predictionError = null;
        if (intendedStep is { } intended && frame.DisplayDeltaTicks is { } displayStep && frame.AnimationDeltaTicks is { } animationStep)
        {
          pacingError = displayStep - intended;
          predictionError = animationStep - intended;
          pacingErrors.Add(pacingError.Value);
          predictionErrors.Add(predictionError.Value);
        }
        long? lateness =
          scheduleOffset is { } offset && frame.IntendedDisplayTicks != 0 ? frame.FirstSeenTicks - frame.IntendedDisplayTicks - offset : null;

        bool isLate = false;
        if (frame.DisplayDeltaTicks is { } display)
        {
          ++counted;
          isLate = lateness is { } behind ? behind >= half : display >= target + half;
          if (isLate)
            ++late;
        }
        frames[i] = frame with
        {
          Flags = isLate ? frame.Flags | PresentedFrameFlags.Late : frame.Flags,
          TargetTicks = target,
          PacingErrorTicks = pacingError,
          PredictionErrorTicks = predictionError,
          LatenessTicks = lateness,
        };
      }

      var (uneven, even) = schedule ? SplitBySchedule(frames, errorThresholdTicks) : SplitByDisplay(frames, half, errorThresholdTicks);
      var targets = frames.Where(f => f.DisplayDeltaTicks.HasValue).Select(f => (double)f.TargetTicks!.Value).Order().ToArray();
      return new RunPacing(
        refreshTicks / (double)TimeSpan.TicksPerMillisecond,
        refreshCalculated,
        (targets.Length > 0 ? Statistics.Percentile(targets, 0.5) : givenTarget) / TimeSpan.TicksPerMillisecond,
        source,
        late,
        counted > 0 ? late / (double)counted : 0,
        LateShare.Worst(frames, LateShare.WindowTicks),
        uneven,
        even,
        Verdict(uneven, even)
      )
      {
        PacingErrorMs = pacingErrors.Count > 0 ? Statistics.FromTicks(pacingErrors) : null,
        PredictionErrorMs = predictionErrors.Count > 0 ? Statistics.FromTicks(predictionErrors) : null,
      };
    }

    /// <summary>
    /// The earliest offset (capture time minus intended time, sorted) that at least <see cref="OnTimeShare"/> of the frames share within half a
    /// refresh: the run's on-time frames.
    /// </summary>
    private static long OnTimeOffset(long[] sorted, long half)
    {
      int needed = Math.Max(1, (int)Math.Ceiling(sorted.Length * OnTimeShare));
      int end = 0;
      for (int start = 0; start < sorted.Length; ++start)
      {
        end = Math.Max(end, start);
        while (end < sorted.Length && sorted[end] - sorted[start] < half)
          ++end;
        if (end - start >= needed)
          return sorted[start];
      }
      return sorted[0];
    }

    /// <summary>A frame time in whole refreshes, rounded up (a 60 fps target on 144 Hz alternates 2 and 3 refreshes: 3 is the target).</summary>
    private static long WholeRefreshes(double ticks, long refreshTicks) =>
      Math.Max(1, (long)Math.Ceiling((ticks / refreshTicks) - TargetRoundingSlack)) * refreshTicks;

    /// <summary>With a schedule the animation error of every frame splits exactly: a frame counts for the larger of its two parts.</summary>
    private static (long Uneven, long Even) SplitBySchedule(List<PresentedFrame> frames, long errorThresholdTicks)
    {
      long pacing = 0;
      long prediction = 0;
      foreach (var frame in frames)
      {
        if (frame.AnimationErrorTicks is not { } error || Math.Abs(error) <= errorThresholdTicks)
          continue;
        if (frame.PacingErrorTicks is { } p && frame.PredictionErrorTicks is { } q && Math.Abs(q) > Math.Abs(p))
          ++prediction;
        else
          ++pacing;
      }
      return (pacing, prediction);
    }

    /// <summary>
    /// Without a schedule: frames with an error where this or the previous frame was shown off its target (or after skipped frames) are bad
    /// pacing, the rest delta time jitter. A capture card sees whole refreshes; a camera times a frame to about one camera period, so it tells
    /// smaller deviations (and tears, presented mid-refresh with vsync off) from an even display.
    /// </summary>
    private static (long Uneven, long Even) SplitByDisplay(List<PresentedFrame> frames, long half, long errorThresholdTicks)
    {
      long slack = Math.Min(half, 2 * errorThresholdTicks);
      bool Uneven(PresentedFrame f) =>
        f.Flags.HasFlag(PresentedFrameFlags.Torn) || (f.DisplayDeltaTicks is { } display && Math.Abs(display - f.TargetTicks!.Value) >= slack);
      long uneven = 0;
      long even = 0;
      for (int i = 0; i < frames.Count; ++i)
      {
        if (frames[i].AnimationErrorTicks is not { } error || Math.Abs(error) <= errorThresholdTicks)
          continue;
        bool previousUneven = i > 0 && frames[i - 1].Segment == frames[i].Segment && Uneven(frames[i - 1]);
        if (Uneven(frames[i]) || previousUneven || frames[i].SkippedBefore > 0)
          ++uneven;
        else
          ++even;
      }
      return (uneven, even);
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
  }
}
