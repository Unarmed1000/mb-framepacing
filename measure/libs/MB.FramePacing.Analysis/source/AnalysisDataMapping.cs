//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Between the data library's analysis output (MB.FramePacing.Data: summary.json and the CSVs, the file format) and the analysis's types, in
//* both directions: the analysis writes through it, and reports read an analysis back through it to the tick.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using MB.FramePacing.Data;

namespace MB.FramePacing.Analysis
{
  public static class AnalysisDataMapping
  {
    public static CaptureCsvRow ToCsvRow(this CaptureRow row)
    {
      bool hasMarker = row.Status is CaptureStatus.Decoded or CaptureStatus.Torn && row.Payload != default;
      return new CaptureCsvRow(
        row.CaptureIndex,
        row.Status == CaptureStatus.NotRecorded ? null : row.CaptureTicks,
        row.Status.ToString(),
        hasMarker ? row.Payload.Kind.ToString() : null,
        hasMarker ? row.Payload.RunId : null,
        hasMarker ? row.Payload.FrameIndex : null,
        hasMarker ? row.Payload.AnimationTicks : null,
        row.SourceDropBefore,
        row.HostTicks,
        row.DeviceTicks,
        row.MarkerBytes,
        row.Secondary?.FrameIndex
      );
    }

    public static FrameRow ToRow(this PresentedFrame frame) =>
      new FrameRow(
        frame.Segment,
        frame.FrameIndex,
        frame.AnimationTicks,
        frame.FirstCaptureIndex,
        frame.FirstSeenTicks,
        frame.OnScreenTicks,
        frame.CaptureCount,
        frame.SkippedBefore,
        frame.DisplayDeltaTicks,
        frame.AnimationDeltaTicks,
        frame.AnimationErrorTicks,
        frame.DriftTicks,
        frame.Flags == PresentedFrameFlags.None ? Array.Empty<string>() : frame.Flags.ToString().Split(", "),
        frame.IntendedDisplayTicks != 0 ? frame.IntendedDisplayTicks : null,
        frame.MarkerTargetFrameTicks != 0 ? frame.MarkerTargetFrameTicks : null,
        frame.TargetTicks,
        frame.MarkerPreferredFrameTicks != 0 ? frame.MarkerPreferredFrameTicks : null,
        frame.PreferredTicks,
        frame.PacingErrorTicks,
        frame.PredictionErrorTicks,
        frame.LatenessTicks,
        frame.LastSeenTicks,
        frame.CpuStartTicks != 0 ? frame.CpuStartTicks : null,
        frame.CpuBusyTicks != 0 ? frame.CpuBusyTicks : null,
        frame.FrameTimeTicks,
        frame.CpuWaitTicks,
        frame.OlderFrames is { } older ? older.Select(o => new OlderFrame(o.FrameIndex, o.CaptureTicks)).ToArray() : Array.Empty<OlderFrame>(),
        frame.FirstSeenMainTicks,
        frame.FirstSeenMainTicks is { } main ? frame.FirstSeenTicks - main : null
      );

    /// <summary>A presented frame read back. Output written before lastSeenMs existed: the last capture its time on screen allows.</summary>
    public static PresentedFrame ToFrame(this FrameRow row, long capturePeriodTicks) =>
      new PresentedFrame(
        row.Segment,
        row.FrameIndex,
        row.AnimationTicks,
        row.FirstCaptureIndex,
        row.FirstSeenTicks,
        row.LastSeenTicks ?? row.FirstSeenTicks + Math.Max(0, row.OnScreenTicks - capturePeriodTicks),
        row.Captures,
        row.OnScreenTicks,
        row.SkippedBefore,
        row.DisplayDeltaTicks,
        row.AnimationDeltaTicks,
        row.AnimationErrorTicks,
        row.DriftTicks,
        row.Flags.Aggregate(PresentedFrameFlags.None, (flags, name) => flags | Enum.Parse<PresentedFrameFlags>(name)),
        row.MainMarkerFirstSeenTicks,
        row.IntendedDisplayTicks ?? 0,
        (uint)(row.MarkerTargetTicks ?? 0),
        row.TargetTicks,
        row.PacingErrorTicks,
        row.PredictionErrorTicks,
        row.LatenessTicks,
        row.CpuStartTicks ?? 0,
        (uint)(row.CpuBusyTicks ?? 0),
        row.FrameTimeTicks,
        row.CpuWaitTicks,
        (uint)(row.MarkerPreferredTicks ?? 0),
        row.PreferredTicks,
        row.OlderFrames.Count > 0 ? row.OlderFrames.Select(o => new OlderFrameCapture(o.CaptureTicks, o.FrameIndex)).ToArray() : null
      );

    public static SummaryRun ToSummary(this RunAnalysis run, string framesFile) =>
      new SummaryRun(
        run.RunId,
        run.Name,
        run.SequenceId,
        run.StartTimeUtc,
        run.HasStartMarker,
        run.HasEndMarker,
        framesFile,
        run.Counts.ToSummary(),
        run.Statistics.ToSummary(),
        run.Pacing?.ToSummary(),
        RunHistograms.Create(run).ToSummary(),
        run.Camera?.ToSummary(),
        run.Warnings
      );

