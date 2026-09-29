//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One line of a run's frames CSV (run-<id>-frames.csv, doc/analysis-output-format.md): one presented frame. Times are 100 ns ticks; null is
//* an empty cell. The display side comes from the capture, the pacing and CPU fields from the markers.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System.Collections.Generic;

namespace MB.FramePacing.Data
{
  /// <param name="Segment">The part of the run between two gaps in the capture (0 first).</param>
  /// <param name="FirstSeenTicks">The frame's display time: when it was first seen (the capture's clock).</param>
  /// <param name="OnScreenTicks">How long it stayed on screen.</param>
  /// <param name="Captures">How many captures showed it.</param>
  /// <param name="SkippedBefore">Frame indices before this one that were never seen.</param>
  /// <param name="DisplayDeltaTicks">The display time step: from the previous frame's display time to this one's.</param>
  /// <param name="AnimationErrorTicks">The animation time step minus the display time step.</param>
  /// <param name="Flags">"SkippedBefore", "UncertainStart", "Torn", "Late"; empty when none.</param>
  /// <param name="IntendedDisplayTicks">From the marker: when the pacer intended the frame to be shown (its own clock).</param>
  /// <param name="MarkerTargetTicks">From the marker: the pacer's target frame time.</param>
  /// <param name="TargetTicks">The frame time this frame is measured against, in whole refreshes.</param>
  /// <param name="LastSeenTicks">When the last capture showing it was taken; null in output written before it existed.</param>
  /// <param name="CpuStartTicks">From the marker: the CPU start time (PresentMon's CPUStartTime, the pacer's clock).</param>
  /// <param name="CpuBusyTicks">From the marker: CPU busy (PresentMon's MsCPUBusy).</param>
  /// <param name="FrameTimeTicks">From this frame's CPU start to the next frame's (PresentMon's MsBetweenAppStart).</param>
  /// <param name="CpuWaitTicks">The frametime minus CPU busy (PresentMon's MsCPUWait).</param>
  /// <param name="MainMarkerFirstSeenTicks">EXPERIMENTAL camera captures: when the main marker first showed the frame.</param>
  /// <param name="ScanoutDelayTicks">EXPERIMENTAL camera captures: first seen minus the main marker's first seen.</param>
  public sealed record FrameRow(
    int Segment,
    ulong FrameIndex,
    long AnimationTicks,
    long FirstCaptureIndex,
    long FirstSeenTicks,
    long OnScreenTicks,
    int Captures,
    ulong SkippedBefore,
    long? DisplayDeltaTicks,
    long? AnimationDeltaTicks,
    long? AnimationErrorTicks,
    long DriftTicks,
    IReadOnlyList<string> Flags,
    long? IntendedDisplayTicks,
    long? MarkerTargetTicks,
    long? TargetTicks,
    long? PacingErrorTicks,
    long? PredictionErrorTicks,
    long? LatenessTicks,
    long? LastSeenTicks,
    long? CpuStartTicks,
    long? CpuBusyTicks,
    long? FrameTimeTicks,
    long? CpuWaitTicks,
    long? MainMarkerFirstSeenTicks = null,
    long? ScanoutDelayTicks = null
  );
}
