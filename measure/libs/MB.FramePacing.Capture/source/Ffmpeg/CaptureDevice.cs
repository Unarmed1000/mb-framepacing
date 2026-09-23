//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A source ffmpeg can open: a capture card, a video file, an image sequence, a network stream or an ffmpeg test source.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace MB.FramePacing.Capture.Ffmpeg
{
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
}
