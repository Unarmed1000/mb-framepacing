//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A video file's first video stream as ffmpeg describes it (container, codec, profile, pixel format, frame rate, duration), and whether web
//* browsers can play it: H.264 (8-bit 4:2:0), VP9 or AV1 in MP4 or MOV, and VP8, VP9 or AV1 in WebM. HEVC, lossless and 4:4:4 recordings,
//* and containers like Matroska, are not played by every browser; a copy is playable after a remux (the container only) or a transcode.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MB.FramePacing.Capture.Ffmpeg
{
  /// <param name="Extension">The file's extension, lower case without the dot ("mp4").</param>
  /// <param name="Container">ffmpeg's demuxer names ("mov,mp4,m4a,3gp,3g2,mj2", "matroska,webm").</param>
  /// <param name="Codec">The video codec ("h264", "hevc", "utvideo").</param>
  /// <param name="Profile">The codec's profile when ffmpeg names one ("High", "High 4:4:4 Predictive"), else empty.</param>
  /// <param name="PixelFormat">The pixel format ("yuv420p"), else empty.</param>
  /// <param name="Fps">The stream's frame rate when ffmpeg names it.</param>
  /// <param name="Duration">The file's duration when ffmpeg names it.</param>
  public sealed record VideoCodecInfo(
    string Extension,
    string Container,
    string Codec,
    string Profile,
    string PixelFormat,
    double? Fps,
    TimeSpan? Duration
  )
  {
    private static readonly HashSet<string> g_mp4Extensions = new HashSet<string>(StringComparer.Ordinal) { "mp4", "m4v", "mov" };

    // Every browser decodes 8-bit 4:2:0; 10-bit, 4:2:2 and 4:4:4 H.264 not reliably
    private static readonly HashSet<string> g_420 = new HashSet<string>(StringComparer.Ordinal) { "yuv420p", "yuvj420p", "nv12" };

    /// <summary>A browser can decode the stream (in a container it reads).</summary>
    public bool CodecPlayable =>
      Codec switch
      {
        "h264" => g_420.Contains(PixelFormat)
          && !Profile.Contains("4:4:4", StringComparison.Ordinal)
          && !Profile.Contains("4:2:2", StringComparison.Ordinal)
          && !Profile.Contains("10", StringComparison.Ordinal),
        "vp8" or "vp9" => g_420.Contains(PixelFormat),
        "av1" => g_420.Contains(PixelFormat) || PixelFormat == "yuv420p10le",
        _ => false,
      };

    /// <summary>The container is one browsers read, and holds this codec.</summary>
    public bool ContainerPlayable =>
      (g_mp4Extensions.Contains(Extension) && Container.Contains("mp4", StringComparison.Ordinal) && Codec is "h264" or "vp9" or "av1")
      || (Extension == "webm" && Container.Contains("webm", StringComparison.Ordinal) && Codec is "vp8" or "vp9" or "av1");

    /// <summary>Web browsers can play the file as it is.</summary>
    public bool Playable => CodecPlayable && ContainerPlayable;

    /// <summary>The stream plays in a browser once it is in an MP4 file: copying it into one (a remux) makes it playable without a re-encode.</summary>
    public bool RemuxIsEnough => CodecPlayable && Codec is "h264" or "vp9" or "av1";

    /// <summary>The stream in a few words: "h264 (High 4:4:4 Predictive), yuv444p, mkv".</summary>
    public string Description =>
      string.Join(", ", new[] { Profile.Length > 0 ? $"{Codec} ({Profile})" : Codec, PixelFormat, Extension }.Where(part => part.Length > 0));

    /// <summary>Why browsers cannot play it, or null when they can.</summary>
    public string? Problem =>
      Playable ? null
      : RemuxIsEnough ? $"browsers do not read .{Extension} files, though they can play the video inside ({Description})"
      : $"browsers cannot play {Description}";

    /// <summary>The frames per keyframe of a playable copy: about one a second, so stepping back a frame decodes at most a second of video.</summary>
    public int KeyframeInterval => Math.Clamp((int)Math.Round(Fps ?? 60), 1, 600);

    /// <summary>The extension of <paramref name="path"/> as <see cref="Extension"/> has it.</summary>
    public static string ExtensionOf(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

    /// <summary>"hh:mm:ss.ff" (ffmpeg's Duration line) as a time; null when it is not one.</summary>
    public static TimeSpan? ParseDuration(string text) =>
      TimeSpan.TryParseExact(text, @"hh\:mm\:ss\.FFFFFFF", CultureInfo.InvariantCulture, out var duration) ? duration : null;
  }
}
