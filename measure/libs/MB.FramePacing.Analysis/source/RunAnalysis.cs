//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The analysis of one test run (start marker to end marker): counts, statistics, presented frames, pacing and warnings.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
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
    IReadOnlyList<string> Warnings,
    CameraRunStatistics? Camera = null,
    RunPacing? Pacing = null
  );
}
