//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Options for CaptureAnalyzer: the capture clock, the timeline rules, the report directory and the tool version.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Analysis
{
  public sealed record AnalysisOptions
  {
    public TimeSource TimeSource { get; init; } = TimeSource.Auto;
    public TimelineOptions Timeline { get; init; } = new TimelineOptions();

    /// <summary>Where the reports go; null = &lt;capture directory&gt;/analysis.</summary>
    public string? OutputDirectory { get; init; }

    public string ToolVersion { get; init; } = string.Empty;

    /// <summary>Decode the stored frames (frames.mbfc) again even when the capture data (captures.mbcd) exists, and replace the data.</summary>
    public bool Redecode { get; init; }
  }
}
