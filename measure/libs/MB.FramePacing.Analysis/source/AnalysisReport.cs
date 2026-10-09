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
using MB.FramePacing.Trace;

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

    public double CapturePeriodMs => Timeline.CapturePeriod.TotalMilliseconds;

    /// <summary>The |animation error| above which a frame counts as off (<see cref="TimelineOptions.ErrorThreshold"/>).</summary>
    public double ErrorThresholdMs => Timeline.ErrorThreshold.TotalMilliseconds;
  }
}
