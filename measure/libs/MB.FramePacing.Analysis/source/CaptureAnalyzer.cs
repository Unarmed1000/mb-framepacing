//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Analyse a capture directory (captures.mbcd, the capture data, + capture.json) and write the reports to <capture>/analysis. A capture that
//* only has its frames (frames.mbfc) is decoded into captures.mbcd first, so the next analysis starts from the data:
//*   captures.csv                - one row per capture index
//*   run-<id>[-<n>]-frames.csv   - one row per presented application frame
//*   summary.json                - everything else (layout, counts, statistics, histograms, warnings)
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using MB.FramePacing.Capture;
using MB.FramePacing.Data;

namespace MB.FramePacing.Analysis
{
  public static class CaptureAnalyzer
  {
    public const string AnalysisDirectoryName = AnalysisFiles.DirectoryName;
    public const string SummaryFileName = AnalysisFiles.SummaryFileName;
    public const string CapturesFileName = AnalysisFiles.CapturesFileName;
    public const double MovedMarkerUndecodableFraction = 0.05;

    /// <summary>A camera capture always loses a few captures per frame to the scanout crossing the marker; more than this is a problem.</summary>
    public const double CameraUndecodableFraction = 0.2;

    public static AnalysisReport Analyze(
      string captureDirectory,
      AnalysisOptions options,
      IProgress<double>? progress = null,
      CancellationToken cancellationToken = default
    )
    {
      var session = CaptureSessionInfo.TryLoad(captureDirectory);
      var scanout = session?.Camera != null ? ScanoutModel.Camera : ScanoutModel.SingleScanout;
      var (dataHeader, records) = LoadData(captureDirectory, scanout == ScanoutModel.Camera, options.Redecode, progress, cancellationToken);
      var capture = CaptureDecoder.FromData(dataHeader, records, options.TimeSource);

      var timeline = TimelineAnalyzer.Analyze(
        capture.Rows,
        options.Timeline with
        {
          Scanout = scanout,
          TargetFps = options.Timeline.TargetFps ?? session?.TargetFps,
          ExpectedRefreshHz = options.Timeline.ExpectedRefreshHz ?? session?.ExpectedRefreshHz,
          CalibratedRefreshHz = session?.Camera?.RefreshHz,
        }
      );
      // The name the user gave: this analysis's, else the capture's
      if ((options.Name ?? session?.Name) is { } name)
        timeline = timeline with { Runs = timeline.Runs.Select(r => r with { Name = name }).ToList() };
      var warnings = new List<string>(capture.Layout.Warnings);
      warnings.AddRange(timeline.Warnings);
      if (MarkerMayHaveMoved(capture))
        warnings.Add(
          $"Many captures could not be decoded and only the region {capture.Header.Roi} was stored: the marker may have moved out of it. "
            + "Keep the marker at a fixed position, or locate it again ('locate', --roi auto)."
        );
      if (scanout == ScanoutModel.Camera && UndecodableFraction(capture) is var fraction && fraction > CameraUndecodableFraction)
        warnings.Add(
          $"Camera capture: {fraction:P0} of the captures could not be decoded. Check focus, exposure and flicker ('camera-rig verify'), and "
            + "that the camera films well above the refresh rate."
        );
      if (capture.TimeSource == TimeSource.Host)
        warnings.Add("Host timestamps are used (the capture has no device timestamps): expect extra jitter from process scheduling.");
      if (session is { FramesDroppedByRecorder: > 0 })
        warnings.Add(
          $"The recorder dropped {session.FramesDroppedByRecorder} frames (decoding or, with stored frames, the disk too slow?); they appear as "
            + "NotRecorded captures."
        );
      if (session is { FramesDroppedBySource: > 0 })
        warnings.Add($"The capture device/ffmpeg reported {session.FramesDroppedBySource} dropped frames.");

      var outputDirectory = options.OutputDirectory ?? Path.Combine(captureDirectory, AnalysisDirectoryName);
      Directory.CreateDirectory(outputDirectory);
      var report = new AnalysisReport(captureDirectory, outputDirectory, session, capture, timeline, warnings);
      WriteReports(report, options);
      return report;
    }

    /// <summary>
    /// The capture data of <paramref name="captureDirectory"/>: captures.mbcd, or its frames.mbfc decoded (and the result saved as captures.mbcd,
    /// so the next analysis starts from it). <paramref name="redecode"/> decodes the frames again even when the data exists.
    /// </summary>
    public static (CaptureDataHeader Header, CaptureDataRecord[] Records) LoadData(
      string captureDirectory,
      bool camera,
      bool redecode = false,
      IProgress<double>? progress = null,
      CancellationToken cancellationToken = default
    )
    {
      var dataPath = Path.Combine(captureDirectory, CaptureSessionInfo.DataFileName);
      var framesPath = Path.Combine(captureDirectory, CaptureSessionInfo.FramesFileName);
      bool hasFrames = File.Exists(framesPath);
      if (File.Exists(dataPath) && !(redecode && hasFrames))
      {
        using var reader = new CaptureDataReader(dataPath);
        progress?.Report(1.0);
        return (reader.Header, reader.ReadAll());
      }
      if (!hasFrames)
        throw new FileNotFoundException(
          $"'{captureDirectory}' does not contain a capture ({CaptureSessionInfo.DataFileName} or {CaptureSessionInfo.FramesFileName})",
          dataPath
        );

      (CaptureDataHeader Header, CaptureDataRecord[] Records) data;
      using (var frames = new CaptureFileReader(framesPath))
        data = CaptureDecoder.DecodeFrames(frames, camera, progress, cancellationToken);
      WriteData(dataPath, data.Header, data.Records);
      return data;
    }

