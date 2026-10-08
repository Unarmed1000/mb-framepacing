//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Measures every frame against what the application aimed for: late frames, the pacer's pacing and prediction errors, the late share and
//* which cause dominates the animation error.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;

namespace MB.FramePacing.Analysis
{
  internal static class PacingAnalyzer
  {
    // The on-time frames set where the pacer's clock lines up with the capture clock: the earliest group of at least this share of the frames.
    // Frames shown early (the one after a dropped frame, say) are rarer than that, and frames that stay late after a hitch come after it
    private const double OnTimeShare = 0.25;

    /// <summary>
    /// Measures the frames (replacing them in <paramref name="frames"/> with their targets, errors and <see cref="PresentedFrameFlags.Late"/>)
    /// and returns the run's pacing. Display time steps are whole refreshes, so everything is compared with half a refresh of slack.
    /// <list type="bullet">
    /// <item>
    /// With intended display times (<see cref="PacingSource.Schedule"/>) every frame gets a pacing error (display time step minus intended step) and a
    /// prediction error (animation step minus intended step); the animation error is prediction minus pacing error. A frame is late when it is
    /// shown at least half a refresh after its intended time, measured from the run's on-time frames: that also finds frames that stay late
    /// after a hitch.
    /// </item>
    /// <item>Otherwise a frame is late when it is shown at least one refresh later than its target frame time after the previous frame.</item>
    /// </list>
    /// </summary>
    public static RunPacing Analyze(
      List<PresentedFrame> frames,
      NanosecondTimeSpan refresh,
      bool refreshCalculated,
      double? targetFps,
      NanosecondTimeSpan errorThreshold
    )
    {
      // Half a refresh, cut to the nanosecond
      var half = new NanosecondTimeSpan(refresh.Nanoseconds / 2);
      bool schedule = frames.Count(f => f.IntendedDisplayTime != default) >= Math.Max(2, frames.Count / 2);
      var source =
        schedule ? PacingSource.Schedule
        : frames.Any(f => f.MarkerTargetFrameTime != NanosecondTimeDuration.Zero) ? PacingSource.TargetFrameTime
        : frames.Any(f => f.MarkerPreferredFrameTime != NanosecondTimeDuration.Zero) ? PacingSource.PreferredFrameTime
        : targetFps is > 0 ? PacingSource.GivenTarget
        : PacingSource.NativeRefresh;
      var givenTarget = targetFps is { } fps && fps > 0 ? FrameTimeRounding.WholeRefreshesAtRate(fps, refresh) : refresh;

      // How far after its intended time each frame appeared, relative to the run's on-time frames (the pacer and capture clocks differ)
      NanosecondTimeSpan? scheduleOffset = null;
      if (schedule)
      {
        var offsets = new NanosecondList(frames.Count);
        foreach (var frame in frames)
        {
          if (frame.IntendedDisplayTime != default)
            offsets.Add(CaptureMinusPacer(frame));
        }
        offsets.Sort();
        scheduleOffset = OnTimeOffset(offsets.Values, half);
      }

      // Frames the target dropped before each frame: without a schedule, the frame after them is due their frame times later too
      var dropped = DroppedFrames.Before(frames);
      var pacingErrors = new NanosecondList();
      var predictionErrors = new NanosecondList();
      long late = 0;
      long counted = 0;
      for (int i = 0; i < frames.Count; ++i)
      {
        var frame = frames[i];
        var previous = i > 0 && frames[i - 1].Segment == frame.Segment ? frames[i - 1] : null;
        NanosecondTimeSpan? intendedStep =
          schedule && previous != null && frame.IntendedDisplayTime != default && previous.IntendedDisplayTime != default
            ? frame.IntendedDisplayTime - previous.IntendedDisplayTime
            : null;
        // What the frame is measured against: the schedule's step, the pacer's target, else the rate the application wants (a game that
        // wants 30 fps on 60 Hz aims for two refreshes), else the rate given to the tools, else one refresh. An application that presents on
        // demand has no interval to aim for: no target, so only a schedule can make its frames late. Without a schedule, a frame after
        // frames the target dropped is due one frame time per frame later (1 + dropped): the drop explains the longer step, not lateness
        var markerTarget = frame.MarkerTargetFrameTime;
        var markerWants = frame.MarkerPreferredFrameTime;
        var onDemandTime = MB.FramePacing.MarkerDecoding.MarkerPayload.OnDemandFrameTime;
        bool onDemand = markerTarget == onDemandTime || (markerTarget == NanosecondTimeDuration.Zero && markerWants == onDemandTime);
        NanosecondTimeSpan? target =
          intendedStep is { } step ? WholeRefreshes(step, refresh)
          : onDemand ? null
          : new NanosecondTimeSpan(
            (1 + dropped[i])
              * (
                markerTarget != NanosecondTimeDuration.Zero ? WholeRefreshes(markerTarget, refresh)
                : markerWants != NanosecondTimeDuration.Zero ? WholeRefreshes(markerWants, refresh)
                : givenTarget
              ).Nanoseconds
          );
        // What the application wants: only its marker can say so (a lowered pacer and a 30 fps lock target the same); else the rate given
        // to the tools, else one refresh
        NanosecondTimeSpan? preferred =
          markerWants == onDemandTime ? null
          : markerWants != NanosecondTimeDuration.Zero ? WholeRefreshes(markerWants, refresh)
          : givenTarget;
        // A static step (nothing animated while the frame before was on screen) has no prediction error; its pacing error still counts
        bool animates = (frame.Flags & PresentedFrameFlags.StaticBefore) == 0;

        // A step a capture gap made uncertain is not judged: no pacing or prediction error, no late verdict
        bool uncertain = (frame.Flags & PresentedFrameFlags.UncertainStep) != 0;
        NanosecondTimeSpan? pacingError = null;
        NanosecondTimeSpan? predictionError = null;
        if (!uncertain && intendedStep is { } intended && frame.DisplayDelta is { } displayStep && frame.AnimationDelta is { } animationStep)
        {
          pacingError = displayStep - intended;
          pacingErrors.Add(pacingError.Value);
          if (animates)
          {
            predictionError = animationStep - intended;
            predictionErrors.Add(predictionError.Value);
          }
        }
        NanosecondTimeSpan? lateness =
          scheduleOffset is { } offset && frame.IntendedDisplayTime != default ? CaptureMinusPacer(frame) - offset : null;

        bool isLate = false;
        if (!uncertain && frame.DisplayDelta is { } display)
        {
          ++counted;
          isLate = lateness is { } behind ? behind >= half : target is { } aim && display >= aim + half;
          if (isLate)
            ++late;
        }
        frames[i] = frame with
        {
          Flags = isLate ? frame.Flags | PresentedFrameFlags.Late : frame.Flags,
          TargetFrameTime = target,
          PreferredFrameTime = preferred,
          PacingError = pacingError,
          PredictionError = predictionError,
          Lateness = lateness,
        };
      }

      var (uneven, even) = Split(frames, half, errorThreshold);
      var targets = new NanosecondList(frames.Count);
      foreach (var frame in frames)
      {
        if (frame.DisplayDelta.HasValue && frame.TargetFrameTime is { } frameTarget)
          targets.Add(frameTarget);
      }
      targets.Sort();
      pacingErrors.Sort();
      predictionErrors.Sort();
      return new RunPacing(
        refresh,
        refreshCalculated,
        // The median, as a target a frame had: the lower of the two middle ones for an even number of frames
        targets.Count > 0
          ? new NanosecondTimeSpan(targets.Values[(targets.Count - 1) / 2])
          : givenTarget,
        source,
        late,
        counted > 0 ? late / (double)counted : 0,
        LateShare.Worst(frames, LateShare.Window),
        uneven,
        even,
        Verdict(uneven, even)
      )
      {
        PacingErrorMs = pacingErrors.Count > 0 ? Statistics.FromSortedNanoseconds(pacingErrors.Values) : null,
        PredictionErrorMs = predictionErrors.Count > 0 ? Statistics.FromSortedNanoseconds(predictionErrors.Values) : null,
      };
    }

