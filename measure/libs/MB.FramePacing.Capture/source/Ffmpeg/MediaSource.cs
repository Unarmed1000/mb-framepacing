//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A classified import input, ready for FfmpegCaptureSource: the device, the requested mode and known frame times.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System.Collections.Generic;

namespace MB.FramePacing.Capture.Ffmpeg
{
  /// <param name="Mode">Only carries the nominal frame rate of an image sequence.</param>
  /// <param name="FrameTimestamps">Exact frame times for an image sequence (see <see cref="FfmpegCaptureOptions.FrameTimestamps"/>).</param>
  /// <param name="RecordedFps">The real recording rate of a slow motion video file (see <see cref="FfmpegCaptureOptions.RecordedFps"/>).</param>
  public sealed record MediaSource(CaptureDevice Device, RequestedMode Mode, IReadOnlyList<long>? FrameTimestamps, double? RecordedFps = null)
  {
    public FfmpegCaptureOptions ToCaptureOptions(string ffmpegPath) =>
      new FfmpegCaptureOptions
      {
        FfmpegPath = ffmpegPath,
        Device = Device,
        Mode = Mode,
        FrameTimestamps = FrameTimestamps,
        RecordedFps = RecordedFps,
      };
  }
}
