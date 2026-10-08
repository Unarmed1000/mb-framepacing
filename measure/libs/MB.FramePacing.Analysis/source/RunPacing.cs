//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Pacing of one run: the refresh and target frame time used (and the refresh the user expected), late frames, the share of late frames
//* over time and which cause dominates the animation error.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Analysis
{
  /// <summary>Pacing of one run. A frame is late when it is shown at least one refresh later than the target frame time.</summary>
  /// <param name="RefreshPeriod">
  /// The display's refresh period: the capture period for a capture card (it captures at the display's refresh rate), calculated from the
  /// frames for an EXPERIMENTAL camera capture.
  /// </param>
  /// <param name="RefreshCalculated">True when the refresh was calculated from a camera capture's frames.</param>
  /// <param name="TargetFrameTime">
  /// The frame time the run is measured against, in whole refreshes: the median of every frame's target (the lower of the two middle ones
  /// for an even number of frames, so it is always a target a frame had).
  /// </param>
  /// <param name="Source">Where the targets come from: the pacer's schedule or target frame time in the markers, a given target frame rate, or
  /// the display's native refresh rate.</param>
  /// <param name="LateFrames">
  /// Presented frames shown late: at least half a refresh after their intended time with a schedule, otherwise at least one refresh later than
  /// their target frame time after the previous frame.
  /// </param>
  /// <param name="LateShare">Late frames as a share of the presented frames with a display time step (0..1).</param>
  /// <param name="WorstLateShare">The highest share of late frames in any <see cref="LateShare.WindowSeconds"/> window (0..1).</param>
  /// <param name="ErrorFramesWithUnevenDisplay">
  /// Frames with animation error where this or the previous frame was shown late or early, or that follow skipped frames: bad pacing.
  /// </param>
  /// <param name="ErrorFramesWithEvenDisplay">Frames with animation error on an even display: delta time jitter.</param>
  /// <param name="Verdict">Which of the two dominates.</param>
  /// <param name="ExpectedRefreshHz">The display refresh rate the user expects, if given (compared with <see cref="RefreshHz"/>).</param>
  public sealed record RunPacing(
    NanosecondTimeSpan RefreshPeriod,
    bool RefreshCalculated,
    NanosecondTimeSpan TargetFrameTime,
    PacingSource Source,
    long LateFrames,
    double LateShare,
    double WorstLateShare,
    long ErrorFramesWithUnevenDisplay,
    long ErrorFramesWithEvenDisplay,
    PacingVerdict Verdict,
    double? ExpectedRefreshHz = null
  )
  {
    /// <summary>With a schedule: every frame's display time step minus its intended step (ms). Late or early frames, as the pacer sees them.</summary>
    public Statistics? PacingErrorMs { get; init; }

    /// <summary>
    /// With a schedule: every frame's animation time step minus its intended step (ms). The game animated for another moment than it planned
    /// to show the frame (a naive delta time, say). The animation error is the prediction error minus the pacing error.
    /// </summary>
    public Statistics? PredictionErrorMs { get; init; }

    /// <summary>The refresh rate the run was measured with (Hz).</summary>
    public double RefreshHz =>
      RefreshPeriod > NanosecondTimeSpan.Zero ? NanosecondTimeSpan.NanosecondsPerSecond / (double)RefreshPeriod.Nanoseconds : 0;

    /// <summary><see cref="RefreshHz"/> relative to <see cref="ExpectedRefreshHz"/>: 0.01 = 1 % faster; null without an expected rate.</summary>
    public double? RefreshDeviation => ExpectedRefreshHz is > 0 && RefreshHz > 0 ? (RefreshHz / ExpectedRefreshHz.Value) - 1 : null;

    /// <summary>False when the refresh differs from the expected rate by more than <see cref="TimelineAnalyzer.RefreshTolerance"/>.</summary>
    public bool? MatchesExpectedRefresh => RefreshDeviation is { } deviation ? Math.Abs(deviation) <= TimelineAnalyzer.RefreshTolerance : null;
  }
}
