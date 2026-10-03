//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The question a playback export asks before it makes a playable copy of a recording browsers cannot play (PlaybackExport): what it found
//* and what each answer does, worded once for the command line's prompt and the GUI's dialog.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Globalization;
using System.IO;
using MB.FramePacing.Capture.Ffmpeg;

namespace MB.FramePacing.Charts.Playback
{
  /// <param name="Source">The recording.</param>
  /// <param name="SourceSize">Its size in bytes.</param>
  /// <param name="Codec">What ffmpeg found in it.</param>
  /// <param name="Target">Where a "yes" writes the playable copy.</param>
  public sealed record PlaybackQuestion(string Source, long SourceSize, VideoCodecInfo Codec, string Target)
  {
    /// <summary>The question's title.</summary>
    public string Title => "Make a playable copy of the recording?";

    /// <summary>What was found and what the answers do.</summary>
    public string Text =>
      $"{Path.GetFileName(Source)} ({Size(SourceSize)}): {Codec.Problem}. "
      + (
        Codec.RemuxIsEnough
          ? $"ffmpeg can copy its video into {Target} without encoding it again (quick, every frame as it is)."
          : $"ffmpeg can encode a copy browsers play at {Target}, with every frame and its timestamp kept (this takes a while)."
      )
      + " Without it the report has no video, and the page asks the viewer to open a file of the recording.";

    /// <summary>The answer that makes the copy.</summary>
    public string Yes => "Make a playable copy";

    /// <summary>The answer that does not.</summary>
    public string No => "No video";

    /// <summary>The command line option that answers the question in advance.</summary>
    public string Option => "--playback-transcode yes|no";

    /// <summary>The configuration setting that answers it for good.</summary>
    public string Setting => "playbackTranscode";

    /// <summary>A size in bytes as people read it: "1.8 GB".</summary>
    public static string Size(long bytes)
    {
      string[] units = { "bytes", "KB", "MB", "GB", "TB" };
      double value = bytes;
      int unit = 0;
      while (value >= 1000 && unit < units.Length - 1)
      {
        value /= 1000;
        ++unit;
      }
      return unit == 0 ? $"{bytes} bytes" : value.ToString(value < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) + " " + units[unit];
    }
  }
}
