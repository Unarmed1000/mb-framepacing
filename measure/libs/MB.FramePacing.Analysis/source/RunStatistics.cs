//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Statistics of one run: display delta, animation delta, animation error (signed and absolute), drift and time on screen, the
//* animation error summarised the way Gamers Nexus do (error per frame and percent error), and the frame rate the way benchmarks report
//* it: average fps and the 1 % / 0.1 % lows. The frame rate numbers describe the frames that animate: a static frame's time on screen is
//* left out (PresentedFrameFlags.StaticBefore) and counted.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
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
    // Presented frames whose |animation error| exceeds the error threshold (TimelineResult.ErrorThreshold)
    long FramesWithAnimationError,
    // Gamers Nexus's "error per frame": the mean |animation error| of the frames with one
    double ErrorPerFrameMs,
    // Gamers Nexus's "percent error": the sum of |animation error| as a percentage of the time those frames measure (their display time steps)
    double PercentError,
    // Frames with a display time step over the time those steps cover, static frames left out
    double AverageFps,
    // The frame rate at the 99th / 99.9th percentile display time step (nearest rank, so it is a step that happened); null with fewer
    // than MinFramesForOnePercentLow / MinFramesForPointOnePercentLow frames
    double? OnePercentLowFps,
    double? PointOnePercentLowFps,
    // The application side, from the markers (Count 0 when no frame carries them): CPU busy, the frametime (CPU start to the next
    // frame's) and CPU wait (frametime - CPU busy), named as PresentMon's MsCPUBusy, MsBetweenAppStart and MsCPUWait
    Statistics CpuBusyMs,
    Statistics FrameTimeMs,
    Statistics CpuWaitMs,
    // The display time steps the frame rate numbers leave out: each is a static frame's time on screen
    long ExcludedStaticFrames,
    // The display time steps a capture gap made uncertain (PresentedFrameFlags.UncertainStep), not judged; not counted again when static
    long UncertainSteps
  )
  {
    /// <summary>
    /// The statistics of <paramref name="frames"/> (a run, or a section of one): the frames with an animation error give the animation time
    /// steps and the errors, the frames that count toward the frame rate (<see cref="CountsTowardFrameRate"/>) the display time steps and the
    /// frame rates; every frame the drift and time on screen. <paramref name="threshold"/> is the error threshold,
    /// <paramref name="capturePeriod"/> the capture period (no frame counts as off without one).
    /// </summary>
    public static RunStatistics From(IReadOnlyList<PresentedFrame> frames, NanosecondTimeSpan threshold, NanosecondTimeSpan capturePeriod)
    {
      // One pass over the frames gathers each kind of value as nanoseconds (a run of an hour has a million frames), then each is sorted
      // once and gives its statistics; the display time steps' sorted values give the lows too
      var displaySteps = new NanosecondList(frames.Count);
      var animationSteps = new NanosecondList(frames.Count);
      var errors = new NanosecondList(frames.Count);
      var absoluteErrors = new NanosecondList(frames.Count);
      var drifts = new NanosecondList(frames.Count);
      var onScreen = new NanosecondList(frames.Count);
      var cpuBusy = new NanosecondList();
      var frameTimes = new NanosecondList();
      var cpuWaits = new NanosecondList();
      long visibleErrors = 0;
      long absoluteErrorNanoseconds = 0;
      long errorDisplayNanoseconds = 0;
      long frameRateNanoseconds = 0;
      long excludedStatic = 0;
      long uncertain = 0;
      for (int i = 0; i < frames.Count; ++i)
      {
        var frame = frames[i];
        if (frame.AnimationError is { } error)
        {
          var absolute = error.Duration();
          animationSteps.Add(frame.AnimationDelta!.Value);
          errors.Add(error);
          absoluteErrors.Add(absolute);
          if (capturePeriod > NanosecondTimeSpan.Zero && absolute > threshold)
            ++visibleErrors;
          absoluteErrorNanoseconds += Math.Abs(error.Nanoseconds);
          errorDisplayNanoseconds += frame.DisplayDelta!.Value.Nanoseconds;
        }
        if (CountsTowardFrameRate(frame))
        {
          displaySteps.Add(frame.DisplayDelta!.Value);
          frameRateNanoseconds += frame.DisplayDelta!.Value.Nanoseconds;
        }
        drifts.Add(frame.Drift);
        onScreen.Add(frame.OnScreen);
        if (frame.CpuBusy != NanosecondTimeDuration.Zero)
          cpuBusy.Add(frame.CpuBusy);
        if (frame.FrameTime is { } frameTime)
          frameTimes.Add(frameTime);
        if (frame.CpuWait is { } cpuWait)
          cpuWaits.Add(cpuWait);
        if (frame.DisplayDelta.HasValue)
        {
          if ((frame.Flags & PresentedFrameFlags.StaticBefore) != 0)
            ++excludedStatic;
          else if ((frame.Flags & PresentedFrameFlags.UncertainStep) != 0)
            ++uncertain;
        }
      }
      var (errorPerFrameMs, percentError) = ErrorSummary(absoluteErrorNanoseconds, errorDisplayNanoseconds, errors.Count);
      displaySteps.Sort();
      return new RunStatistics(
        Statistics.FromSortedNanoseconds(displaySteps.Values),
        Sorted(animationSteps),
        Sorted(errors),
        Sorted(absoluteErrors),
        Sorted(drifts),
        Sorted(onScreen),
        visibleErrors,
        errorPerFrameMs,
        percentError,
        AverageFramesPerSecond(displaySteps.Count, frameRateNanoseconds),
        LowFps(displaySteps.Values, 0.99, MinFramesForOnePercentLow),
        LowFps(displaySteps.Values, 0.999, MinFramesForPointOnePercentLow),
        Sorted(cpuBusy),
        Sorted(frameTimes),
        Sorted(cpuWaits),
        excludedStatic,
        uncertain
      );

      static Statistics Sorted(NanosecondList nanoseconds)
      {
        nanoseconds.Sort();
        return Statistics.FromSortedNanoseconds(nanoseconds.Values);
      }
    }

    /// <summary>
    /// The frame's display time step counts toward the frame rate numbers: it has one, it is not a static frame's time on screen, and no
    /// capture gap made it uncertain.
    /// </summary>
    public static bool CountsTowardFrameRate(PresentedFrame frame) =>
      frame.DisplayDelta.HasValue && (frame.Flags & (PresentedFrameFlags.StaticBefore | PresentedFrameFlags.UncertainStep)) == 0;

    /// <summary>A 1 % low needs at least this many frames to rest on more than the single slowest one.</summary>
    public const int MinFramesForOnePercentLow = 100;

    /// <summary>A 0.1 % low needs at least this many frames.</summary>
    public const int MinFramesForPointOnePercentLow = 1000;

    /// <summary>
    /// The frame rate of the step that <paramref name="fraction"/> of the display time steps (ascending nanoseconds) are at most as long as.
    /// </summary>
    private static double? LowFps(ReadOnlySpan<long> sortedSteps, double fraction, int minFrames)
    {
      if (sortedSteps.Length < minFrames)
        return null;
      long step = sortedSteps[Math.Max(0, (int)Math.Ceiling(fraction * sortedSteps.Length) - 1)];
      return step > 0 ? NanosecondTimeSpan.NanosecondsPerSecond / (double)step : null;
    }

    /// <summary><paramref name="count"/> frames over the time their display time steps cover; 0 when that is no time.</summary>
    private static double AverageFramesPerSecond(int count, long nanoseconds) =>
      // The frames times a second's nanoseconds is exact in a double (a 32-bit number times 5^9 * 2^9), so the one division rounds
      nanoseconds > 0
        ? count * (double)NanosecondTimeSpan.NanosecondsPerSecond / nanoseconds
        : 0;

    /// <summary><see cref="ErrorPerFrameMs"/> and <see cref="PercentError"/> of frames' animation errors and display time steps.</summary>
    public static (double ErrorPerFrameMs, double PercentError) ErrorSummary(
      IReadOnlyCollection<(NanosecondTimeSpan Error, NanosecondTimeSpan DisplayStep)> frames
    )
    {
      if (frames.Count == 0)
        return (0, 0);
      return ErrorSummary(frames.Sum(f => Math.Abs(f.Error.Nanoseconds)), frames.Sum(f => f.DisplayStep.Nanoseconds), frames.Count);
    }

    /// <summary>
    /// The same from the sums: the errors' absolute nanoseconds and the display time steps' nanoseconds of <paramref name="count"/> frames.
    /// </summary>
    private static (double ErrorPerFrameMs, double PercentError) ErrorSummary(long absoluteNanoseconds, long displayNanoseconds, int count)
    {
      if (count == 0)
        return (0, 0);
      // The mean error is two divisions (by the frames, then by the nanoseconds in a millisecond), the percentage a product, then a
      // division
      return (
        absoluteNanoseconds / (double)count / NanosecondTimeSpan.NanosecondsPerMillisecond,
        displayNanoseconds > 0 ? absoluteNanoseconds * 100.0 / displayNanoseconds : 0
      );
    }
  }
}
