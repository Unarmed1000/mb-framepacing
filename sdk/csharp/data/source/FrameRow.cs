//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One line of a run's frames CSV (run-<id>-frames.csv, doc/analysis-output-format.md): one presented frame. Points in time are
//* NanosecondTickCounts, on the capture's clock or the frame pacer's; spans are NanosecondTimeSpans; the marker's own durations (a u32 in
//* the file) NanosecondTimeDurations; null is an empty cell. The display side comes from the capture, the pacing and CPU fields from the markers. The C++ data module's FrameRow.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System.Collections.Generic;

namespace MB.FramePacing.Data
{
  /// <param name="Segment">The part of the run between two gaps in the capture (0 first).</param>
  /// <param name="AnimationTime">From the marker: the animation time.</param>
  /// <param name="FirstSeenTime">The frame's display time: when it was first seen (the capture's clock).</param>
  /// <param name="OnScreen">How long it stayed on screen.</param>
  /// <param name="Captures">How many captures showed it.</param>
  /// <param name="SkippedBefore">Frame indices before this one that were never seen.</param>
  /// <param name="DisplayDelta">The display time step: from the previous frame's display time to this one's.</param>
  /// <param name="AnimationDelta">The animation time step: from the previous frame's animation time to this one's.</param>
  /// <param name="AnimationError">The animation time step minus the display time step.</param>
  /// <param name="Drift">The sum of the judged animation errors so far.</param>
  /// <param name="Flags">"SkippedBefore", "UncertainStart", "Torn", "Late", "StaticAfter", "StaticBefore", "UncertainStep", "StaticAssumed"; empty when none.</param>
  /// <param name="IntendedDisplayTime">From the marker: when the pacer intended the frame to be shown (its own clock).</param>
  /// <param name="MarkerTargetFrameTime">From the marker: the pacer's target frame time; Payload.OnDemandFrameTime = on demand.</param>
  /// <param name="TargetFrameTime">The frame time this frame is measured against, in whole refreshes; null on demand.</param>
  /// <param name="MarkerPreferredFrameTime">
  /// From the marker: the frame time the application wants to run at; Payload.OnDemandFrameTime = on demand.
  /// </param>
  /// <param name="PreferredFrameTime">
  /// The preferred frame time the late share is measured against, in whole refreshes: the marker's, else the target frame rate given to the
  /// tools, else one refresh; null on demand.
  /// </param>
  /// <param name="LastSeenTime">When the last capture showing it was taken; null in output written before it existed.</param>
  /// <param name="CpuStartTime">From the marker: the CPU start time (PresentMon's CPUStartTime, the pacer's clock).</param>
  /// <param name="CpuBusy">From the marker: CPU busy (PresentMon's MsCPUBusy).</param>
  /// <param name="FrameTime">From this frame's CPU start to the next frame's (PresentMon's MsBetweenAppStart).</param>
  /// <param name="CpuWait">The frametime minus CPU busy (PresentMon's MsCPUWait).</param>
  /// <param name="OlderFrames">The captures that showed an older frame out of order while this frame was the newest, in capture order.</param>
  /// <param name="MainMarkerFirstSeenTime">EXPERIMENTAL camera captures: when the main marker first showed the frame.</param>
  /// <param name="ScanoutDelay">EXPERIMENTAL camera captures: first seen minus the main marker's first seen.</param>
  public sealed record FrameRow(
    int Segment,
    ulong FrameIndex,
    NanosecondTimeSpan AnimationTime,
    long FirstCaptureIndex,
    NanosecondTickCount FirstSeenTime,
    NanosecondTimeSpan OnScreen,
    int Captures,
    ulong SkippedBefore,
    NanosecondTimeSpan? DisplayDelta,
    NanosecondTimeSpan? AnimationDelta,
    NanosecondTimeSpan? AnimationError,
    NanosecondTimeSpan Drift,
    IReadOnlyList<string> Flags,
    NanosecondTickCount? IntendedDisplayTime,
    NanosecondTimeDuration? MarkerTargetFrameTime,
    NanosecondTimeSpan? TargetFrameTime,
    NanosecondTimeDuration? MarkerPreferredFrameTime,
    NanosecondTimeSpan? PreferredFrameTime,
    NanosecondTimeSpan? PacingError,
    NanosecondTimeSpan? PredictionError,
    NanosecondTimeSpan? Lateness,
    NanosecondTickCount? LastSeenTime,
    NanosecondTickCount? CpuStartTime,
    NanosecondTimeDuration? CpuBusy,
    NanosecondTimeSpan? FrameTime,
    NanosecondTimeSpan? CpuWait,
    IReadOnlyList<OlderFrame> OlderFrames,
    NanosecondTickCount? MainMarkerFirstSeenTime = null,
    NanosecondTimeSpan? ScanoutDelay = null
  );
}
