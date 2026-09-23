//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Notes on a presented frame: application frames were skipped before it, or its first-seen time is uncertain.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Analysis
{
  [Flags]
  public enum PresentedFrameFlags
  {
    None = 0,

    /// <summary>Application frames were rendered but never captured before this one (skipped or shown shorter than a capture period).</summary>
    SkippedBefore = 1,

    /// <summary>Captures before this frame's first capture could not be decoded or were not recorded, so its first-seen time is uncertain.</summary>
    UncertainStart = 2,
  }
}
