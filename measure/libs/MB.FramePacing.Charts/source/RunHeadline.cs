//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The headline of one run, the same in the GUI and the report files: its title, the headline tiles (average fps, 1 % and 0.1 % low,
//* frames visibly off, animation error p99 and p99.9, worst error, late frames) and which cause of animation error dominates.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MB.FramePacing.Analysis;

namespace MB.FramePacing.Charts
{
  public static class RunHeadline
  {
    /// <summary>"Run 1  'name'": the name the user gave, else the sequence id.</summary>
    public static string Title(RunAnalysis run) => $"Run {run.RunId}" + ((run.Name ?? run.SequenceId) is { } shown ? $"  '{shown}'" : string.Empty);

    /// <summary>"Sequence id 46521e10-…." when the title shows a name instead, else null.</summary>
    public static string? SequenceLine(RunAnalysis run) => run.Name != null && run.SequenceId != null ? $"Sequence id {run.SequenceId}." : null;

    /// <summary>The headline tiles, in the order the GUI and the report show them (<see cref="ReportItem.TileIds"/>).</summary>
    public static IReadOnlyList<HeadlineTile> Tiles(ChartRun chart)
    {
      var run = chart.Run;
      var s = run.Statistics;
      var c = run.Counts;
      var pacing = run.Pacing;
      double thresholdMs = chart.ErrorThresholdTicks / (double)TimeSpan.TicksPerMillisecond;
      int measured = s.AbsoluteAnimationErrorMs.Count;
      // The largest error either way: shown too soon (positive) or too late (negative)
      double worst = Math.Max(s.AnimationErrorMs.Max, -s.AnimationErrorMs.Min);
      string worstWay =
        worst <= 0 ? string.Empty
        : -s.AnimationErrorMs.Min >= s.AnimationErrorMs.Max ? "too late"
        : "too soon";
      var tiles = new[]
      {
        new HeadlineTile(
          ReportItem.FramesDropped,
          "Frames dropped",
          Number(c.DroppedFrames),
          c.PresentedFrames + c.DroppedFrames > 0 ? Percent(c.DroppedFrames / (double)(c.PresentedFrames + c.DroppedFrames)) : string.Empty,
          c.DroppedFrames > 0,
          "Frames the application rendered that never reached the display: frame indices skipped while the capture missed nothing, and never "
            + "shown later. Underneath, their share of the rendered frames. A capture gap is never counted as dropped."
        ),
        new HeadlineTile(
          ReportItem.OutOfOrder,
          "Out of order",
          Number(c.OutOfOrderCaptures),
          c.OutOfOrderCaptures == 1 ? "refresh" : "refreshes",
          c.OutOfOrderCaptures > 0,
          "Refreshes that showed an older frame again after a newer one: frames presented in another order than they were rendered."
        ),
        new HeadlineTile(
          ReportItem.AverageFps,
          "Average fps",
          s.AverageFps > 0 ? Invariant(s.AverageFps, "0.0") : "-",
          (s.AverageFps > 0 ? Invariant(1000 / s.AverageFps, "0.00") + " ms · " : string.Empty)
            + (s.ExcludedStaticFrames > 0 ? Number(s.ExcludedStaticFrames) + " static excluded" : Number(run.Counts.PresentedFrames) + " frames"),
          false,
          "Presented frames per second: the frames with a display time step over the time those steps cover (what the display showed); "
            + "underneath, the mean display time step and the number of presented frames, or with static frames how many are left out. "
            + StaticNote,
          s.AverageFps > 0
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
          "Frames shown at least one refresh later than the target frame time after the previous frame.",
          pacing != null
        ),
      };
      return tiles.OrderBy(t => ReportItem.TileIds.ToList().IndexOf(t.Id)).ToArray();
    }

    /// <summary>
    /// The tiles <paramref name="options"/> show: the items it shows, with a value unless empty ones are hidden, and the auto tiles (frames
    /// dropped, out of order) only when the run has either, unless shown on purpose.
    /// </summary>
    public static IReadOnlyList<HeadlineTile> Shown(ChartRun chart, ReportOptions options)
    {
      bool anyFault = chart.Run.Counts.DroppedFrames > 0 || chart.Run.Counts.OutOfOrderCaptures > 0;
      return Tiles(chart)
        .Where(t => options.IsShown(t.Id) && (t.HasValue || !options.HideEmpty))
        .Where(t => anyFault || !ReportItem.AutoTiles.Contains(t.Id) || options.Shown.Contains(t.Id))
        .ToList();
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
          + "no longer than the step underneath. "
          + StaticNote,
        fps.HasValue
      );

