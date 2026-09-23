//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The analysis of one test run (start marker to end marker): counts, statistics, presented frames and warnings.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;

namespace MB.FramePacing.Analysis
{
  public sealed record RunAnalysis(
    uint RunId,
    string? Name,
    DateTime? StartTimeUtc,
    bool HasStartMarker,
    bool HasEndMarker,
    RunCounts Counts,
    RunStatistics Statistics,
    IReadOnlyList<PresentedFrame> Frames,
    IReadOnlyList<string> Warnings
  );
}
