//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What the playback page needs to know about an analysed capture: where its analysis is, the recording it was imported from, and whether
//* its times are the recording's own. The page finds a capture in the video by its time, so it needs a video file imported with the video's
//* timestamps: the device clock, not a camera rig, not timestamps made from a recorded frame rate.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.IO;
using System.Text.Json;
using MB.FramePacing.Analysis;
using MB.FramePacing.Data;

namespace MB.FramePacing.Charts.Playback
{
  /// <param name="AnalysisDirectory">The analysis output folder: the playback folder goes in it.</param>
  /// <param name="InputPath">The recording the capture was imported from (capture.json inputPath), null when it names none.</param>
  /// <param name="TimeSource">The clock the capture's times come from: "Device" (the video's timestamps for an import) or "Host".</param>
  /// <param name="RecordedFps">Set when the capture's times were made from a recorded frame rate instead of read from the video.</param>
  /// <param name="Camera">An EXPERIMENTAL camera capture: its frames are the rig's zones, not the video's.</param>
  public sealed record PlaybackCapture(string AnalysisDirectory, string? InputPath, string? TimeSource, double? RecordedFps, bool Camera)
  {
    /// <summary>The capture of a live analysis.</summary>
    public static PlaybackCapture From(AnalysisReport report) =>
      new PlaybackCapture(
        report.OutputDirectory,
        report.Session?.InputPath,
        report.Capture.TimeSource.ToString(),
        report.Session?.RecordedFps,
        report.Session?.Camera != null
      );

    /// <summary>The capture of the analysis in <paramref name="folder"/> (an analysis folder, or a capture folder that has one), from summary.json.</summary>
    public static PlaybackCapture Read(string folder)
    {
      string directory = AnalysisOutput.Directory(folder);
      var summary = AnalysisSummary.Read(Path.Combine(directory, AnalysisFiles.SummaryFileName));
      string? inputPath = null;
      double? recordedFps = null;
      bool camera = summary.Scanout == nameof(ScanoutModel.Camera);
      if (summary.Capture is { ValueKind: JsonValueKind.Object } capture)
      {
        if (capture.TryGetProperty("inputPath", out var input) && input.ValueKind == JsonValueKind.String)
          inputPath = input.GetString();
        if (capture.TryGetProperty("recordedFps", out var fps) && fps.ValueKind == JsonValueKind.Number)
          recordedFps = fps.GetDouble();
        camera |= capture.TryGetProperty("camera", out var rig) && rig.ValueKind == JsonValueKind.Object;
      }
      return new PlaybackCapture(directory, inputPath, summary.TimeSource, recordedFps, camera);
    }

    /// <summary>The playback folder: next to the analysis's files.</summary>
    public string PlaybackDirectory => Path.Combine(AnalysisDirectory, PlaybackFiles.DirectoryName);

    /// <summary>The capture names no recording (a capture card, a stream, or an import from before capture.json had inputPath).</summary>
    public bool NamesNoVideo => string.IsNullOrEmpty(InputPath);

    /// <summary>
    /// Why the playback page cannot show this capture with <paramref name="video"/> (null: the recording capture.json names), or null when it
    /// can. A missing recording is not a problem here: the page may still have a copy of it.
    /// </summary>
    public string? Problem(string? video = null)
    {
      if (Camera)
        return "The playback page needs a recording imported as a video file: this is a camera capture (very experimental), whose frames are the camera rig's zones.";
      if (RecordedFps != null)
        return "The capture's times were made from a recorded frame rate (--recorded-fps), not read from the video: the playback page could not find its frames in the video.";
      string? path = video ?? InputPath;
      if (string.IsNullOrEmpty(path))
        return "The capture names no video file it was imported from (a capture card, a stream, or an import made before capture.json named it): name the recording with --video <file>.";
      if (Directory.Exists(path))
        return "The capture was imported from a folder of images, not a video file: the playback page needs a video.";
      if (TimeSource != nameof(Analysis.TimeSource.Device))
        return $"The capture's times come from the computer's clock (time source {TimeSource ?? "unknown"}), not from the video: the playback page needs the video's own timestamps (analyse with --time Device).";
      return null;
    }
  }
}
