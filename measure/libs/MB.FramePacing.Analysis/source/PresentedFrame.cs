//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One application frame as it reached the display: when it was first seen, how long it stayed, its display and animation delta, animation
//* error and drift. The animation error is not judged (null) for a step from a static frame (nothing animated while it was on screen), and
//* the drift adds up only the judged errors.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Analysis
{
  /// <param name="FirstSeenTime">
  /// When the frame was first seen: in the capture for a capture card; for an EXPERIMENTAL camera capture when the sync marker showed it.
  /// </param>
  /// <param name="MainMarkerFirstSeenTime">EXPERIMENTAL camera captures: when the main marker (which identifies the frame) first showed it.</param>
  /// <param name="IntendedDisplayTime">From the marker: when the pacer intended the frame to be shown (its steady clock), 0 = unknown.</param>
  /// <param name="MarkerTargetFrameTime">From the marker: the pacer's target frame time, 0 = unknown.</param>
  /// <param name="TargetFrameTime">The frame time this frame is measured against, in whole refreshes (<see cref="RunPacing.Source"/>).</param>
  /// <param name="PacingError">With a schedule: the display time step minus the intended step.</param>
  /// <param name="PredictionError">With a schedule: the animation time step minus the intended step.</param>
  /// <param name="Lateness">With a schedule: how long after its intended time the frame appeared, relative to the run's on-time frames.</param>
  /// <param name="CpuStartTime">From the marker: the CPU start time, when the CPU started working on the frame (the pacer's clock), 0 = unknown.</param>
  /// <param name="CpuBusy">From the marker: CPU busy, how long the CPU worked on the frame before presenting it, 0 = unknown.</param>
  /// <param name="FrameTime">
  /// The frametime (PresentMon's MsBetweenAppStart): from this frame's CPU start to the next frame's, when the next frame index was captured
  /// and both carry a CPU start time.
  /// </param>
  /// <param name="CpuWait">CPU wait (PresentMon's MsCPUWait): the frametime minus CPU busy, when both are known.</param>
  /// <param name="MarkerPreferredFrameTime">
  /// From the marker: the frame time the application wants to run at, 0 = unknown, <c>MarkerPayload.OnDemandFrameTime</c> = frames only
  /// when something changes.
  /// </param>
  /// <param name="PreferredFrameTime">
  /// The frame time the application wants, in whole refreshes: the marker's preferred frame time, else the target frame rate given to the
  /// tools, else one refresh (<see cref="RunPacing"/>). Null when the application presents on demand.
  /// </param>
  /// <param name="OlderFrames">
  /// The captures that showed an older frame out of order while this frame was the newest (before the next presented frame), in capture
  /// order; null when none did.
  /// </param>
  public sealed record PresentedFrame(
    int Segment,
    ulong FrameIndex,
    TimeSpan AnimationTime,
    long FirstCaptureIndex,
    TickCount64 FirstSeenTime,
    TickCount64 LastSeenTime,
    int CaptureCount,
    TimeSpan OnScreen,
    ulong SkippedBefore,
    TimeSpan? DisplayDelta,
    TimeSpan? AnimationDelta,
    TimeSpan? AnimationError,
    TimeSpan Drift,
    PresentedFrameFlags Flags,
    TickCount64? MainMarkerFirstSeenTime = null,
    TickCount64 IntendedDisplayTime = default,
    TimeSpan32 MarkerTargetFrameTime = default,
    TimeSpan? TargetFrameTime = null,
    TimeSpan? PacingError = null,
    TimeSpan? PredictionError = null,
    TimeSpan? Lateness = null,
    TickCount64 CpuStartTime = default,
    TimeSpan32 CpuBusy = default,
    TimeSpan? FrameTime = null,
    TimeSpan? CpuWait = null,
    TimeSpan32 MarkerPreferredFrameTime = default,
    TimeSpan? PreferredFrameTime = null,
    System.Collections.Generic.IReadOnlyList<OlderFrameCapture>? OlderFrames = null
  );
}
