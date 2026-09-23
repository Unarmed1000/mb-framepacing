//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Everything CaptureAnalyzer produced for one capture: session info, decoded captures, runs and warnings.
//*
//* (c) 2026 Mana Battery
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
  }
}
