//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One analysed run on the Analysis page: headline tiles, pacing, statistics rows, warnings and the frames behind the charts.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using MB.FramePacing.Analysis;
using MB.FramePacing.Charts;

namespace MB.FramePacing.Gui.ViewModels
{
  public sealed class RunViewModel
  {
    public RunViewModel(ChartRun chart)
    {
      var run = chart.Run;
      double capturePeriodMs = chart.CapturePeriodTicks / (double)TimeSpan.TicksPerMillisecond;
      double errorThresholdMs = chart.ErrorThresholdTicks / (double)TimeSpan.TicksPerMillisecond;
      Chart = chart;
      Run = run;
      CapturePeriodMs = capturePeriodMs;
      ErrorThresholdMs = errorThresholdMs;
      IsCamera = chart.Camera;
      Title = RunHeadline.Title(run);
      Tiles = RunHeadline.Shown(chart, ReportOptions.Default);
      TileColumns = ReportOptions.Default.TilesPerRowFor(Tiles.Count);
      Display = RunHeadline.Display(RunSection.Whole(chart));
      var c = run.Counts;
      StartText = run.StartTimeUtc is { } start ? $"Started {start.ToLocalTime():yyyy-MM-dd HH:mm:ss}" : "No start time";
      CountsText =
        $"{c.PresentedFrames} presented frames from {c.Captures} captures: {c.Decoded} decoded, {c.Undecodable} undecodable, {c.Torn} torn, "
        + $"{c.NotRecorded} not recorded; {c.SourceDroppedFrames} dropped by the source, {c.MissedCaptures} missed. "
        + $"{c.SkippedFrameIndices} frame indices never captured, {c.Segments} segment(s).";
      ErrorFramesText =
        $"{run.Statistics.FramesWithAnimationError} frame(s) with |animation error| above {errorThresholdMs.ToString("0.###", CultureInfo.InvariantCulture)} ms";
      var s = run.Statistics;
      if (run.Pacing is { } pacing)
      {
        string target = pacing.TargetFrameMs.ToString("0.##", CultureInfo.InvariantCulture);
        TargetText = pacing.Source switch
        {
          PacingSource.Schedule =>
            $"Late = shown half a refresh or more after the frame's intended display time (the pacer's schedule in the markers, typically {target} ms per frame).",
          PacingSource.TargetFrameTime =>
            $"Late = on screen half a refresh or more beyond each frame's target frame time (the pacer's target in the markers, typically {target} ms).",
          PacingSource.PreferredFrameTime =>
            $"Late = on screen half a refresh or more beyond the frame time the application wants (its preferred frame time in the markers, typically {target} ms).",
          PacingSource.GivenTarget => $"Late = on screen half a refresh or more beyond the {target} ms target (the given target frame rate).",
          _ => $"Late = on screen half a refresh or more beyond the {target} ms target (no pacing information: the display's native refresh rate).",
        };
        RefreshText =
          string.Create(
            CultureInfo.InvariantCulture,
            $"Display refresh {pacing.RefreshHz:0.##} Hz ({(pacing.RefreshCalculated ? "calculated from the camera frames" : "the capture rate")})"
          )
          + (
            pacing.ExpectedRefreshHz is { } expected && pacing.RefreshDeviation is { } deviation
              ? string.Create(
                CultureInfo.InvariantCulture,
                $", expected {expected:0.##} Hz: {(pacing.MatchesExpectedRefresh == true ? "matches" : $"differs by {deviation:+0.0%;-0.0%}")}"
              )
              : string.Empty
          )
          + ".";
        VerdictText = "Cause: " + RunHeadline.Cause(pacing);
      }
      var statistics = new List<StatisticsRow>
      {
        // The display time steps describe the frames that animate, as the frame rates do
        StatisticsRow.From(
          "Display time step" + (RunHeadline.ExcludedStatic(s.ExcludedStaticFrames) is { } excluded ? $" ({excluded})" : string.Empty),
          s.DisplayDeltaMs
        ),
        StatisticsRow.From("Animation time step", s.AnimationDeltaMs),
        StatisticsRow.From("Animation error", s.AnimationErrorMs),
        StatisticsRow.From("|Animation error|", s.AbsoluteAnimationErrorMs),
        StatisticsRow.From("Drift", s.DriftMs),
        StatisticsRow.From("On screen", s.OnScreenMs),
      };
      // With the pacer's schedule in the markers: the animation error split into its two parts
      if (run.Pacing?.PacingErrorMs is { } pacingError && run.Pacing.PredictionErrorMs is { } predictionError)
      {
        statistics.Add(StatisticsRow.From("Pacing error", pacingError));
        statistics.Add(StatisticsRow.From("Prediction error", predictionError));
      }
      // The application side, when the markers carry it
      if (s.FrameTimeMs.Count > 0)
        statistics.Add(StatisticsRow.From("Frametime", s.FrameTimeMs));
      if (s.CpuBusyMs.Count > 0)
        statistics.Add(StatisticsRow.From("CPU busy", s.CpuBusyMs));
      if (s.CpuWaitMs.Count > 0)
        statistics.Add(StatisticsRow.From("CPU wait", s.CpuWaitMs));
      Statistics = statistics;
    }

    /// <summary>What the charts are drawn from.</summary>
    public ChartRun Chart { get; }

    public RunAnalysis Run { get; }
    public double CapturePeriodMs { get; }

    /// <summary>The |animation error| above which a frame counts as off (the band on the charts).</summary>
    public double ErrorThresholdMs { get; }
    public bool IsCamera { get; }
    public string TargetText { get; } = string.Empty;

    /// <summary>The refresh rate used, where it comes from, and how it compares with the expected display rate: the Display card's tooltip.</summary>
    public string RefreshText { get; } = string.Empty;

    /// <summary>The Display card: the report's display box (RunHeadline.Display) for the whole run.</summary>
    public DisplaySummary Display { get; }

    /// <summary>The Display card has a line for what the application wants.</summary>
    public bool HasWants => Display.Wants.Length > 0;
    public string VerdictText { get; } = string.Empty;
    public string Title { get; }

    /// <summary>The headline tiles, the same the report's Timeline image shows.</summary>
    public IReadOnlyList<HeadlineTile> Tiles { get; }

    /// <summary>How many tiles go in a row: the report card's rule, two rows (ReportOptions.TilesPerRowFor).</summary>
    public int TileColumns { get; }
    public string StartText { get; }
    public string CountsText { get; }
    public string ErrorFramesText { get; }
    public IReadOnlyList<StatisticsRow> Statistics { get; }
    public IReadOnlyList<string> Warnings => Run.Warnings;
    public bool HasWarnings => Run.Warnings.Count > 0;

    public override string ToString() => Title;
  }
}
