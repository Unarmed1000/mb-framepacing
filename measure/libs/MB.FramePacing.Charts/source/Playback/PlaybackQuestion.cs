//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A question the playback page asks before it writes a video (PlaybackExport): what it found about the recording and what each answer
//* does, worded once for the command line's prompt and the GUI's dialog.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Globalization;
using System.IO;
using MB.FramePacing.Capture.Ffmpeg;

namespace MB.FramePacing.Charts.Playback
{
  /// <param name="Kind">Copy or link a playable recording, or make a playable copy of one browsers cannot play.</param>
  /// <param name="Source">The recording.</param>
  /// <param name="SourceSize">Its size in bytes.</param>
  /// <param name="Codec">What ffmpeg found in it.</param>
  /// <param name="Target">Where a "yes" writes the video.</param>
  public sealed record PlaybackQuestion(PlaybackQuestionKind Kind, string Source, long SourceSize, VideoCodecInfo Codec, string Target)
  {
    /// <summary>The question's title.</summary>
    public string Title =>
      Kind == PlaybackQuestionKind.CopyOrLink ? "Copy the recording into the playback folder?" : "Make a playable copy of the recording?";

    /// <summary>What was found and what the answers do.</summary>
    public string Text =>
      Kind == PlaybackQuestionKind.CopyOrLink
        ? $"{Path.GetFileName(Source)} ({Size(SourceSize)}, {Codec.Description}) plays in web browsers. Copy it into {Path.GetDirectoryName(Target)}, so "
          + "the folder plays anywhere on its own, or link it where it is (no extra disk space, but the folder needs the recording)?"
        : $"{Path.GetFileName(Source)} ({Size(SourceSize)}): {Codec.Problem}. "
          + (
            Codec.RemuxIsEnough
              ? $"ffmpeg can copy its video into {Target} without encoding it again (quick, every frame as it is)."
              : $"ffmpeg can encode a copy browsers play at {Target}, with every frame and its timestamp kept (this takes a while)."
          )
          + " Without it the page links the recording, and the browser shows a message instead of the video.";

    /// <summary>The answer that writes the video.</summary>
    public string Yes => Kind == PlaybackQuestionKind.CopyOrLink ? "Copy into the folder" : "Make a playable copy";

    /// <summary>The answer that links the recording.</summary>
    public string No => Kind == PlaybackQuestionKind.CopyOrLink ? "Link the recording" : "Link it as it is";

    /// <summary>The command line option that answers the question in advance, with the value for each answer.</summary>
    public string Option => Kind == PlaybackQuestionKind.CopyOrLink ? "--playback-video copy|link" : "--playback-transcode yes|no";

    /// <summary>The configuration setting that answers it for good.</summary>
    public string Setting => Kind == PlaybackQuestionKind.CopyOrLink ? "playbackVideo" : "playbackTranscode";

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
