//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The tracks a capture's measurements are added to a trace as, in the order they are shown. A track is a line of the trace viewer: each
//* holds one kind of thing, in time order, and none of its slices overlap.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Trace
{
  /// <summary>Experimental. A track of a capture's measurements in a trace; the order is the order they are shown in.</summary>
  public enum TraceTrack
  {
    /// <summary>A slice per frame, from its display time for its time on screen.</summary>
    OnScreen,

    /// <summary>A slice per frame from its intended display time to the next frame's.</summary>
    Intended,

    /// <summary>A slice from a frame's CPU start time for its CPU busy time.</summary>
    Cpu,
    AnimationError,
    DisplayStep,
    Lateness,

    /// <summary>Late frames, frame indices that were skipped, frames out of order, torn frames.</summary>
    FrameEvents,

    /// <summary>Captures not decoded, not recorded, dropped by the source or missed.</summary>
    CaptureEvents,
  }
}
