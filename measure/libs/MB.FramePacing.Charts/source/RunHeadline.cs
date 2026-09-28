//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The headline of one run, the same in the GUI and the report files: its title, the headline tiles (average fps, 1 % and 0.1 % low,
//* frames visibly off, animation error p99 and p99.9, worst error, late frames) and which cause of animation error dominates.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using MB.FramePacing.Analysis;

namespace MB.FramePacing.Charts
{
  public static class RunHeadline
  {
    /// <summary>"Run 1  'name'".</summary>
    public static string Title(RunAnalysis run) => $"Run {run.RunId}" + (run.SequenceId != null ? $"  '{run.SequenceId}'" : string.Empty);

    /// <summary>The headline tiles, in the order the GUI and the report show them.</summary>
    public static IReadOnlyList<HeadlineTile> Tiles(ChartRun chart)
    {
      var run = chart.Run;
      var s = run.Statistics;
      var pacing = run.Pacing;
      double thresholdMs = chart.ErrorThresholdTicks / (double)TimeSpan.TicksPerMillisecond;
      int measured = s.AbsoluteAnimationErrorMs.Count;
      // The largest error either way: shown too soon (positive) or too late (negative)
      double worst = Math.Max(s.AnimationErrorMs.Max, -s.AnimationErrorMs.Min);
      string worstWay =
        worst <= 0 ? string.Empty
        : -s.AnimationErrorMs.Min >= s.AnimationErrorMs.Max ? "too late"
        : "too soon";
      return new[]
      {
        new HeadlineTile(
          ReportItem.AverageFps,
          "Average fps",
          s.AverageFps > 0 ? Invariant(s.AverageFps, "0.0") : "-",
          (s.AverageFps > 0 ? Invariant(1000 / s.AverageFps, "0.00") + " ms · " : string.Empty) + Number(run.Counts.PresentedFrames) + " frames",
          false,
          "Presented frames per second: the frames with a display time step over the time those steps cover (what the display showed); "
            + "underneath, the mean display time step and the number of presented frames."
        ),
        Low(ReportItem.OnePercentLow, "1 % low", s.OnePercentLowFps, "99", RunStatistics.MinFramesForOnePercentLow),
        Low(ReportItem.PointOnePercentLow, "0.1 % low", s.PointOnePercentLowFps, "99.9", RunStatistics.MinFramesForPointOnePercentLow),
        new HeadlineTile(
          ReportItem.FramesOff,
          "Frames visibly off",
          Number(s.FramesWithAnimationError),
          measured > 0 ? Percent(s.FramesWithAnimationError / (double)measured) : string.Empty,
          s.FramesWithAnimationError > 0,
          $"Frames whose animation time is off by more than the error threshold ({Invariant(thresholdMs, "0.###")} ms)."
        ),
        ErrorPercentile(ReportItem.ErrorP99, "Error p99", s.AbsoluteAnimationErrorMs.P99, measured, "99", RunStatistics.MinFramesForOnePercentLow),
        ErrorPercentile(
          ReportItem.ErrorP999,
          "Error p99.9",
          s.AbsoluteAnimationErrorMs.P999,
          measured,
          "99.9",
          RunStatistics.MinFramesForPointOnePercentLow
        ),
        new HeadlineTile(
          ReportItem.WorstError,
          "Worst error",
          Invariant(worst, "0.0") + " ms",
          worstWay,
          false,
          "The largest animation error, and which way: shown too soon or too late."
        ),
        new HeadlineTile(
          ReportItem.LateFrames,
          "Late frames",
          pacing != null ? Number(pacing.LateFrames) : "-",
          pacing != null ? Percent(pacing.LateShare) : string.Empty,
          pacing?.LateFrames > 0,
          "Frames shown at least one refresh later than the target frame time after the previous frame."
        ),
      };
    }

    /// <summary>A 1 % or 0.1 % low: the frame rate at that percentile of the display time steps, with the step itself underneath.</summary>
    private static HeadlineTile Low(string id, string caption, double? fps, string percentile, int minFrames) =>
      new HeadlineTile(
        id,
        caption,
        fps is { } value ? Invariant(value, "0.0") : "-",
        fps is { } shown ? Invariant(1000 / shown, "0.0") + " ms" : $"needs {Number(minFrames)} frames",
        false,
        $"The frame rate at the {percentile}th percentile display time step (nearest rank): {percentile} % of the frames stayed on screen "
          + "no longer than the step underneath."
      );

    /// <summary>A tail percentile of the |animation error|: the rare frames a player notices, which an average or the p95 hides.</summary>
    private static HeadlineTile ErrorPercentile(string id, string caption, double valueMs, int measured, string percentile, int minFrames) =>
      new HeadlineTile(
        id,
        caption,
        measured >= minFrames ? Invariant(valueMs, "0.0") + " ms" : "-",
        measured >= minFrames ? string.Empty : $"needs {Number(minFrames)} frames",
        false,
        $"{percentile} % of the frames have an |animation error| below this."
      );

    /// <summary>Which cause of animation error dominates, with the counts behind it ("mostly bad pacing: ... (3 error frame(s) ...).").</summary>
    public static string Cause(RunPacing pacing)
    {
      string counts =
        $"{pacing.ErrorFramesWithUnevenDisplay} error frame(s) at uneven display, {pacing.ErrorFramesWithEvenDisplay} on an even display";
      return pacing.Verdict switch
      {
        PacingVerdict.BadPacing => $"mostly bad pacing: frames shown late or early, or dropped ({counts}).",
        PacingVerdict.DeltaTimeJitter => $"mostly delta time jitter: an even display with uneven animation steps ({counts}).",
        PacingVerdict.Both => $"both bad pacing and delta time jitter ({counts}).",
        _ => "no animation error above the threshold.",
      };
    }

    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Percent(double share) => share.ToString("P1", CultureInfo.InvariantCulture);

    private static string Invariant(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
  }
}