    /// <summary>Write captures.mbcd next to the frames it was decoded from (to a temporary file first, then over the old one).</summary>
    private static void WriteData(string path, CaptureDataHeader header, IReadOnlyList<CaptureDataRecord> records)
    {
      string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
      try
      {
        using (var writer = new CaptureDataWriter(temporary, header))
          writer.WriteRecords(records);
        File.Move(temporary, path, overwrite: true);
      }
      finally
      {
        if (File.Exists(temporary))
          File.Delete(temporary);
      }
    }

    /// <summary>
    /// A capture that stored only a region relies on the marker staying inside it: more than <see cref="MovedMarkerUndecodableFraction"/>
    /// undecodable captures hint that it did not.
    /// </summary>
    private static bool MarkerMayHaveMoved(DecodedCapture capture)
    {
      if (capture.Header.Roi.IsEmpty)
        return false;
      int recorded = capture.Rows.Count(r => r.Status != CaptureStatus.NotRecorded);
      int undecodable = capture.Rows.Count(r => r.Status == CaptureStatus.Undecodable);
      return recorded > 0 && undecodable > recorded * MovedMarkerUndecodableFraction;
    }

    private static double UndecodableFraction(DecodedCapture capture)
    {
      int recorded = capture.Rows.Count(r => r.Status != CaptureStatus.NotRecorded);
      return recorded > 0 ? capture.Rows.Count(r => r.Status == CaptureStatus.Undecodable) / (double)recorded : 0;
    }

    /// <summary>
    /// The start of every report file of a run: "run-{id}", and "run-{id}-{n}" for the n-th run with the same id (<paramref name="ordinal"/>
    /// counts from 0 among the runs with that id).
    /// </summary>
    public static string RunFilePrefix(RunAnalysis run, int ordinal) => AnalysisFiles.RunFilePrefix(run.RunId, ordinal);

    public static string RunFramesFileName(RunAnalysis run, int ordinal) => AnalysisFiles.FramesFileName(run.RunId, ordinal);

    private static void WriteReports(AnalysisReport report, AnalysisOptions options)
    {
      bool camera = report.Session?.Camera != null;
      WriteCaptures(Path.Combine(report.OutputDirectory, CapturesFileName), report.Capture.Rows);
      var ordinals = new Dictionary<uint, int>();
      var runFiles = new List<string>();
      foreach (var run in report.Timeline.Runs)
      {
        int ordinal = ordinals.TryGetValue(run.RunId, out int seen) ? seen : 0;
        ordinals[run.RunId] = ordinal + 1;
        var name = RunFramesFileName(run, ordinal);
        runFiles.Add(name);
        WriteFrames(Path.Combine(report.OutputDirectory, name), run.Frames, camera);
      }
      WriteSummary(Path.Combine(report.OutputDirectory, SummaryFileName), report, options, runFiles);
    }

    private static void WriteCaptures(string path, IReadOnlyList<CaptureRow> rows) => CapturesCsv.Write(path, rows.Select(r => r.ToCsvRow()));

    private static void WriteFrames(string path, IReadOnlyList<PresentedFrame> frames, bool camera) =>
      FramesCsv.Write(path, frames.Select(f => f.ToRow()), camera);

    private static void WriteSummary(string path, AnalysisReport report, AnalysisOptions options, List<string> runFiles)
    {
      bool camera = report.Session?.Camera != null;
      new AnalysisSummary(
        AnalysisSummary.CurrentFormatVersion,
        options.ToolVersion,
        camera ? "Camera capture: " + Capture.Camera.CameraRig.ExperimentalNotice : null,
        (camera ? ScanoutModel.Camera : ScanoutModel.SingleScanout).ToString(),
        DateTime.UtcNow,
        Path.GetFullPath(report.CaptureDirectory),
        report.Session != null ? JsonSerializer.SerializeToElement(report.Session, AnalysisSummary.JsonOptions) : null,
        $"{report.Capture.Header.Width}x{report.Capture.Header.Height}",
        report.Capture.TimeSource.ToString(),
        report.CapturePeriodMs,
        report.CapturePeriodMs,
        report.ErrorThresholdMs,
        report.Capture.Layout.Locks.Select(l => new SummaryMarker(l.Bounds.ToString(), l.ModuleSizePx)).ToList(),
        report.Warnings,
        report.Timeline.Runs.Select((run, i) => run.ToSummary(runFiles[i])).ToList()
      ).Write(path);
    }
  }
}
