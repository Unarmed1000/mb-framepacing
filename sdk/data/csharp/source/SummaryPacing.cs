//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* summary.json's runs[].pacing: the refresh, the target the frames are measured against, late frames and the verdict.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Data
{
  /// <param name="RefreshPeriodMs">The display's refresh period (a capture card's capture period, or calculated from a camera's frames).</param>
  /// <param name="TargetFrameMs">The frame time the run is measured against, in whole refreshes.</param>
  /// <param name="Source">
  /// "Schedule", "TargetFrameTime", "PreferredFrameTime", "GivenTarget" or "NativeRefresh": where the targets come from.
  /// </param>
  /// <param name="LateShare">Late frames as a share of the presented frames with a display time step (0..1).</param>
  /// <param name="WorstLateShare">The highest share of late frames in any 2 s window (0..1).</param>
  /// <param name="Verdict">"None", "BadPacing", "DeltaTimeJitter" or "Both".</param>
  /// <param name="ExpectedRefreshHz">The refresh rate the user expects, if given.</param>
  /// <param name="PacingErrorMs">With a schedule: display time step minus intended step.</param>
  /// <param name="PredictionErrorMs">With a schedule: animation time step minus intended step.</param>
  /// <param name="RefreshDeviation">The refresh rate relative to the expected one (0.01 = 1 % faster), null without one.</param>
  public sealed record SummaryPacing(
    double RefreshPeriodMs,
    bool RefreshCalculated,
    double TargetFrameMs,
    string Source,
    long LateFrames,
    double LateShare,
    double WorstLateShare,
    long ErrorFramesWithUnevenDisplay,
    long ErrorFramesWithEvenDisplay,
    string Verdict,
    double? ExpectedRefreshHz,
    ValueStatistics? PacingErrorMs,
    ValueStatistics? PredictionErrorMs,
    double RefreshHz,
    double? RefreshDeviation,
    bool? MatchesExpectedRefresh
  );
}
