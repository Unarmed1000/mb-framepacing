//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What the charts of one run are drawn from: the analysed run and the capture's period and error threshold, in ticks.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using MB.FramePacing.Analysis;

namespace MB.FramePacing.Charts
{
  /// <param name="Run">The analysed run.</param>
  /// <param name="CapturePeriodTicks">The capture period (<see cref="TimelineResult.CapturePeriodTicks"/>).</param>
  /// <param name="ErrorThresholdTicks">The |animation error| above which a frame counts as off (<see cref="TimelineResult.ErrorThresholdTicks"/>).</param>
  /// <param name="Camera">An EXPERIMENTAL camera capture: the refresh strip draws every frame until the next one.</param>
  public sealed record ChartRun(RunAnalysis Run, long CapturePeriodTicks, long ErrorThresholdTicks, bool Camera)
  {
    public static ChartRun From(AnalysisReport report, RunAnalysis run) =>
      new ChartRun(run, report.Timeline.CapturePeriodTicks, report.Timeline.ErrorThresholdTicks, report.Session?.Camera != null);
  }
}
