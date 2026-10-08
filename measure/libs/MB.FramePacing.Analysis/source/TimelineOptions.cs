//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Options for TimelineAnalyzer: the frame index jump that counts as an application restart, an optional run filter, the application's
//* target frame rate, the display refresh rate the user expects and the camera rig's calibrated refresh rate.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
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

    /// <summary>
    /// The |animation error| above which a frame counts as off, the same for every capture source. The animation error is measured exactly
    /// (the marker's animation time step against the display time step), so the default is small: 1 ms.
    /// </summary>
    public NanosecondTimeSpan ErrorThreshold { get; init; } = TimelineAnalyzer.DefaultErrorThreshold;

    /// <summary>The frame rate the application aims for (null = the markers' pacing, else one refresh per frame).</summary>
    public double? TargetFps { get; init; }

    /// <summary>
    /// The display refresh rate the user expects (Hz). A camera capture compares it with the refresh rate calculated from the frames (and
    /// uses it to settle an ambiguous estimate); a capture card, with its capture rate. Null = no comparison.
    /// </summary>
    public double? ExpectedRefreshHz { get; init; }

    /// <summary>EXPERIMENTAL camera captures: the refresh rate the rig measured at calibration, to settle an ambiguous estimate.</summary>
    public double? CalibratedRefreshHz { get; init; }

    /// <summary>
    /// Assume a rest is static when a frame the target dropped took its flag: the frame a StaticBefore flag speaks for was never shown, or,
    /// in a run that uses the static flags, a frame was dropped after a hold over which the animation clock stood still. The frame is
    /// flagged <see cref="PresentedFrameFlags.StaticAssumed"/>. Off: such a rest is judged like any other step.
    /// </summary>
    public bool AssumeStatic { get; init; } = true;
  }
}
