//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Options for importing an image sequence: its frame rate or a timestamp file (videos and streams carry their own timestamps).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FramePacing.Capture.Ffmpeg
{
  public sealed record MediaInputOptions
  {
    /// <summary>Frame rate of an image sequence (ignored for videos and streams, which carry their own timestamps).</summary>
    public double? Fps { get; init; }

    /// <summary>
    /// The rate a video file was really recorded at, for high speed camera clips stored at a slower playback rate (frame n is at
    /// n / RecordedFps; the file's timestamps are ignored). Ignored for image sequences and streams.
    /// </summary>
    public double? RecordedFps { get; init; }

    /// <summary>Per-image capture times for an image sequence (see <see cref="ImageSequence"/>).</summary>
    public string? TimestampFile { get; init; }
  }
}
