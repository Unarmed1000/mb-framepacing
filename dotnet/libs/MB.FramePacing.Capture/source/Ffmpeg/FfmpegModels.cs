//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Devices, modes and capture options for the ffmpeg backend.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Capture.Ffmpeg
{
  /// <summary>The ffmpeg input device family.</summary>
  public enum FfmpegInputKind
  {
    /// <summary>Windows DirectShow ('-f dshow').</summary>
    DirectShow,

    /// <summary>Linux Video4Linux2 ('-f v4l2').</summary>
    Video4Linux2,

    /// <summary>macOS AVFoundation ('-f avfoundation').</summary>
    AVFoundation,

    /// <summary>ffmpeg's built in test sources ('-f lavfi'), for trying the pipeline without hardware.</summary>
    Lavfi,

    /// <summary>Anything ffmpeg opens with a plain '-i': a video file or a stream URL (rtsp://, srt://, udp://, http://...).</summary>
    Media,

    /// <summary>A folder of images, played through an ffconcat list with per-frame durations (see <see cref="ImageSequence"/>).</summary>
    ImageSequence,
  }

  /// <param name="Input">What is passed to '-i': a DirectShow name, a /dev/video path, an AVFoundation index or a lavfi graph.</param>
  /// <param name="Name">Human readable name.</param>
  public sealed record CaptureDevice(FfmpegInputKind Kind, string Input, string Name)
  {
    /// <summary>Capture cards and network streams deliver in real time; files and image sequences can be read at any pace.</summary>
    public bool IsLive =>
      Kind switch
      {
        FfmpegInputKind.ImageSequence => false,
        FfmpegInputKind.Media => !File.Exists(Input),
        _ => true,
      };

    public static FfmpegInputKind PlatformKind =>
      RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? FfmpegInputKind.DirectShow
      : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? FfmpegInputKind.AVFoundation
      : FfmpegInputKind.Video4Linux2;

    /// <summary>
    /// Parse a user supplied device: "lavfi:&lt;graph&gt;" for a test source, otherwise a device of the platform family. DirectShow accepts the
    /// device name (with or without the "video=" prefix), v4l2 a /dev/video path or number, AVFoundation the device index.
    /// </summary>
    public static CaptureDevice Parse(string text)
    {
      if (text.StartsWith("lavfi:", StringComparison.OrdinalIgnoreCase))
        return new CaptureDevice(FfmpegInputKind.Lavfi, text.Substring(6), "lavfi " + text.Substring(6));
      switch (PlatformKind)
      {
        case FfmpegInputKind.DirectShow:
          var name = text.StartsWith("video=", StringComparison.OrdinalIgnoreCase) ? text.Substring(6) : text;
          return new CaptureDevice(FfmpegInputKind.DirectShow, name, name);
        case FfmpegInputKind.Video4Linux2:
          var path = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? $"/dev/video{number}" : text;
          return new CaptureDevice(FfmpegInputKind.Video4Linux2, path, path);
        default:
          return new CaptureDevice(FfmpegInputKind.AVFoundation, text, "avfoundation " + text);
      }
    }
  }

  /// <summary>A mode a device offers. Fps is the maximum for the size (0 if the platform does not report it).</summary>
  /// <param name="Format">Pixel format or codec, e.g. "yuyv422", "nv12" or "mjpeg".</param>
  public sealed record CaptureMode(int Width, int Height, double Fps, string Format, bool IsCompressed)
  {
    public override string ToString() =>
      Fps > 0 ? string.Create(CultureInfo.InvariantCulture, $"{Width}x{Height}@{Fps:0.###} {Format}") : $"{Width}x{Height} {Format}";
  }

  /// <summary>A requested mode: "1920x1080@240", "1920x1080" or "@120".</summary>
  public readonly record struct RequestedMode(int Width, int Height, double Fps)
  {
    public bool HasSize => Width > 0 && Height > 0;
    public bool HasFps => Fps > 0;

    public static RequestedMode Parse(string text)
    {
      int at = text.IndexOf('@');
      string size = at >= 0 ? text.Substring(0, at) : text;
      string fps = at >= 0 ? text.Substring(at + 1) : string.Empty;
      int width = 0;
      int height = 0;
      if (size.Length > 0)
        (width, height) = ParseSize(size, text);
      double rate = 0;
      if (fps.Length > 0 && (!double.TryParse(fps, NumberStyles.Float, CultureInfo.InvariantCulture, out rate) || rate <= 0))
        throw new FormatException($"Invalid frame rate in mode '{text}'");
      return new RequestedMode(width, height, rate);
    }

    public static (int Width, int Height) ParseSize(string size, string context)
    {
      var parts = size.ToLowerInvariant().Split('x');
      if (
        parts.Length != 2
        || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int width)
        || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int height)
        || width <= 0
        || height <= 0
      )
        throw new FormatException($"Expected a size as WIDTHxHEIGHT in '{context}'");
      return (width, height);
    }
  }

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
