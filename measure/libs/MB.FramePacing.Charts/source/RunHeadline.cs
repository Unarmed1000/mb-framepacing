//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The headline of one run, the same in the GUI and the report files: its title, the headline tiles (frames, frames off, error per frame
//* and percent error, typical and worst error, late frames, the worst 2 s, the resolution) and which cause of animation error dominates.
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
    public static string Title(RunAnalysis run) => $"Run {run.RunId}" + (run.Name != null ? $"  '{run.Name}'" : string.Empty);

    /// <summary>The headline tiles, in the order the GUI and the report show them.</summary>
    public static IReadOnlyList<HeadlineTile> Tiles(ChartRun chart)
    {
      var run = chart.Run;
      var s = run.Statistics;
      var pacing = run.Pacing;
      double thresholdMs = chart.ErrorThresholdTicks / (double)TimeSpan.TicksPerMillisecond;
      double capturePeriodMs = chart.CapturePeriodTicks / (double)TimeSpan.TicksPerMillisecond;
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
          ReportItem.PresentedFrames,
          "Presented frames",
          Number(run.Counts.PresentedFrames),
          string.Empty,
          false,
          "Frames of the application seen on the display."
        ),
        new HeadlineTile(
          ReportItem.FramesOff,
          "Frames visibly off",
          Number(s.FramesWithAnimationError),
          measured > 0 ? Percent(s.FramesWithAnimationError / (double)measured) : string.Empty,
          s.FramesWithAnimationError > 0,
          $"Frames whose animation time is off by more than the error threshold ({Invariant(thresholdMs, "0.###")} ms)."
        ),
        new HeadlineTile(
          ReportItem.ErrorPerFrame,
          "Error per frame",
          Invariant(s.ErrorPerFrameMs, "0.00") + " ms",
          Invariant(s.PercentError, "0.0") + " %",
          false,
          "The mean |animation error| (Gamers Nexus's error per frame); next to it the percent error: all |animation error| as a share of the "
            + "time on screen. A healthy game stays well under 1 ms and a few %."
        ),
        new HeadlineTile(
          ReportItem.TypicalError,
          "Typical error (p95)",
          Invariant(s.AbsoluteAnimationErrorMs.P95, "0.0") + " ms",
          string.Empty,
          false,
          "95 % of the frames have an animation error below this."
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
        new HeadlineTile(
          ReportItem.WorstLate,
          "Worst 2 s late",
          pacing != null ? Percent(pacing.WorstLateShare) : "-",
          string.Empty,
          false,
          "The highest share of late frames in any 2 s stretch: rare spikes stay low, busy stretches stand out."
        ),
        new HeadlineTile(
          ReportItem.Resolution,
          "Resolution",
          Invariant(capturePeriodMs, "0.0") + " ms",
          string.Empty,
          false,
          "One capture period: the capture card's refresh (it captures at the display's refresh rate), or the camera's frame time."
        ),
      };
    }

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
