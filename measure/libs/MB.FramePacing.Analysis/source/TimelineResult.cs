//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Result of TimelineAnalyzer: the capture period, the animation error threshold, every analysed run and the warnings.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;

namespace MB.FramePacing.Analysis
{
  /// <param name="ErrorThresholdTicks">The |animation error| above which a frame counts as off (<see cref="TimelineOptions.ErrorThresholdTicks"/>).</param>
  public sealed record TimelineResult(
    long CapturePeriodTicks,
    long ErrorThresholdTicks,
    IReadOnlyList<RunAnalysis> Runs,
    IReadOnlyList<string> Warnings
  );
}
