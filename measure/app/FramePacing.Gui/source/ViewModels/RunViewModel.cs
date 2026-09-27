//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One analysed run on the Analysis page: headline tiles, pacing, statistics rows, warnings and the frames behind the charts.
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
    public RunViewModel(RunAnalysis run, double capturePeriodMs, double errorThresholdMs, bool camera)
    {
      Run = run;
      CapturePeriodMs = capturePeriodMs;
      ErrorThresholdMs = errorThresholdMs;
      IsCamera = camera;
      Title = $"Run {run.RunId}" + (run.Name != null ? $"  '{run.Name}'" : string.Empty);
      var c = run.Counts;
      StartText = run.StartTimeUtc is { } start ? $"Started {start.ToLocalTime():yyyy-MM-dd HH:mm:ss}" : "No start time";
      CountsText =
        $"{c.PresentedFrames} presented frames from {c.Captures} captures: {c.Decoded} decoded, {c.Undecodable} undecodable, {c.Torn} torn, "
        + $"{c.NotRecorded} not recorded. {c.SkippedFrameIndices} frame indices never captured, {c.Segments} segment(s).";
      ErrorFramesText =
        $"{run.Statistics.FramesWithAnimationError} frame(s) with |animation error| above {errorThresholdMs.ToString("0.###", CultureInfo.InvariantCulture)} ms";
      var s = run.Statistics;
      PresentedFramesText = c.PresentedFrames.ToString("N0", CultureInfo.InvariantCulture);
      ErrorFramesTile = s.FramesWithAnimationError.ToString("N0", CultureInfo.InvariantCulture);
      HasErrorFrames = s.FramesWithAnimationError > 0;
      TypicalErrorText = s.AbsoluteAnimationErrorMs.P95.ToString("0.0 'ms'", CultureInfo.InvariantCulture);
      WorstErrorText = s.AbsoluteAnimationErrorMs.Max.ToString("0.0 'ms'", CultureInfo.InvariantCulture);
      ResolutionText = capturePeriodMs.ToString("0.0 'ms'", CultureInfo.InvariantCulture);
      if (run.Pacing is { } pacing)
      {
        LateFramesText = pacing.LateFrames.ToString("N0", CultureInfo.InvariantCulture);
        LateShareText = pacing.LateShare.ToString("P1", CultureInfo.InvariantCulture);
        WorstLateShareText = pacing.WorstLateShare.ToString("P1", CultureInfo.InvariantCulture);
        HasLateFrames = pacing.LateFrames > 0;
        TargetText =
          $"Late = shown at least one refresh after the {pacing.TargetFrameMs.ToString("0.##", CultureInfo.InvariantCulture)} ms target "
          + (pacing.TargetGiven ? "(the given target frame rate)." : "(the run's median display time).");
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
        RefreshMismatch = pacing.MatchesExpectedRefresh == false;
        VerdictText =
          pacing.Verdict switch
          {
            PacingVerdict.BadPacing => "Cause: mostly bad pacing, frames shown late or early, or dropped",
            PacingVerdict.DeltaTimeJitter => "Cause: mostly delta time jitter, an even display with uneven animation steps",
            PacingVerdict.Both => "Cause: both bad pacing and delta time jitter",
            _ => "Cause: none, no animation error above the threshold",
          } + $" ({pacing.ErrorFramesWithUnevenDisplay} error frame(s) at uneven display, {pacing.ErrorFramesWithEvenDisplay} on an even display).";
      }
      Statistics = new List<StatisticsRow>
      {
        StatisticsRow.From("Display time", s.DisplayDeltaMs),
        StatisticsRow.From("Animation time step", s.AnimationDeltaMs),
        StatisticsRow.From("Animation error", s.AnimationErrorMs),
        StatisticsRow.From("|Animation error|", s.AbsoluteAnimationErrorMs),
        StatisticsRow.From("Drift", s.DriftMs),
        StatisticsRow.From("On screen", s.OnScreenMs),
      };
    }

    public RunAnalysis Run { get; }
    public double CapturePeriodMs { get; }

    /// <summary>The |animation error| above which a frame counts as off (the band on the charts).</summary>
    public double ErrorThresholdMs { get; }
    public bool IsCamera { get; }
    public string LateFramesText { get; } = "-";
    public string LateShareText { get; } = "-";
    public string WorstLateShareText { get; } = "-";
    public bool HasLateFrames { get; }
    public string TargetText { get; } = string.Empty;

    /// <summary>The refresh rate used, where it comes from, and how it compares with the expected display rate.</summary>
    public string RefreshText { get; } = string.Empty;

    /// <summary>The refresh rate differs from the expected display rate.</summary>
    public bool RefreshMismatch { get; }
    public string VerdictText { get; } = string.Empty;
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
