//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Options for CaptureRunner: output directory, duration, start and end marker triggering, ring size, live preview and the version information
//* stored in capture.json.
//*
//* (c) 2026 Mana Battery
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

    public string ToolVersion { get; init; } = string.Empty;
    public string? FfmpegVersion { get; init; }
    public string? FfmpegCommandLine { get; init; }
  }
}
