//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Everything FfmpegCaptureSource needs to start ffmpeg: the executable, the device and mode, crop and scale, and known frame times of image
//* sequences.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using MB.FramePacing.Marker;

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

    /// <summary>DirectShow real-time buffer (absorbs short hiccups before ffmpeg drops frames).</summary>
    public int RealTimeBufferMegabytes { get; init; } = 1024;

    /// <summary>
    /// Known capture times (TimeSpan ticks) of the frames, in order. When set they replace ffmpeg's timestamps and the capture ends after the
    /// last one. Used for image sequences, where the times come from --fps or a timestamp file and ffmpeg's own are too coarse.
    /// </summary>
    public IReadOnlyList<long>? FrameTimestamps { get; init; }

    /// <summary>Extra arguments inserted before '-i' (advanced device options).</summary>
    public IReadOnlyList<string> ExtraInputArguments { get; init; } = Array.Empty<string>();
  }
}
