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
      double thresholdMs = chart.ErrorThreshold.Value.TotalMilliseconds;
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
          "Frames the frame before stayed on screen for half a refresh or more longer than they were animated for and than their intended period.",
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

    /// <summary>
    /// What the run's captures held, the GUI's counts line and a note on the card: "336 presented frames from 480 captures: 480 decoded, 0
    /// undecodable, ...".
    /// </summary>
    public static string Counts(RunAnalysis run)
    {
      var c = run.Counts;
      return $"{c.PresentedFrames} presented frames from {c.Captures} captures: {c.Decoded} decoded, {c.Undecodable} undecodable, {c.Torn} torn, "
        + $"{c.NotRecorded} not recorded; {c.SourceDroppedFrames} dropped by the source, {c.MissedCaptures} missed. "
        + $"{c.SkippedFrameIndices} frame indices never captured, {c.Segments} segment(s).";
    }

    /// <summary>Which cause of animation error dominates, with the counts behind it ("mostly bad pacing: ... (3 error frame(s) ...).").</summary>
    public static string Cause(RunPacing pacing)
    {
      string counts =
        $"{pacing.ErrorFramesWithUnevenDisplay} error frame(s) at uneven display, {pacing.ErrorFramesWithEvenDisplay} on an even display";
      return pacing.Verdict switch
      {
        PacingVerdict.BadPacing => $"mostly bad pacing: display time steps off their target, or frames dropped ({counts}).",
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
      double refreshMs = (pacing?.RefreshPeriod ?? chart.CapturePeriod).Value.TotalMilliseconds;
      var frames = Enumerable.Range(section.Start, section.FrameCount).Select(i => section.Data.Frames[i]).ToList();
      bool expectedMismatch = pacing?.MatchesExpectedRefresh == false;
      var marked = MarkedDisagreeing(section);
      string kind =
        pacing == null ? "unknown"
        : pacing.RefreshCalculated ? "calculated from the camera"
        : "fixed refresh (vsync)";
      if (pacing?.ExpectedRefreshHz is { } expected && expectedMismatch)
        kind += $", expected {expected.ToString("0.##", CultureInfo.InvariantCulture)} Hz";
      if (marked.Count > 0)
        kind += $", markers {string.Join(", ", marked.Select(m => $"{Invariant(m.Hz, "0.##")} Hz"))}";
      bool mismatch = expectedMismatch || marked.Count > 0;

      // What the frames targeted, in whole refreshes: the target frame time the marker carries (a pacer that adapts its rate,
      // targets several), else the preferred frame time, as the reference lines show it. Not the schedule's step, which is longer after a
      // late frame, and not a frame's due time after frames the target dropped (an application faster than the display skips before most)
      var refreshes = frames
        .Where(f => f.DisplayDelta.HasValue)
        .Select(FrameReference.Target)
        .OfType<NanosecondTimeDuration>()
        .Select(target => (int)Math.Round(target.Value.TotalMilliseconds / refreshMs))
        .Distinct()
        .Order()
        .ToArray();
      string target =
        refreshes.Length == 0 ? string.Empty
        : refreshes.Length == 1 ? $", target {Refreshes(refreshes[0])} ({Invariant(1000 / (refreshes[0] * refreshMs), "0.#")} fps)"
        : $", target {refreshes[0]}–{Refreshes(refreshes[^1])}";
      return new DisplaySummary(Hz(pacing), kind, mismatch, $"{ReportCard.Ms1(refreshMs)} ms per refresh{target}", Wants(frames));
    }

    /// <summary>
    /// The card's warning when the display and the capture disagree (<see cref="RefreshAgreement"/>, the analysis's rule): the section's
    /// frames carry a refresh period in their markers that is not the refresh the analysis measured with, or the user expected another
    /// display rate (--display-hz). The facts, then what to check; empty when they agree. It never says which side is wrong.
    /// </summary>
    public static IReadOnlyList<string> RefreshWarning(RunSection section)
    {
      var pacing = section.Run.Run.Pacing;
      if (pacing == null)
        return Array.Empty<string>();
      string measured = pacing.RefreshCalculated
        ? $"the frames give a {Invariant(pacing.RefreshHz, "0.##")} Hz display"
        : $"the capture runs at {Invariant(pacing.RefreshHz, "0.##")} fps";
      var lines = new List<string>();
      var marked = MarkedDisagreeing(section);
      if (marked.Count > 0)
        lines.Add($"The markers say the application works with {RefreshAgreement.Displays(marked)}, but {measured}.");
      if (pacing.MatchesExpectedRefresh == false && pacing.ExpectedRefreshHz is { } expected)
        lines.Add($"A {Invariant(expected, "0.##")} Hz display was expected (--display-hz), but {measured}.");
      if (lines.Count == 0)
        return lines;
      lines.Add(
        pacing.RefreshCalculated
          ? "Check the display's refresh rate, the camera's frame rate and the refresh period the application writes."
          : "A capture card must capture at the display's refresh rate: check the card's mode, the display's refresh rate and the refresh period the application writes."
      );
      return lines;
    }

    /// <summary>
    /// The card's warning band: its title and lines, the display and the capture disagreeing (<see cref="RefreshWarning"/>) first, then the
    /// run's vsync warning (<see cref="VsyncOffCheck"/>: the setup is the run's, so a section shows it too). No lines when neither applies.
    /// </summary>
    public static (string Title, IReadOnlyList<string> Lines) Warning(RunSection section)
    {
      var refresh = RefreshWarning(section);
      var vsyncOff = section.Data.VsyncOff;
      string title =
        refresh.Count > 0 && vsyncOff.Count > 0 ? "The display and the capture disagree, and vsync is probably off"
        : refresh.Count > 0 ? "The display and the capture disagree"
        : "Vsync is probably off";
      return (title, refresh.Concat(vsyncOff).ToArray());
    }

    /// <summary>The refresh periods the section's markers carry that do not agree with the refresh the analysis measured with.</summary>
    private static IReadOnlyList<MarkedRefresh> MarkedDisagreeing(RunSection section)
    {
      if (section.Run.Run.Pacing is not { } pacing)
        return Array.Empty<MarkedRefresh>();
      var frames = Enumerable.Range(section.Start, section.FrameCount).Select(i => section.Data.Frames[i]);
      return RefreshAgreement.Disagreeing(RefreshAgreement.MarkerPeriods(frames), pacing.RefreshHz);
    }

    /// <summary>The refresh rate ("60 Hz"), "?? Hz" without pacing.</summary>
    public static string Hz(RunPacing? pacing) => pacing != null ? $"{Invariant(pacing.RefreshHz, "0.##")} Hz" : "?? Hz";

    /// <summary>What the application wants, when its markers say so ("preferred 60 fps", "preferred 1–60 fps, on demand"), else empty.</summary>
    private static string Wants(IReadOnlyList<PresentedFrame> frames)
    {
      var preferredFps = frames
        .Where(f => Known(f.MarkerPreferredFrameTime))
        .Select(f => Math.Round(NanosecondTimeSpan.NanosecondsPerSecond / (double)f.MarkerPreferredFrameTime.Nanoseconds, 1))
        .Distinct()
        .Order()
        .ToArray();
      bool onDemand = frames.Any(f => f.MarkerPreferredFrameTime == MB.FramePacing.MarkerDecoding.MarkerPayload.OnDemandFrameTime);
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

    /// <summary>
    /// What the markers of <paramref name="section"/> say about pacing (the report's pacer band and the GUI's Pacer line): whether a pacer
    /// or a frame limiter held its frames, a checkbox per pacing field, and what the analysis measured them against.
    /// <list type="bullet">
    /// <item>Only the markers' paced flag answers, the application's word: a frame without it was not held by a pacer or a limiter (the
    /// user, 2026-10-29: a run without the bit is not paced). Nothing is worked out from the other fields ("stop guessing now we have a
    /// bit").</item>
    /// <item>The checkboxes are facts: ✓ every frame of the section carries the field, its share when some do, – none.</item>
    /// </list>
    /// </summary>
    public static PacingSummary Pacing(RunSection section)
    {
      var chart = section.Run;
      var pacing = chart.Run.Pacing;
      var refresh = pacing?.RefreshPeriod ?? chart.CapturePeriod;
      var frames = Enumerable.Range(section.Start, section.FrameCount).Select(i => section.Data.Frames[i]).ToList();
      int count = frames.Count;
      int paced = frames.Count(f => f.Flags.HasFlag(PresentedFrameFlags.Paced));
      int scheduled = frames.Count(f => f.IntendedDisplayTime != default);
      int targeted = frames.Count(f => f.MarkerTargetFrameTime != NanosecondTimeDuration.Zero);
      int preferred = frames.Count(f => f.MarkerPreferredFrameTime != NanosecondTimeDuration.Zero);
      var onDemand = MB.FramePacing.MarkerDecoding.MarkerPayload.OnDemandFrameTime;
      int targetOnDemand = frames.Count(f => f.MarkerTargetFrameTime == onDemand);
      int preferredOnDemand = frames.Count(f => f.MarkerPreferredFrameTime == onDemand);
      // The target slower than what the application prefers, in whole refreshes as the analysis rounds them (the pacer runs below its rate)
      int slower = frames.Count(f =>
        Known(f.MarkerTargetFrameTime)
        && Known(f.MarkerPreferredFrameTime)
        && FrameTimeRounding.WholeRefreshes(f.MarkerTargetFrameTime, refresh) > FrameTimeRounding.WholeRefreshes(f.MarkerPreferredFrameTime, refresh)
      );
      var targetFps = FpsOf(frames.Select(f => f.MarkerTargetFrameTime));
      var preferredFps = FpsOf(frames.Select(f => f.MarkerPreferredFrameTime));

      // The mark is the share itself when only some frames carry the field: a half-filled circle reads as a dot at the card's size
      PacingFieldRow Row(string name, int part, string detail) =>
        new PacingFieldRow(
          part == 0 ? "–"
            : part == count ? "✓"
            : PercentOf(part, count),
          name,
          part == 0 ? string.Empty : detail
        );
      // A field's rates, and how much of it is on demand: "on demand" alone when every frame is
      string Says(double[] fps, int onDemandFrames, string more)
      {
        var says = new List<string>();
        if (fps.Length > 0)
          says.Add(FpsRange(fps));
        if (onDemandFrames > 0)
          says.Add(onDemandFrames == count ? "on demand" : $"on demand in {PercentOf(onDemandFrames, count)}");
        if (more.Length > 0)
          says.Add(more);
        return string.Join(", ", says);
      }
      var rows = new[]
      {
        Row("paced flag", paced, string.Empty),
        Row("schedule (intended display times)", scheduled, string.Empty),
        Row("target frame time", targeted, Says(targetFps, targetOnDemand, string.Empty)),
        Row(
          "preferred frame time",
          preferred,
          Says(preferredFps, preferredOnDemand, slower > 0 ? $"target slower in {PercentOf(slower, count)}" : string.Empty)
        ),
      };

      var (answer, value, basis) =
        paced == 0
          ? (PacedAnswer.NotPaced, "no", "no frame carries the paced flag")
          : (PacedAnswer.Paced, paced == count ? "yes" : $"in {PercentOf(paced, count)} of the frames", "the markers say so");
      return new PacingSummary(answer, value, basis, rows, pacing != null ? TargetSource(pacing.Source) : string.Empty);
    }

    /// <summary>Where the frames' targets come from, in words ("the pacer's schedule in the markers"): the card, the GUI and analyze say it.</summary>
    public static string TargetSource(PacingSource source) =>
      source switch
      {
        PacingSource.Schedule => "the pacer's schedule in the markers",
        PacingSource.TargetFrameTime => "the pacer's target frame time in the markers",
        PacingSource.PreferredFrameTime => "the preferred frame time in the markers",
        PacingSource.GivenTarget => "the given target frame rate",
        _ => "no pacing information: the display's native refresh rate",
      };

    /// <summary>The distinct frame rates of the known frame times (neither unknown nor on demand), to a tenth of a frame, slowest first.</summary>
    private static double[] FpsOf(IEnumerable<NanosecondTimeDuration> frameTimes) =>
      frameTimes
        .Where(Known)
        .Select(t => Math.Round(NanosecondTimeSpan.NanosecondsPerSecond / (double)t.Nanoseconds, 1))
        .Distinct()
        .Order()
        .ToArray();

    private static string FpsRange(double[] fps) =>
      fps.Length == 1 ? $"{Invariant(fps[0], "0.#")} fps" : $"{Invariant(fps[0], "0.#")}–{Invariant(fps[^1], "0.#")} fps";

    /// <summary>A share as a whole percent, never 0 % for some or 100 % for not all ("40 %").</summary>
    private static string PercentOf(int part, int whole)
    {
      long percent = Math.Clamp((long)Math.Round(100.0 * part / whole, MidpointRounding.AwayFromZero), part > 0 ? 1 : 0, part < whole ? 99 : 100);
      return $"{percent} %";
    }

    /// <summary>The marker says a frame time: neither unknown (0) nor on demand.</summary>
    private static bool Known(NanosecondTimeDuration frameTime) =>
      frameTime != NanosecondTimeDuration.Zero && frameTime != MB.FramePacing.MarkerDecoding.MarkerPayload.OnDemandFrameTime;

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
