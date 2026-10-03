//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a playback export does: which recording (the one capture.json names, unless given), what to do with it (the answers given in
//* advance: an option for one run, else the configuration, else ask), the ffmpeg that probes and copies it, the section and the report's items.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using MB.FramePacing.Capture;

namespace MB.FramePacing.Charts.Playback
{
  public sealed record PlaybackExportOptions
  {
    /// <summary>The ffmpeg that describes the recording and makes copies of it.</summary>
    public required string FfmpegPath { get; init; }

    /// <summary>The recording, instead of the one capture.json names (null: that one).</summary>
    public string? VideoPath { get; init; }

    /// <summary>A recording browsers cannot play: ask, make a playable copy, or no video. One they play is always copied.</summary>
    public PlaybackTranscodeChoice TranscodeChoice { get; init; }

    /// <summary>Where the section starts, in seconds since the run's first frame (null with <see cref="ToSeconds"/> null: the whole run).</summary>
    public double? FromSeconds { get; init; }

    /// <summary>Where the section ends.</summary>
    public double? ToSeconds { get; init; }

    /// <summary>The report's items.</summary>
    public ReportOptions Report { get; init; } = ReportOptions.Default;

    /// <summary>The tools' version, shown at the bottom of the page.</summary>
    public string ToolVersion { get; init; } = string.Empty;

    /// <summary>The answer in advance: <paramref name="transcode"/> (an option for one run) wins over the configuration's, else ask.</summary>
    public static PlaybackTranscodeChoice Choice(PlaybackTranscodeChoice? transcode, FramePacingConfig config) =>
      transcode ?? config.PlaybackTranscode ?? PlaybackTranscodeChoice.Ask;
  }
}
