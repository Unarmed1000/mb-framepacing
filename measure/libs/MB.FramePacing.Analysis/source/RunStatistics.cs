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
    // Presented frames whose |animation error| exceeds the error threshold (TimelineResult.ErrorThresholdTicks)
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
    long ExcludedStaticFrames
  )
  {
    /// <summary>
    /// The statistics of <paramref name="frames"/> (a run, or a section of one): the frames with an animation error give the animation time
    /// steps and the errors, the frames that count toward the frame rate (<see cref="CountsTowardFrameRate"/>) the display time steps and the
    /// frame rates; every frame the drift and time on screen. <paramref name="thresholdTicks"/> is the error threshold,
    /// <paramref name="capturePeriodTicks"/> the capture period (no frame counts as off without one).
    /// </summary>
    public static RunStatistics From(IReadOnlyList<PresentedFrame> frames, long thresholdTicks, long capturePeriodTicks)
    {
      var withMetrics = frames.Where(f => f.AnimationErrorTicks.HasValue).ToList();
      var frameRate = frames.Where(CountsTowardFrameRate).ToList();
      var (errorPerFrameMs, percentError) = ErrorSummary(
        withMetrics.Select(f => (f.AnimationErrorTicks!.Value, f.DisplayDeltaTicks!.Value)).ToList()
      );
      return new RunStatistics(
        Statistics.FromTicks(frameRate.Select(f => f.DisplayDeltaTicks!.Value)),
        Statistics.FromTicks(withMetrics.Select(f => f.AnimationDeltaTicks!.Value)),
        Statistics.FromTicks(withMetrics.Select(f => f.AnimationErrorTicks!.Value)),
        Statistics.FromTicks(withMetrics.Select(f => Math.Abs(f.AnimationErrorTicks!.Value))),
        Statistics.FromTicks(frames.Select(f => f.DriftTicks)),
        Statistics.FromTicks(frames.Select(f => f.OnScreenTicks)),
        withMetrics.LongCount(f => capturePeriodTicks > 0 && Math.Abs(f.AnimationErrorTicks!.Value) > thresholdTicks),
        errorPerFrameMs,
        percentError,
        AverageFpsOf(frameRate),
        LowFps(frameRate, 0.99, MinFramesForOnePercentLow),
        LowFps(frameRate, 0.999, MinFramesForPointOnePercentLow),
        Statistics.FromTicks(frames.Where(f => f.CpuBusyTicks != 0).Select(f => (long)f.CpuBusyTicks)),
        Statistics.FromTicks(frames.Where(f => f.FrameTimeTicks.HasValue).Select(f => f.FrameTimeTicks!.Value)),
        Statistics.FromTicks(frames.Where(f => f.CpuWaitTicks.HasValue).Select(f => f.CpuWaitTicks!.Value)),
        frames.LongCount(f => f.DisplayDeltaTicks.HasValue && (f.Flags & PresentedFrameFlags.StaticBefore) != 0)
      );
    }

    /// <summary>
    /// The frame's display time step counts toward the frame rate numbers: it has one, and it is not a static frame's time on screen.
    /// </summary>
    public static bool CountsTowardFrameRate(PresentedFrame frame) =>
      frame.DisplayDeltaTicks.HasValue && (frame.Flags & PresentedFrameFlags.StaticBefore) == 0;

    /// <summary>A 1 % low needs at least this many frames to rest on more than the single slowest one.</summary>
    public const int MinFramesForOnePercentLow = 100;

    /// <summary>A 0.1 % low needs at least this many frames.</summary>
    public const int MinFramesForPointOnePercentLow = 1000;

    private static double AverageFpsOf(IReadOnlyCollection<PresentedFrame> frames)
    {
      long ticks = frames.Sum(f => f.DisplayDeltaTicks!.Value);
      return ticks > 0 ? frames.Count * (double)TimeSpan.TicksPerSecond / ticks : 0;
    }

    private static double? LowFps(IReadOnlyCollection<PresentedFrame> frames, double fraction, int minFrames)
    {
      if (frames.Count < minFrames)
        return null;
      var steps = frames.Select(f => f.DisplayDeltaTicks!.Value).OrderBy(t => t).ToArray();
      long step = steps[Math.Max(0, (int)Math.Ceiling(fraction * steps.Length) - 1)];
      return step > 0 ? TimeSpan.TicksPerSecond / (double)step : null;
    }

    /// <summary><see cref="ErrorPerFrameMs"/> and <see cref="PercentError"/> of frames' animation errors and display time steps, in ticks.</summary>
    public static (double ErrorPerFrameMs, double PercentError) ErrorSummary(IReadOnlyCollection<(long ErrorTicks, long DisplayStepTicks)> frames)
    {
      if (frames.Count == 0)
        return (0, 0);
      long absolute = frames.Sum(f => Math.Abs(f.ErrorTicks));
      long display = frames.Sum(f => f.DisplayStepTicks);
      return (absolute / (double)frames.Count / TimeSpan.TicksPerMillisecond, display > 0 ? absolute * 100.0 / display : 0);
    }
  }
}
