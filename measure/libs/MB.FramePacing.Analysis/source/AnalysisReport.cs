//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Everything CaptureAnalyzer produced for one capture: session info, decoded captures, runs and warnings.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using MB.FramePacing.Capture;
using MB.FramePacing.TraceJoin;

namespace MB.FramePacing.Analysis
{
  public sealed record AnalysisReport(
    string CaptureDirectory,
    string OutputDirectory,
    CaptureSessionInfo? Session,
    DecodedCapture Capture,
    TimelineResult Timeline,
    IReadOnlyList<string> Warnings
  )
  {
    /// <summary>
    /// EXPERIMENTAL: each run joined with the capture's Perfetto trace, in the order of the timeline's runs; null for a run that was not
    /// joined. Empty when no trace was joined at all.
    /// </summary>
    public IReadOnlyList<TraceRunJoin?> TraceJoins { get; init; } = Array.Empty<TraceRunJoin?>();

    /// <summary>The join of the run at <paramref name="runIndex"/> of the timeline's runs; null when it has none.</summary>
    public TraceRunJoin? TraceJoinOf(int runIndex) => runIndex < TraceJoins.Count ? TraceJoins[runIndex] : null;

    /// <summary>
    /// EXPERIMENTAL: each run's markers held against the frame record the application logged into a trace (slices named
    /// "mbfp frame &lt;run id&gt; &lt;frame index&gt;"), in the order of the timeline's runs; null for a run without one. Empty when no
    /// trace was read.
    /// </summary>
    public IReadOnlyList<FrameRecordCheck?> TraceRecordChecks { get; init; } = Array.Empty<FrameRecordCheck?>();

    /// <summary>
    /// EXPERIMENTAL: what the traces say of the run at <paramref name="runIndex"/>, as lines of numbers: how well its markers and the
    /// joined trace line up, or, without a join, its markers against the application's frame record. Null when there is neither.
    /// </summary>
    public IReadOnlyList<string>? TraceCheckLines(int runIndex)
    {
      if (TraceJoinOf(runIndex)?.LineUp is { } lineUp)
        return lineUp.Lines();
      if (runIndex < TraceRecordChecks.Count && TraceRecordChecks[runIndex] is { } record)
        return new[] { "The frame record of the application's own trace (experimental; numbers only, and nothing is joined):", record.Line() };
      return null;
    }

    public double CapturePeriodMs => Timeline.CapturePeriod.Value.TotalMilliseconds;

    /// <summary>The |animation error| above which a frame counts as off (<see cref="TimelineOptions.ErrorThreshold"/>).</summary>
    public double ErrorThresholdMs => Timeline.ErrorThreshold.Value.TotalMilliseconds;
  }
}
