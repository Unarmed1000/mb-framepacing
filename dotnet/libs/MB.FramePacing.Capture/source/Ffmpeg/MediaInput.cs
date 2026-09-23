//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Turns "something to analyse that is not a capture card" into an ffmpeg capture source:
//*   - an existing file       -> a video file (any container/codec ffmpeg reads: mp4, mkv, mov, avi, ...)
//*   - an existing folder     -> an image sequence (needs a frame rate or a timestamp file)
//*   - anything else          -> a stream URL (rtsp://, srt://, udp://, http(s)://, ...), handled live
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MB.FramePacing.Capture.Ffmpeg
{
  public enum MediaInputKind
  {
    VideoFile,
    ImageSequence,
    Stream,
  }

  public sealed record MediaInputOptions
  {
    /// <summary>Frame rate of an image sequence (ignored for videos and streams, which carry their own timestamps).</summary>
    public double? Fps { get; init; }

    /// <summary>Per-image capture times for an image sequence (see <see cref="ImageSequence"/>).</summary>
    public string? TimestampFile { get; init; }
  }

  /// <param name="Mode">Only carries the nominal frame rate of an image sequence.</param>
  /// <param name="FrameTimestamps">Exact frame times for an image sequence (see <see cref="FfmpegCaptureOptions.FrameTimestamps"/>).</param>
  public sealed record MediaSource(CaptureDevice Device, RequestedMode Mode, IReadOnlyList<long>? FrameTimestamps)
  {
    public FfmpegCaptureOptions ToCaptureOptions(string ffmpegPath) =>
      new FfmpegCaptureOptions
      {
        FfmpegPath = ffmpegPath,
        Device = Device,
        Mode = Mode,
        FrameTimestamps = FrameTimestamps,
      };
  }

  public static class MediaInput
  {
    public static MediaInputKind Classify(string input) =>
      File.Exists(input) ? MediaInputKind.VideoFile
      : Directory.Exists(input) ? MediaInputKind.ImageSequence
      : MediaInputKind.Stream;

    /// <summary>
    /// Build the ffmpeg input for <paramref name="input"/>. An image sequence writes its ffconcat list into <paramref name="workDirectory"/>
    /// (normally the capture folder, so the list stays with the capture).
    /// </summary>
    public static MediaSource Create(string input, MediaInputOptions options, string workDirectory)
    {
      switch (Classify(input))
      {
        case MediaInputKind.VideoFile:
          var file = Path.GetFullPath(input);
          return new MediaSource(new CaptureDevice(FfmpegInputKind.Media, file, Path.GetFileName(file)), default, null);

        case MediaInputKind.ImageSequence:
          var folder = Path.GetFullPath(input);
          var frames = ImageSequence.Collect(folder, options.Fps, options.TimestampFile);
          Directory.CreateDirectory(workDirectory);
          var list = Path.Combine(workDirectory, "images.ffconcat");
          double fps = ImageSequence.WriteConcatList(frames, list);
          // The mode only carries the nominal rate for capture.json; the list's durations drive the timestamps
          return new MediaSource(
            new CaptureDevice(FfmpegInputKind.ImageSequence, list, $"{frames.Count} images in {folder}"),
            new RequestedMode(0, 0, fps),
            frames.Select(frame => frame.TimeTicks).ToList()
          );

        default:
          if (!input.Contains("://", StringComparison.Ordinal))
            throw new FileNotFoundException($"'{input}' is not an existing file or folder, and not a stream URL (like rtsp://host/stream)");
          return new MediaSource(new CaptureDevice(FfmpegInputKind.Media, input, input), default, null);
      }
    }
  }
}
