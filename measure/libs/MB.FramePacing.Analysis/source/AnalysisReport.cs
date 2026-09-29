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
    public double CapturePeriodMs => Timeline.CapturePeriodTicks / (double)TimeSpan.TicksPerMillisecond;

    /// <summary>The |animation error| above which a frame counts as off (<see cref="TimelineOptions.ErrorThresholdTicks"/>).</summary>
    public double ErrorThresholdMs => Timeline.ErrorThresholdTicks / (double)TimeSpan.TicksPerMillisecond;
  }
}
