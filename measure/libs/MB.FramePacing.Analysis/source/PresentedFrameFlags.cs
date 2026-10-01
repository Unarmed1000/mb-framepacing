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

    /// <summary>
    /// From the markers: nothing animates while this frame is on screen, until the next frame. Said by the frame itself (its StaticAfter
    /// flag) or by the next frame (its StaticBefore flag, when the application only knew it then). The step from it is not judged.
    /// </summary>
    StaticAfter = 16,

    /// <summary>
    /// The frame before it is static after: this frame's display time step is that frame's time on screen, in which nothing animated. The
    /// step has no animation error, and the frame rate numbers (average fps, the lows, the display time step statistics and histogram) leave
    /// it out.
    /// </summary>
    StaticBefore = 32,

    /// <summary>
    /// A capture card missed a moment this display time step depends on: this frame or the one before it was first seen after captures
    /// that were not decoded, not recorded or dropped by the source, so it may have appeared earlier. The step is not judged: no animation
    /// error, no late verdict, and the frame rate numbers leave it out.
    /// </summary>
    UncertainStep = 64,

    /// <summary>
    /// This frame's <see cref="StaticAfter"/> is assumed, not said by a marker: a frame dropped next to it took the flag with it, or was the
    /// frame a StaticBefore flag spoke for (TimelineOptions.AssumeStatic). It counts as static everywhere; the flag tells it apart.
    /// </summary>
    StaticAssumed = 128,
  }
}