    /// <summary>
    /// A frame's first-seen time minus its intended display time. The first is on the capture's clock, the second on the pacer's, so this
    /// is the two clocks' offset plus how late the frame was: only the difference between two frames' values says something.
    /// </summary>
    private static NanosecondTimeSpan CaptureMinusPacer(PresentedFrame frame) => frame.FirstSeenTime - frame.IntendedDisplayTime;

    /// <summary>
    /// The earliest offset (capture time minus intended time, sorted) that at least <see cref="OnTimeShare"/> of the frames share within half a
    /// refresh: the run's on-time frames.
    /// </summary>
    private static NanosecondTimeSpan OnTimeOffset(ReadOnlySpan<long> sortedNanoseconds, NanosecondTimeSpan half)
    {
      int needed = Math.Max(1, (int)Math.Ceiling(sortedNanoseconds.Length * OnTimeShare));
      int end = 0;
      for (int start = 0; start < sortedNanoseconds.Length; ++start)
      {
        end = Math.Max(end, start);
        while (end < sortedNanoseconds.Length && sortedNanoseconds[end] - sortedNanoseconds[start] < half.Nanoseconds)
          ++end;
        if (end - start >= needed)
          return new NanosecondTimeSpan(sortedNanoseconds[start]);
      }
      return new NanosecondTimeSpan(sortedNanoseconds[0]);
    }

    private static NanosecondTimeSpan WholeRefreshes(NanosecondTimeSpan frameTime, NanosecondTimeSpan refresh) =>
      FrameTimeRounding.WholeRefreshes(frameTime, refresh);

    /// <summary>
    /// Which cause each frame with an error counts for: bad pacing (uneven) or delta time jitter (even).
    /// <list type="bullet">
    /// <item>With the pacer's schedule the animation error splits exactly: the frame counts for the larger of its pacing and prediction error.</item>
    /// <item>
    /// Otherwise (no schedule, or a frame without a split) it is bad pacing when this or the previous frame was shown off its target (or
    /// after skipped frames, or torn), else delta time jitter. A display changes frames on refreshes, so a display time step counts as off its
    /// target from half a refresh on, whatever captured it.
    /// </item>
    /// </list>
    /// </summary>
    private static (long Uneven, long Even) Split(List<PresentedFrame> frames, NanosecondTimeSpan half, NanosecondTimeSpan errorThreshold)
    {
      bool OffTarget(PresentedFrame f) =>
        f.Flags.HasFlag(PresentedFrameFlags.Torn)
        || (f.DisplayDelta is { } display && f.TargetFrameTime is { } target && (display - target).Duration() >= half);
      long uneven = 0;
      long even = 0;
      for (int i = 0; i < frames.Count; ++i)
      {
        var frame = frames[i];
        if (frame.AnimationError is not { } error || error.Duration() <= errorThreshold)
          continue;
        bool pacing =
          frame.PacingError is { } p && frame.PredictionError is { } q
            ? p.Duration() >= q.Duration()
            : OffTarget(frame) || frame.SkippedBefore > 0 || (i > 0 && frames[i - 1].Segment == frame.Segment && OffTarget(frames[i - 1]));
        if (pacing)
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
