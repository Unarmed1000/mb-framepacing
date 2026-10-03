//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The video a playback folder's pages play, and what it was made from: playback.json in the folder. It keeps the answer to the question
//* asked about the recording (copy or link, transcode or not) for as long as the recording is unchanged (same path, size and modification
//* time), so a later page of the same capture (a section) asks nothing. Written through a temporary file and a rename.
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
  /// <param name="Kind">The recording linked, copied into the folder, or a playable copy of it in the folder.</param>
  /// <param name="VideoFile">The folder's own video ("video.mp4"); null when the recording is linked.</param>
  /// <param name="Source">The recording's absolute path.</param>
  /// <param name="SourceSize">Its size in bytes when the video was made.</param>
  /// <param name="SourceModifiedUtc">Its modification time then.</param>
  /// <param name="SourcePlayable">Browsers can play the recording as it is.</param>
  /// <param name="Description">The recording's video stream ("h264 (High), yuv420p, mp4").</param>
  /// <param name="Problem">Why browsers cannot play the recording, null when they can.</param>
  public sealed record PlaybackVideo(
    PlaybackVideoKind Kind,
    string? VideoFile,
    string Source,
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

    /// <summary>The pages can play what they link: a copy, or a recording browsers play.</summary>
    [JsonIgnore]
    public bool Playable => Kind != PlaybackVideoKind.Linked || SourcePlayable;

    /// <summary>The file the pages in <paramref name="directory"/> play.</summary>
    public string PathIn(string directory) => VideoFile != null ? Path.Combine(directory, VideoFile) : Source;

    /// <summary>The video is there: the linked recording, or the folder's own file.</summary>
    public bool ExistsIn(string directory) => File.Exists(PathIn(directory));

    /// <summary>The recording at <paramref name="source"/> is the one this video was made from: same path, size and modification time.</summary>
    public bool IsFrom(FileInfo source) =>
      string.Equals(
        Path.GetFullPath(Source),
        source.FullName,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
      )
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
        return video is { FormatVersion: CurrentFormatVersion } && !string.IsNullOrEmpty(video.Source) ? video : null;
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
