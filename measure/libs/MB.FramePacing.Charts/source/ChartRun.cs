//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What the charts of one run are drawn from: the analysed run and the capture's period and error threshold, in ticks.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;
using System.Linq;
using MB.FramePacing.Analysis;
using MB.FramePacing.Data;

namespace MB.FramePacing.Charts
{
  /// <param name="Run">The analysed run.</param>
  /// <param name="CapturePeriodTicks">The capture period (<see cref="TimelineResult.CapturePeriodTicks"/>).</param>
  /// <param name="ErrorThresholdTicks">The |animation error| above which a frame counts as off (<see cref="TimelineResult.ErrorThresholdTicks"/>).</param>
  /// <param name="Camera">An EXPERIMENTAL camera capture: the refresh strip draws every frame until the next one.</param>
  public sealed record ChartRun(RunAnalysis Run, long CapturePeriodTicks, long ErrorThresholdTicks, bool Camera)
  {
    /// <summary>
    /// The capture's rows (captures.csv), for what the capture missed (RunEvents): every run of the capture shares them. Null when not known.
    /// </summary>
    public IReadOnlyList<CaptureCsvRow>? Captures { get; init; }

    /// <param name="captures">The report's capture rows as <see cref="Captures"/> (<see cref="CapturesOf"/>, once for every run), or null to map them.</param>
    public static ChartRun From(AnalysisReport report, RunAnalysis run, IReadOnlyList<CaptureCsvRow>? captures = null) =>
      new ChartRun(run, report.Timeline.CapturePeriod.Ticks, report.Timeline.ErrorThreshold.Ticks, report.Session?.Camera != null)
      {
        Captures = captures ?? CapturesOf(report),
      };

    /// <summary>The report's capture rows as captures.csv has them.</summary>
    public static IReadOnlyList<CaptureCsvRow> CapturesOf(AnalysisReport report) => report.Capture.Rows.Select(r => r.ToCsvRow()).ToList();
  }
}
