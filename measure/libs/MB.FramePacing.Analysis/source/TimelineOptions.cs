//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Options for TimelineAnalyzer: the frame index jump that counts as an application restart, and an optional run filter.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Analysis
{
  public sealed record TimelineOptions
  {
    /// <summary>A backwards application frame index jump larger than this starts a new segment (application restart).</summary>
    public ulong RestartThresholdFrames { get; init; } = 1000;

    /// <summary>Only analyse this run id (null = all runs).</summary>
    public uint? RunId { get; init; }
  }
}
