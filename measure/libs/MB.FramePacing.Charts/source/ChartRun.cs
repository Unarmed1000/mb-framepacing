//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What the charts of one run are drawn from: the analysed run and the capture's period and error threshold, in nanoseconds.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;
using System.Linq;
using MB.FramePacing.Analysis;
using MB.FramePacing.Data;
using MB.FramePacing.TraceJoin;

namespace MB.FramePacing.Charts
{
  /// <param name="Run">The analysed run.</param>
  /// <param name="CapturePeriod">The capture period (<see cref="TimelineResult.CapturePeriod"/>).</param>
  /// <param name="ErrorThreshold">The |animation error| above which a frame counts as off (<see cref="TimelineResult.ErrorThreshold"/>).</param>
  /// <param name="Camera">An EXPERIMENTAL camera capture: the refresh strip draws every frame until the next one.</param>
  public sealed record ChartRun(RunAnalysis Run, NanosecondTimeSpan CapturePeriod, NanosecondTimeSpan ErrorThreshold, bool Camera)
  {
    /// <summary>
    /// The capture's rows (captures.csv), for what the capture missed (RunEvents): every run of the capture shares them. Null when not known.
    /// </summary>
    public IReadOnlyList<CaptureCsvRow>? Captures { get; init; }

    /// <summary>
    /// EXPERIMENTAL: what a Perfetto trace of the run says of each frame (run-&lt;id&gt;-trace.csv), a row per frame of <see cref="Run"/> in
    /// its order. Null when the analysis joined no trace with the run.
    /// </summary>
    public IReadOnlyList<TraceFrame>? Trace { get; init; }

    /// <param name="captures">The report's capture rows as <see cref="Captures"/> (<see cref="CapturesOf"/>, once for every run), or null to map them.</param>
    public static ChartRun From(AnalysisReport report, RunAnalysis run, IReadOnlyList<CaptureCsvRow>? captures = null) =>
      new ChartRun(run, report.Timeline.CapturePeriod, report.Timeline.ErrorThreshold, report.Session?.Camera != null)
      {
        Captures = captures ?? CapturesOf(report),
        Trace = TraceOf(report, run),
      };

    /// <summary>What the report's trace says of <paramref name="run"/>'s frames; null when the run was not joined with one.</summary>
    private static IReadOnlyList<TraceFrame>? TraceOf(AnalysisReport report, RunAnalysis run)
    {
      for (int index = 0; index < report.Timeline.Runs.Count; ++index)
      {
        if (ReferenceEquals(report.Timeline.Runs[index], run))
          return report.TraceJoinOf(index)?.Rows;
      }
      return null;
    }

    /// <summary>The report's capture rows as captures.csv has them.</summary>
    public static IReadOnlyList<CaptureCsvRow> CapturesOf(AnalysisReport report) => report.Capture.Rows.Select(r => r.ToCsvRow()).ToList();
  }
}
