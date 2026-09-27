//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One application frame as it reached the display: when it was first seen, how long it stayed, its display and animation delta, animation
//* error and drift.
//*
//* (c) 2026 Mana Battery
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
  /// <param name="PacingErrorTicks">With a schedule: the display step minus the intended step.</param>
  /// <param name="PredictionErrorTicks">With a schedule: the animation time step minus the intended step.</param>
  /// <param name="LatenessTicks">With a schedule: how long after its intended time the frame appeared, relative to the run's on-time frames.</param>
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
    long? LatenessTicks = null
  );
}
