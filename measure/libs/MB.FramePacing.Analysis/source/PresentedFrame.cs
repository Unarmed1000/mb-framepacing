//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One application frame as it reached the display: when it was first seen, how long it stayed, its display and animation delta, animation
//* error and drift. The animation error is not judged (null) for a step from or to a static frame (nothing animates in it), and the drift
//* adds up only the judged errors.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Analysis
{
  /// <param name="FirstSeenTicks">
  /// When the frame was first seen: in the capture for a capture card; for an EXPERIMENTAL camera capture when the sync marker showed it.
  /// </param>
  /// <param name="FirstSeenMainTicks">EXPERIMENTAL camera captures: when the main marker (which identifies the frame) first showed it.</param>
  /// <param name="IntendedDisplayTicks">From the marker: when the pacer intended the frame to be shown (its steady clock), 0 = unknown.</param>
  /// <param name="MarkerTargetFrameTicks">From the marker: the pacer's target frame time, 0 = unknown.</param>
  /// <param name="TargetTicks">The frame time this frame is measured against, in whole refreshes (<see cref="RunPacing.Source"/>).</param>
  /// <param name="PacingErrorTicks">With a schedule: the display time step minus the intended step.</param>
  /// <param name="PredictionErrorTicks">With a schedule: the animation time step minus the intended step.</param>
  /// <param name="LatenessTicks">With a schedule: how long after its intended time the frame appeared, relative to the run's on-time frames.</param>
  /// <param name="CpuStartTicks">From the marker: the CPU start time, when the CPU started working on the frame (the pacer's clock), 0 = unknown.</param>
  /// <param name="CpuBusyTicks">From the marker: CPU busy, how long the CPU worked on the frame before presenting it, 0 = unknown.</param>
  /// <param name="FrameTimeTicks">
  /// The frametime (PresentMon's MsBetweenAppStart): from this frame's CPU start to the next frame's, when the next frame index was captured
  /// and both carry a CPU start time.
  /// </param>
  /// <param name="CpuWaitTicks">CPU wait (PresentMon's MsCPUWait): the frametime minus CPU busy, when both are known.</param>
  /// <param name="MarkerPreferredFrameTicks">
  /// From the marker: the frame time the application wants to run at, 0 = unknown, <c>MarkerPayload.OnDemandFrameTicks</c> = frames only
  /// when something changes.
  /// </param>
  /// <param name="PreferredTicks">
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
    long AnimationTicks,
    long FirstCaptureIndex,
    long FirstSeenTicks,
    long LastSeenTicks,
    int CaptureCount,
    long OnScreenTicks,
    ulong SkippedBefore,
    long? DisplayDeltaTicks,
    long? AnimationDeltaTicks,
    long? AnimationErrorTicks,
    long DriftTicks,
    PresentedFrameFlags Flags,
    long? FirstSeenMainTicks = null,
    long IntendedDisplayTicks = 0,
    uint MarkerTargetFrameTicks = 0,
    long? TargetTicks = null,
    long? PacingErrorTicks = null,
    long? PredictionErrorTicks = null,
    long? LatenessTicks = null,
    long CpuStartTicks = 0,
    uint CpuBusyTicks = 0,
    long? FrameTimeTicks = null,
    long? CpuWaitTicks = null,
    uint MarkerPreferredFrameTicks = 0,
    long? PreferredTicks = null,
    System.Collections.Generic.IReadOnlyList<OlderFrameCapture>? OlderFrames = null
  );
}
