//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Everything FfmpegCaptureSource needs to start ffmpeg: the executable, the device and mode, crop and scale, and known frame times of image
//* sequences.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using MB.FramePacing.Capture.Camera;
using MB.FramePacing.MarkerDecoding;

namespace MB.FramePacing.Capture.Ffmpeg
{
  public sealed record FfmpegCaptureOptions
  {
    public required string FfmpegPath { get; init; }
    public required CaptureDevice Device { get; init; }

    /// <summary>Requested device mode (size and/or frame rate). Unset parts are left to the device default.</summary>
    public RequestedMode Mode { get; init; }

    /// <summary>Device pixel format or codec ("mjpeg", "yuyv422", "nv12", ...), null for the device default.</summary>
    public string? InputFormat { get; init; }

    /// <summary>Crop in source pixels applied before scaling.</summary>
    public PixelRect? Roi { get; init; }

    /// <summary>Stored frame size after crop; null keeps the (cropped) source size.</summary>
    public (int Width, int Height)? Scale { get; init; }

    /// <summary>
    /// A second crop in source pixels, stored below <see cref="Roi"/> as one frame: the sync marker's region next to the main marker's, so
    /// a capture that stores only the markers still checks tearing. Needs <see cref="Roi"/>; both are downscaled by
    /// <see cref="RoiDownscale"/> (their sizes are multiples of it) and <see cref="Scale"/> is not used.
    /// </summary>
    public PixelRect? SyncRoi { get; init; }

    /// <summary>The integer area downscale of <see cref="Roi"/> and <see cref="SyncRoi"/> when both are stored; 1 keeps the source's pixels.</summary>
    public int RoiDownscale { get; init; } = 1;

    /// <summary>DirectShow real-time buffer (absorbs short hiccups before ffmpeg drops frames).</summary>
    public int RealTimeBufferMegabytes { get; init; } = 1024;

    /// <summary>
    /// Known capture times of the frames, in order. When set they replace ffmpeg's timestamps and the capture ends after the
    /// last one. Used for image sequences, where the times come from --fps or a timestamp file and ffmpeg's own are too coarse.
    /// </summary>
    public IReadOnlyList<NanosecondTickCount>? FrameTimestamps { get; init; }

    /// <summary>
    /// The rate the frames were really recorded at, for a high speed camera clip stored at a slower playback rate. When set, frame n gets the
    /// timestamp n / RecordedFps and the file's own timestamps are ignored.
    /// </summary>
    public double? RecordedFps { get; init; }

    /// <summary>The absolute path of the imported video file or image folder (<see cref="MediaSource.InputPath"/>); null for devices and streams.</summary>
    public string? InputPath { get; init; }

    /// <summary>
    /// EXPERIMENTAL camera capture: rectify and store only the rig's marker zones, stacked top to bottom in scanout order (replaces Roi and
    /// Scale).
    /// </summary>
    public CameraRig? Camera { get; init; }

    /// <summary>Extra arguments inserted before '-i' (advanced device options).</summary>
    public IReadOnlyList<string> ExtraInputArguments { get; init; } = Array.Empty<string>();
  }
}
