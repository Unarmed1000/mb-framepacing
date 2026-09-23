//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Options for CaptureAnalyzer: the capture clock, the timeline rules, the report directory and the tool version.
//*
//* (c) 2026 Mana Battery
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
  }
}
