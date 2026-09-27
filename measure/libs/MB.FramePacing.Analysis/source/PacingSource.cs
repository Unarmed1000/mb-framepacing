//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Where a run's frame targets come from: the application's frame pacer (through the marker), a target frame rate given to the tools, or the
//* display's native refresh rate.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Analysis
{
  public enum PacingSource
  {
    /// <summary>The markers carry intended display times: every frame is measured against the pacer's schedule.</summary>
    Schedule,

    /// <summary>The markers carry the pacer's target frame time (no schedule): every frame against its own target.</summary>
    TargetFrameTime,

    /// <summary>No pacing information in the markers; the target frame rate given to the tools (--target-fps).</summary>
    GivenTarget,

    /// <summary>No pacing information at all: one frame per refresh, the display's native rate.</summary>
    NativeRefresh,
  }
}