    /// <summary>A run read back, with its frames (read from <see cref="SummaryRun.FramesFile"/>).</summary>
    public static RunAnalysis ToRun(this SummaryRun run, IReadOnlyList<PresentedFrame> frames) =>
      new RunAnalysis(
        run.RunId,
        run.SequenceId,
        run.StartTimeUtc,
        run.HasStartMarker,
        run.HasEndMarker,
        run.Counts.ToCounts(),
        run.Statistics.ToStatistics(),
        frames,
        run.Warnings ?? Array.Empty<string>(),
        Pacing: run.Pacing?.ToPacing(),
        Name: run.Name
      );

    public static ValueStatistics ToSummary(this Statistics s) =>
      new ValueStatistics(s.Count, s.Min, s.Mean, s.StdDev, s.P50, s.P95, s.P99, s.P999, s.Max);

    public static Statistics ToStatistics(this ValueStatistics? s) =>
      s == null ? Statistics.Empty : new Statistics(s.Count, s.Min, s.Mean, s.StdDev, s.P50, s.P95, s.P99, s.P999, s.Max);

    private static SummaryCounts ToSummary(this RunCounts c) =>
      new SummaryCounts(
        c.Captures,
        c.Decoded,
        c.Undecodable,
        c.Torn,
        c.NotRecorded,
        c.SourceDropEvents,
        c.PresentedFrames,
        c.SkippedFrameIndices,
        c.OutOfOrderCaptures,
        c.Segments
      );

    private static RunCounts ToCounts(this SummaryCounts c) =>
      new RunCounts(
        c.Captures,
        c.Decoded,
        c.Undecodable,
        c.Torn,
        c.NotRecorded,
        c.SourceDropEvents,
        c.PresentedFrames,
        c.SkippedFrameIndices,
        c.OutOfOrderCaptures,
        c.Segments
      );

    private static SummaryStatistics ToSummary(this RunStatistics s) =>
      new SummaryStatistics(
        s.DisplayDeltaMs.ToSummary(),
        s.AnimationDeltaMs.ToSummary(),
        s.AnimationErrorMs.ToSummary(),
        s.AbsoluteAnimationErrorMs.ToSummary(),
        s.DriftMs.ToSummary(),
        s.OnScreenMs.ToSummary(),
        s.FramesWithAnimationError,
        s.ErrorPerFrameMs,
        s.PercentError,
        s.AverageFps,
        s.OnePercentLowFps,
        s.PointOnePercentLowFps,
        s.ExcludedStaticFrames,
        s.UncertainSteps,
        s.CpuBusyMs.ToSummary(),
        s.FrameTimeMs.ToSummary(),
        s.CpuWaitMs.ToSummary()
      );

    private static RunStatistics ToStatistics(this SummaryStatistics s) =>
      new RunStatistics(
        s.DisplayDeltaMs.ToStatistics(),
        s.AnimationDeltaMs.ToStatistics(),
        s.AnimationErrorMs.ToStatistics(),
        s.AbsoluteAnimationErrorMs.ToStatistics(),
        s.DriftMs.ToStatistics(),
        s.OnScreenMs.ToStatistics(),
        s.FramesWithAnimationError,
        s.ErrorPerFrameMs,
        s.PercentError,
        s.AverageFps,
        s.OnePercentLowFps,
        s.PointOnePercentLowFps,
        s.CpuBusyMs.ToStatistics(),
        s.FrameTimeMs.ToStatistics(),
        s.CpuWaitMs.ToStatistics(),
        s.ExcludedStaticFrames,
        s.UncertainSteps
      );

    private static SummaryPacing ToSummary(this RunPacing p) =>
      new SummaryPacing(
        p.RefreshPeriodMs,
        p.RefreshCalculated,
        p.TargetFrameMs,
        p.Source.ToString(),
        p.LateFrames,
        p.LateShare,
        p.WorstLateShare,
        p.ErrorFramesWithUnevenDisplay,
        p.ErrorFramesWithEvenDisplay,
        p.Verdict.ToString(),
        p.ExpectedRefreshHz,
        p.PacingErrorMs?.ToSummary(),
        p.PredictionErrorMs?.ToSummary(),
        p.RefreshHz,
        p.RefreshDeviation,
        p.MatchesExpectedRefresh
      );

    private static RunPacing ToPacing(this SummaryPacing p) =>
      new RunPacing(
        p.RefreshPeriodMs,
        p.RefreshCalculated,
        p.TargetFrameMs,
        Enum.Parse<PacingSource>(p.Source),
        p.LateFrames,
        p.LateShare,
        p.WorstLateShare,
        p.ErrorFramesWithUnevenDisplay,
        p.ErrorFramesWithEvenDisplay,
        Enum.Parse<PacingVerdict>(p.Verdict),
        p.ExpectedRefreshHz
      )
      {
        PacingErrorMs = p.PacingErrorMs == null ? null : p.PacingErrorMs.ToStatistics(),
        PredictionErrorMs = p.PredictionErrorMs == null ? null : p.PredictionErrorMs.ToStatistics(),
      };

    private static SummaryHistograms ToSummary(this RunHistograms h) =>
      new SummaryHistograms(h.AnimationErrorMs.ToSummary(), h.DisplayDeltaMs.ToSummary());

    private static SummaryHistogram ToSummary(this Histogram h) =>
      new SummaryHistogram(h.BinWidthMs, h.Total, h.Bins.Select(b => new SummaryHistogramBin(b.CenterMs, b.Count)).ToList());

    private static SummaryCamera ToSummary(this CameraRunStatistics c) =>
      new SummaryCamera(c.ScanoutDelay.ToSummary(), c.FramesSeenInBothZones, c.TornFrames, c.SecondZoneOnlyFrames);
  }
}
