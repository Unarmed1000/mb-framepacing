//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Notes on a presented frame: application frames were skipped before it, its first-seen time is uncertain, it was torn or it was late.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
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

    /// <summary>EXPERIMENTAL camera captures: the frame reached the second (lower) zone before the timing zone, so it was presented mid-scanout.</summary>
    Torn = 4,

    /// <summary>
    /// Shown half a refresh or more after its intended display time, or without a schedule half a refresh or more beyond its target frame
    /// time after the previous frame (see <see cref="RunPacing"/>).
    /// </summary>
    Late = 8,

    /// <summary>From the marker: nothing animates in this frame, so the animation error of a step from or to it is not judged.</summary>
    Static = 16,

    /// <summary>
    /// The frame before it is static: this frame's display time step is that static frame's time on screen, which the frame rate numbers
    /// (average fps, the lows, the display time step statistics and histogram) leave out.
    /// </summary>
    StaticBefore = 32,
  }
}
