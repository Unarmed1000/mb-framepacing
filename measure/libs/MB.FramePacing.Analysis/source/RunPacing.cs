//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Pacing of one run: the refresh and target frame time used (and the refresh the user expected), late frames, the share of late frames
//* over time and which cause dominates the animation error.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Analysis
{
  /// <summary>Pacing of one run. A frame is late when it is shown at least one refresh later than the target frame time.</summary>
  /// <param name="RefreshPeriodMs">
  /// The display's refresh period: the capture period for a capture card (it captures at the display's refresh rate), calculated from the
  /// frames for an EXPERIMENTAL camera capture.
  /// </param>
  /// <param name="RefreshCalculated">True when the refresh was calculated from a camera capture's frames.</param>
  /// <param name="TargetFrameMs">The frame time the run is measured against, in whole refreshes.</param>
  /// <param name="TargetGiven">True when it comes from the given target frame rate, false when from the run's median display time.</param>
  /// <param name="LateFrames">Presented frames shown at least one refresh later than the target frame time.</param>
  /// <param name="LateShare">Late frames as a share of the presented frames with a display time (0..1).</param>
  /// <param name="WorstLateShare">The highest share of late frames in any <see cref="LateShare.WindowSeconds"/> window (0..1).</param>
  /// <param name="ErrorFramesWithUnevenDisplay">
  /// Frames with animation error where this or the previous frame was shown late or early, or that follow skipped frames: bad pacing.
  /// </param>
  /// <param name="ErrorFramesWithEvenDisplay">Frames with animation error on an even display: delta time jitter.</param>
  /// <param name="Verdict">Which of the two dominates.</param>
  /// <param name="ExpectedRefreshHz">The display refresh rate the user expects, if given (compared with <see cref="RefreshHz"/>).</param>
  public sealed record RunPacing(
    double RefreshPeriodMs,
    bool RefreshCalculated,
    double TargetFrameMs,
    bool TargetGiven,
    long LateFrames,
    double LateShare,
    double WorstLateShare,
    long ErrorFramesWithUnevenDisplay,
    long ErrorFramesWithEvenDisplay,
    PacingVerdict Verdict,
    double? ExpectedRefreshHz = null
  )
  {
    /// <summary>The refresh rate the run was measured with (Hz).</summary>
    public double RefreshHz => RefreshPeriodMs > 0 ? 1000 / RefreshPeriodMs : 0;

    /// <summary><see cref="RefreshHz"/> relative to <see cref="ExpectedRefreshHz"/>: 0.01 = 1 % faster; null without an expected rate.</summary>
    public double? RefreshDeviation => ExpectedRefreshHz is > 0 && RefreshHz > 0 ? (RefreshHz / ExpectedRefreshHz.Value) - 1 : null;

    /// <summary>False when the refresh differs from the expected rate by more than <see cref="TimelineAnalyzer.RefreshTolerance"/>.</summary>
    public bool? MatchesExpectedRefresh => RefreshDeviation is { } deviation ? Math.Abs(deviation) <= TimelineAnalyzer.RefreshTolerance : null;
  }
}
