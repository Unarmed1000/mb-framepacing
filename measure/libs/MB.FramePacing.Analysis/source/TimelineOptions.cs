//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Options for TimelineAnalyzer: the frame index jump that counts as an application restart, an optional run filter, the application's
//* target frame rate, the display refresh rate the user expects and the camera rig's calibrated refresh rate.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

namespace MB.FramePacing.Analysis
{
  public sealed record TimelineOptions
  {
    /// <summary>A backwards application frame index jump larger than this starts a new segment (application restart).</summary>
    public ulong RestartThresholdFrames { get; init; } = 1000;

    /// <summary>How a capture relates to the scanout (EXPERIMENTAL: Camera).</summary>
    public ScanoutModel Scanout { get; init; } = ScanoutModel.SingleScanout;

    /// <summary>Only analyse this run id (null = all runs).</summary>
    public uint? RunId { get; init; }

    /// <summary>The frame rate the application aims for (null = each run's median display time, in whole refreshes).</summary>
    public double? TargetFps { get; init; }

    /// <summary>
    /// The display refresh rate the user expects (Hz). A camera capture compares it with the refresh rate calculated from the frames (and
    /// uses it to settle an ambiguous estimate); a capture card, with its capture rate. Null = no comparison.
    /// </summary>
    public double? ExpectedRefreshHz { get; init; }

    /// <summary>EXPERIMENTAL camera captures: the refresh rate the rig measured at calibration, to settle an ambiguous estimate.</summary>
    public double? CalibratedRefreshHz { get; init; }
  }
}
