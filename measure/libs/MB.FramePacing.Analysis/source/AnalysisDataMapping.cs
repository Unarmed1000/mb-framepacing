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
using System.Collections.Concurrent;
using System.Collections.Frozen;
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
        row.Status == CaptureStatus.NotRecorded ? null : row.CaptureTime,
        NameOf(row.Status),
        hasMarker ? NameOf(row.Payload.Kind) : null,
        hasMarker ? row.Payload.RunId : null,
        hasMarker ? row.Payload.FrameIndex : null,
        hasMarker ? row.Payload.AnimationTime : null,
        row.SourceDrops,
        row.MissedBefore,
        row.Sync?.RunId,
        row.Sync?.FrameIndex,
        row.HostTime,
        row.DeviceTime,
        row.MarkerBytes
      );
    }

    public static FrameRow ToRow(this PresentedFrame frame) =>
      new FrameRow(
        frame.Segment,
        frame.FrameIndex,
        frame.AnimationTime,
        frame.FirstCaptureIndex,
        frame.FirstSeenTime,
        frame.OnScreen,
        frame.CaptureCount,
        frame.SkippedBefore,
        frame.DisplayDelta,
        frame.AnimationDelta,
        frame.AnimationError,
        frame.Drift,
        frame.Flags == PresentedFrameFlags.None ? Array.Empty<string>() : g_flagNames.GetOrAdd(frame.Flags, flags => flags.ToString().Split(", ")),
        Known(frame.IntendedDisplayTime),
        Known(frame.MarkerTargetFrameTime),
        frame.TargetFrameTime,
        Known(frame.MarkerPreferredFrameTime),
        frame.PreferredFrameTime,
        frame.PacingError,
        frame.PredictionError,
        frame.Lateness,
        frame.LastSeenTime,
        Known(frame.CpuStartTime),
        Known(frame.CpuBusy),
        frame.FrameTime,
        frame.CpuWait,
        frame.OlderFrames is { } older ? older.Select(o => new OlderFrame(o.FrameIndex, o.CaptureTime)).ToArray() : Array.Empty<OlderFrame>(),
        frame.MainMarkerFirstSeenTime,
        frame.MainMarkerFirstSeenTime is { } main ? frame.FirstSeenTime - main : null
      );

    // The names an output file repeats on every line are made once, not once per line: a run of an hour has a million lines
    private static readonly ConcurrentDictionary<PresentedFrameFlags, string[]> g_flagNames =
      new ConcurrentDictionary<PresentedFrameFlags, string[]>();
    private static readonly ConcurrentDictionary<string, PresentedFrameFlags> g_flagsByName = new ConcurrentDictionary<string, PresentedFrameFlags>(
      StringComparer.Ordinal
    );

    /// <summary>An enum value's name, as its ToString gives it.</summary>
    private static string NameOf<T>(T value)
      where T : struct, Enum => EnumNames<T>.Names.TryGetValue(value, out string? name) ? name : value.ToString();

    /// <summary>The flags a frames CSV names: each name is one of the enum's (anything else is an <see cref="ArgumentException"/>, as Enum.Parse's).</summary>
    private static PresentedFrameFlags FlagsOf(IReadOnlyList<string> names)
    {
      var flags = PresentedFrameFlags.None;
      for (int i = 0; i < names.Count; ++i)
        flags |= g_flagsByName.GetOrAdd(names[i], name => Enum.Parse<PresentedFrameFlags>(name));
      return flags;
    }

    private static class EnumNames<T>
      where T : struct, Enum
    {
      // Every value the enum names, known up front and only read afterwards: a frozen table
      public static readonly FrozenDictionary<T, string> Names = Enum.GetValues<T>().Distinct().ToFrozenDictionary(v => v, v => v.ToString());
    }

    // A marker's value of 0 means unknown: the output leaves it out
    private static TickCount64? Known(TickCount64 time) => time != default ? time : null;

    private static TimeSpan32? Known(TimeSpan32 span) => span != TimeSpan32.Zero ? span : null;

    /// <summary>A presented frame read back. Output written before lastSeenTicks existed: the last capture its time on screen allows.</summary>
    public static PresentedFrame ToFrame(this FrameRow row, TimeSpan capturePeriod) =>
      new PresentedFrame(
        row.Segment,
        row.FrameIndex,
        row.AnimationTime,
        row.FirstCaptureIndex,
        row.FirstSeenTime,
        row.LastSeenTime ?? row.FirstSeenTime + new TimeSpan(Math.Max(0, row.OnScreen.Ticks - capturePeriod.Ticks)),
        row.Captures,
        row.OnScreen,
        row.SkippedBefore,
        row.DisplayDelta,
        row.AnimationDelta,
        row.AnimationError,
        row.Drift,
        FlagsOf(row.Flags),
        row.MainMarkerFirstSeenTime,
        row.IntendedDisplayTime ?? default,
        row.MarkerTargetFrameTime ?? default,
        row.TargetFrameTime,
        row.PacingError,
        row.PredictionError,
        row.Lateness,
        row.CpuStartTime ?? default,
        row.CpuBusy ?? default,
        row.FrameTime,
        row.CpuWait,
        row.MarkerPreferredFrameTime ?? default,
        row.PreferredFrameTime,
        row.OlderFrames.Count > 0 ? row.OlderFrames.Select(o => new OlderFrameCapture(o.CaptureTime, o.FrameIndex)).ToArray() : null
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
        c.SourceDroppedFrames,
        c.MissedCaptures,
        c.PresentedFrames,
        c.SkippedFrameIndices,
        c.DroppedFrames,
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
        c.SourceDroppedFrames,
        c.MissedCaptures,
        c.PresentedFrames,
        c.SkippedFrameIndices,
        c.DroppedFrames,
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
        p.RefreshPeriod,
        p.RefreshCalculated,
        p.TargetFrameTime,
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
        p.RefreshPeriod,
        p.RefreshCalculated,
        p.TargetFrameTime,
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
