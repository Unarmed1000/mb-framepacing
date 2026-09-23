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

    /// <summary>Per-image capture times for an image sequence (see <see cref="ImageSequence"/>).</summary>
    public string? TimestampFile { get; init; }
  }
}
