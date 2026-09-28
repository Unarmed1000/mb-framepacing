//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Options for CaptureRunner: output directory, duration, start and end marker triggering, ring size, live preview, the target frame
//* rate, the expected display refresh rate and the version information stored in capture.json.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using MB.FramePacing.Capture.Camera;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture
{
  public sealed record CaptureRunOptions
  {
    public required string OutputDirectory { get; init; }

    /// <summary>Stop after this long (measured from the first written frame when waiting for a start marker). Null = until cancelled.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Hold frames back until a start marker is seen (a short pre-roll is kept).</summary>
    public bool WaitForStart { get; init; }

    /// <summary>Stop once the end marker of the run has been seen (plus <see cref="EndTail"/>).</summary>
    public bool StopAtEnd { get; init; }

    public TimeSpan EndTail { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Also store the captured frames themselves (frames.mbfc). Off by default: the capture data (captures.mbcd, every frame's decoded markers
    /// and timestamps) is all the analysis needs, and frames take width x height bytes each.
    /// </summary>
    public bool KeepFrames { get; init; }

    /// <summary>Ring size in frames; null sizes it for one second of frames (at most 512 MiB).</summary>
    public int? RingFrames { get; init; }

    /// <summary>
    /// Receives a copy of the newest frame roughly every <see cref="FrameRecorderOptions.PreviewInterval"/> (on the runner's thread; the image
    /// is reused, copy what you need before returning). Null = no preview.
    /// </summary>
    public Action<GrayImage, long>? Preview { get; init; }

    /// <summary>EXPERIMENTAL: the camera rig whose rectified zones the source delivers (recorded in capture.json for the analysis).</summary>
    public CameraRig? Camera { get; init; }

    /// <summary>The real recording rate of a slow motion clip, if the timestamps were generated from it.</summary>
    public double? RecordedFps { get; init; }

    /// <summary>The frame rate the application aims for, stored in capture.json for the analysis (null = judged from the frames).</summary>
    public double? TargetFps { get; init; }

    /// <summary>The capture's name, stored in capture.json; reports show it instead of the sequence id (null = none).</summary>
    public string? Name { get; init; }

    /// <summary>The display refresh rate the user expects, stored in capture.json for the analysis to compare with (null = none).</summary>
    public double? ExpectedRefreshHz { get; init; }

    public string ToolVersion { get; init; } = string.Empty;
    public string? FfmpegVersion { get; init; }
    public string? FfmpegCommandLine { get; init; }
  }
}
