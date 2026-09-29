//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Where a run's frame targets come from: the application's frame pacer (through the marker), a target frame rate given to the tools, or the
//* display's native refresh rate.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
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

    /// <summary>
    /// The markers carry only the frame time the application wants to run at (no schedule, no target): every frame against its preferred
    /// frame time, so a game that wants 30 fps on a 60 Hz display is measured against two refreshes.
    /// </summary>
    PreferredFrameTime,

    /// <summary>No pacing information in the markers; the target frame rate given to the tools (--target-fps).</summary>
    GivenTarget,

    /// <summary>No pacing information at all: one frame per refresh, the display's native rate.</summary>
    NativeRefresh,
  }
}
