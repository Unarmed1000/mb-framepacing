//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The video a playback report holds, and what it was made from: playback.json in the report's folder. The folder is complete in itself and
//* names nothing outside it: of the recording it keeps only the file name, size and modification time, by which saving the same report
//* again recognises an unchanged recording and uses the video again without a question. Written through a temporary file and a rename.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MB.FramePacing.Charts.Playback
{
  /// <param name="Kind">A copy of the recording, a playable copy of it, or no video.</param>
  /// <param name="VideoFile">The report's video in its folder ("video.mp4"); null without one.</param>
  /// <param name="SourceName">The recording's file name (no folder).</param>
  /// <param name="SourceSize">Its size in bytes when the video was made.</param>
  /// <param name="SourceModifiedUtc">Its modification time then.</param>
  /// <param name="SourcePlayable">Browsers can play the recording as it is.</param>
  /// <param name="Description">The recording's video stream ("h264 (High), yuv420p, mp4").</param>
  /// <param name="Problem">Why browsers cannot play the recording, null when they can.</param>
  public sealed record PlaybackVideo(
    PlaybackVideoKind Kind,
    string? VideoFile,
    string SourceName,
    long SourceSize,
    DateTime SourceModifiedUtc,
    bool SourcePlayable,
    string Description,
    string? Problem
  )
  {
    public const string FileName = "playback.json";

    /// <summary>The format this version writes and reads.</summary>
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    private static readonly JsonSerializerOptions g_options = new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>The report has a video it plays.</summary>
    [JsonIgnore]
    public bool Playable => VideoFile != null;

    /// <summary>The report's video in <paramref name="directory"/>, null without one.</summary>
    public string? PathIn(string directory) => VideoFile != null ? Path.Combine(directory, VideoFile) : null;

    /// <summary>The report's video is there (or it has none).</summary>
    public bool ExistsIn(string directory) => PathIn(directory) is not { } path || File.Exists(path);

    /// <summary>The recording <paramref name="source"/> is the one this video was made from: same file name, size and modification time.</summary>
    public bool IsFrom(FileInfo source) =>
      string.Equals(SourceName, source.Name, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
      && source.Length == SourceSize
      && source.LastWriteTimeUtc == SourceModifiedUtc;

    /// <summary>The playback.json in <paramref name="directory"/>; null when there is none, or it cannot be read (it is then made again).</summary>
    public static PlaybackVideo? Read(string directory)
    {
      string path = Path.Combine(directory, FileName);
      if (!File.Exists(path))
        return null;
      try
      {
        var video = JsonSerializer.Deserialize<PlaybackVideo>(File.ReadAllText(path), g_options);
        return video is { FormatVersion: CurrentFormatVersion } && !string.IsNullOrEmpty(video.SourceName) ? video : null;
      }
      catch (JsonException)
      {
        return null;
      }
    }

    /// <summary>Write playback.json into <paramref name="directory"/>: a temporary file renamed over it.</summary>
    public void Write(string directory)
    {
      string path = Path.Combine(directory, FileName);
      string temporary = path + ".tmp";
      File.WriteAllText(temporary, JsonSerializer.Serialize(this, g_options), new UTF8Encoding(false));
      File.Move(temporary, path, overwrite: true);
    }
  }
}
