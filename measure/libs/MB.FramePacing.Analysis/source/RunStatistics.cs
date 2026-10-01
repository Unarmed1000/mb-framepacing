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
    public static RunStatistics From(IReadOnlyList<PresentedFrame> frames, TimeSpan threshold, TimeSpan capturePeriod)
    {
      var withMetrics = frames.Where(f => f.AnimationError.HasValue).ToList();
      var frameRate = frames.Where(CountsTowardFrameRate).ToList();
      var (errorPerFrameMs, percentError) = ErrorSummary(withMetrics.Select(f => (f.AnimationError!.Value, f.DisplayDelta!.Value)).ToList());
      return new RunStatistics(
        Statistics.From(frameRate.Select(f => f.DisplayDelta!.Value)),
        Statistics.From(withMetrics.Select(f => f.AnimationDelta!.Value)),
        Statistics.From(withMetrics.Select(f => f.AnimationError!.Value)),
        Statistics.From(withMetrics.Select(f => f.AnimationError!.Value.Duration())),
        Statistics.From(frames.Select(f => f.Drift)),
        Statistics.From(frames.Select(f => f.OnScreen)),
        withMetrics.LongCount(f => capturePeriod > TimeSpan.Zero && f.AnimationError!.Value.Duration() > threshold),
        errorPerFrameMs,
        percentError,
        AverageFpsOf(frameRate),
        LowFps(frameRate, 0.99, MinFramesForOnePercentLow),
        LowFps(frameRate, 0.999, MinFramesForPointOnePercentLow),
        Statistics.From(frames.Where(f => f.CpuBusy != TimeSpan32.Zero).Select(f => f.CpuBusy.ToTimeSpan())),
        Statistics.From(frames.Where(f => f.FrameTime.HasValue).Select(f => f.FrameTime!.Value)),
        Statistics.From(frames.Where(f => f.CpuWait.HasValue).Select(f => f.CpuWait!.Value)),
        frames.LongCount(f => f.DisplayDelta.HasValue && (f.Flags & PresentedFrameFlags.StaticBefore) != 0),
        frames.LongCount(f =>
          f.DisplayDelta.HasValue
          && (f.Flags & (PresentedFrameFlags.UncertainStep | PresentedFrameFlags.StaticBefore)) == PresentedFrameFlags.UncertainStep
        )
      );
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

    private static double AverageFpsOf(IReadOnlyCollection<PresentedFrame> frames)
    {
      long ticks = frames.Sum(f => f.DisplayDelta!.Value.Ticks);
      return ticks > 0 ? frames.Count * (double)TimeSpan.TicksPerSecond / ticks : 0;
    }

    private static double? LowFps(IReadOnlyCollection<PresentedFrame> frames, double fraction, int minFrames)
    {
      if (frames.Count < minFrames)
        return null;
      var steps = frames.Select(f => f.DisplayDelta!.Value).OrderBy(t => t).ToArray();
      var step = steps[Math.Max(0, (int)Math.Ceiling(fraction * steps.Length) - 1)];
      return step > TimeSpan.Zero ? TimeSpan.TicksPerSecond / (double)step.Ticks : null;
    }

    /// <summary><see cref="ErrorPerFrameMs"/> and <see cref="PercentError"/> of frames' animation errors and display time steps.</summary>
    public static (double ErrorPerFrameMs, double PercentError) ErrorSummary(IReadOnlyCollection<(TimeSpan Error, TimeSpan DisplayStep)> frames)
    {
      if (frames.Count == 0)
        return (0, 0);
      long absolute = frames.Sum(f => Math.Abs(f.Error.Ticks));
      long display = frames.Sum(f => f.DisplayStep.Ticks);
      return (absolute / (double)frames.Count / TimeSpan.TicksPerMillisecond, display > 0 ? absolute * 100.0 / display : 0);
    }
  }
}
