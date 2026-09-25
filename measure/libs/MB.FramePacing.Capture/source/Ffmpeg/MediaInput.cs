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
using System.IO;
using System.Linq;

namespace MB.FramePacing.Capture.Ffmpeg
{
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
          if (options.RecordedFps is <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "The recorded frame rate must be positive");
          return new MediaSource(new CaptureDevice(FfmpegInputKind.Media, file, Path.GetFileName(file)), default, null, options.RecordedFps);

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