    /// <summary>A tail percentile of the |animation error|: the rare frames a player notices, which an average or the p95 hides.</summary>
    private static HeadlineTile ErrorPercentile(string id, string caption, double valueMs, int measured, string percentile, int minFrames) =>
      new HeadlineTile(
        id,
        caption,
        measured >= minFrames ? Invariant(valueMs, "0.0") + " ms" : "-",
        measured >= minFrames ? string.Empty : $"needs {Number(minFrames)} frames",
        false,
        $"{percentile} % of the frames have an |animation error| below this.",
        measured >= minFrames
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

    /// <summary>
    /// The display <paramref name="section"/> was shown on: its refresh rate, whether it is the fixed refresh a capture card captures at
    /// (vsync) or calculated from a camera's frames, the time per refresh, what the section's frames targeted in whole refreshes, and what the
    /// application wants when its markers say so. The report's display box and the GUI's Display card show it.
    /// </summary>
    public static DisplaySummary Display(RunSection section)
    {
      var chart = section.Run;
      var pacing = chart.Run.Pacing;
      double refreshMs = pacing?.RefreshPeriodMs ?? (chart.CapturePeriodTicks / (double)TimeSpan.TicksPerMillisecond);
      var frames = Enumerable.Range(section.Start, section.FrameCount).Select(i => section.Data.Frames[i]).ToList();
      bool mismatch = pacing?.MatchesExpectedRefresh == false;
      string kind =
        pacing == null ? "unknown"
        : pacing.RefreshCalculated ? "calculated from the camera"
        : "fixed refresh (vsync)";
      if (pacing?.ExpectedRefreshHz is { } expected && mismatch)
        kind += $", expected {expected.ToString("0.##", CultureInfo.InvariantCulture)} Hz";

      // What the frames targeted, in whole refreshes: the target frame time the marker carries (a pacer that adapts its rate, like Swappy,
      // targets several), else the target each frame was measured against. Not the schedule's step, which is longer after a late frame.
      var refreshes = frames
        .Where(f => f.DisplayDeltaTicks.HasValue && (Known(f.MarkerTargetFrameTicks) || f.TargetTicks.HasValue))
        .Select(f =>
          (int)
            Math.Round(
              (Known(f.MarkerTargetFrameTicks) ? f.MarkerTargetFrameTicks : f.TargetTicks!.Value) / (double)TimeSpan.TicksPerMillisecond / refreshMs
            )
        )
        .Distinct()
        .Order()
        .ToArray();
      string target =
        refreshes.Length == 0 ? string.Empty
        : refreshes.Length == 1 ? $", target {Refreshes(refreshes[0])} ({Invariant(1000 / (refreshes[0] * refreshMs), "0.#")} fps)"
        : $", target {refreshes[0]}–{Refreshes(refreshes[^1])}";
      return new DisplaySummary(Hz(pacing), kind, mismatch, $"{ReportCard.Ms1(refreshMs)} ms per refresh{target}", Wants(frames));
    }

    /// <summary>The refresh rate ("60 Hz"), "?? Hz" without pacing.</summary>
    public static string Hz(RunPacing? pacing) => pacing != null ? $"{Invariant(pacing.RefreshHz, "0.##")} Hz" : "?? Hz";

    /// <summary>What the application wants, when its markers say so ("preferred 60 fps", "preferred 1–60 fps, on demand"), else empty.</summary>
    private static string Wants(IReadOnlyList<PresentedFrame> frames)
    {
      var preferredFps = frames
        .Where(f => Known(f.MarkerPreferredFrameTicks))
        .Select(f => Math.Round(TimeSpan.TicksPerSecond / (double)f.MarkerPreferredFrameTicks, 1))
        .Distinct()
        .Order()
        .ToArray();
      bool onDemand = frames.Any(f => f.MarkerPreferredFrameTicks == MB.FramePacing.Marker.MarkerPayload.OnDemandFrameTicks);
      var wants = new List<string>();
      if (preferredFps.Length > 0)
      {
        string Fps(double fps) => Invariant(fps, "0.#");
        wants.Add(
          preferredFps.Length == 1 ? $"preferred {Fps(preferredFps[0])} fps" : $"preferred {Fps(preferredFps[0])}–{Fps(preferredFps[^1])} fps"
        );
      }
      if (onDemand)
        wants.Add("on demand");
      return string.Join(", ", wants);
    }

    private static bool Known(uint ticks) => ticks > 0 && ticks != MB.FramePacing.Marker.MarkerPayload.OnDemandFrameTicks;

    private static string Refreshes(int count) => count == 1 ? "1 refresh" : $"{count} refreshes";

    /// <summary>
    /// The frame rate numbers describe the frames that animate: "excluding 48 static frames" when a section has any, else null. Every frame
    /// rate number and the display time step histogram leave those display time steps out.
    /// </summary>
    public static string? ExcludedStatic(long count) =>
      count > 0 ? $"excluding {Number(count)} static frame{(count == 1 ? string.Empty : "s")}" : null;

    private const string StaticNote = "A static frame's time on screen (the marker says nothing animates) is left out.";

    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Percent(double share) => share.ToString("P1", CultureInfo.InvariantCulture);

    private static string Invariant(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
  }
}
