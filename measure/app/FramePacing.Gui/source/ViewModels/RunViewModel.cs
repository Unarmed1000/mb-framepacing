//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One analysed run on the Analysis page: headline tiles, statistics rows, warnings and the frames behind the charts.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System.Collections.Generic;
using System.Globalization;
using MB.FramePacing.Analysis;

namespace MB.FramePacing.Gui.ViewModels
{
  public sealed class RunViewModel
  {
    public RunViewModel(RunAnalysis run, double capturePeriodMs)
    {
      Run = run;
      CapturePeriodMs = capturePeriodMs;
      Title = $"Run {run.RunId}" + (run.Name != null ? $"  '{run.Name}'" : string.Empty);
      var c = run.Counts;
      StartText = run.StartTimeUtc is { } start ? $"Started {start.ToLocalTime():yyyy-MM-dd HH:mm:ss}" : "No start time";
      CountsText =
        $"{c.PresentedFrames} presented frames from {c.Captures} captures: {c.Decoded} decoded, {c.Undecodable} undecodable, {c.Torn} torn, "
        + $"{c.NotRecorded} not recorded. {c.SkippedFrameIndices} frame indices never captured, {c.Segments} segment(s).";
      ErrorFramesText =
        $"{run.Statistics.FramesWithAnimationError} frame(s) with |animation error| above the {capturePeriodMs.ToString("0.###", CultureInfo.InvariantCulture)} ms measurement resolution";
      var s = run.Statistics;
      PresentedFramesText = c.PresentedFrames.ToString("N0", CultureInfo.InvariantCulture);
      ErrorFramesTile = s.FramesWithAnimationError.ToString("N0", CultureInfo.InvariantCulture);
      HasErrorFrames = s.FramesWithAnimationError > 0;
      TypicalErrorText = s.AbsoluteAnimationErrorMs.P95.ToString("0.0 'ms'", CultureInfo.InvariantCulture);
      WorstErrorText = s.AbsoluteAnimationErrorMs.Max.ToString("0.0 'ms'", CultureInfo.InvariantCulture);
      ResolutionText = capturePeriodMs.ToString("0.0 'ms'", CultureInfo.InvariantCulture);
      Statistics = new List<StatisticsRow>
      {
        StatisticsRow.From("Display delta", s.DisplayDeltaMs),
        StatisticsRow.From("Animation delta", s.AnimationDeltaMs),
        StatisticsRow.From("Animation error", s.AnimationErrorMs),
        StatisticsRow.From("|Animation error|", s.AbsoluteAnimationErrorMs),
        StatisticsRow.From("Drift", s.DriftMs),
        StatisticsRow.From("On screen", s.OnScreenMs),
      };
    }

    public RunAnalysis Run { get; }
    public double CapturePeriodMs { get; }
    public string PresentedFramesText { get; }
    public string ErrorFramesTile { get; }
    public bool HasErrorFrames { get; }
    public string TypicalErrorText { get; }
    public string WorstErrorText { get; }
    public string ResolutionText { get; }
    public string Title { get; }
    public string StartText { get; }
    public string CountsText { get; }
    public string ErrorFramesText { get; }
    public IReadOnlyList<StatisticsRow> Statistics { get; }
    public IReadOnlyList<string> Warnings => Run.Warnings;
    public bool HasWarnings => Run.Warnings.Count > 0;

    public override string ToString() => Title;
  }
}
