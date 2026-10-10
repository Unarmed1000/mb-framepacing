//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Analyse a capture directory (captures.mbcd, the capture data, + capture.json) and write the reports to <capture>/analysis. A capture that
//* only has its frames (frames.mbfc) is decoded into captures.mbcd first, so the next analysis starts from the data:
//*   captures.csv                - one row per capture index
//*   run-<id>[-<n>]-frames.csv   - one row per presented application frame
//*   summary.json                - everything else (layout, counts, statistics, histograms, warnings)
//* EXPERIMENTAL: with a Perfetto trace of the run (measure/doc/perfetto.md), also what the trace says of each presented frame:
//*   run-<id>[-<n>]-trace.csv    - one row per presented application frame (the tools' own file, not part of the analysis output format)
//*   run-<id>[-<n>]-trace-check.txt - how well the markers and the trace line up, as numbers
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
using MB.FramePacing.TraceJoin;

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
          $"Many captures could not be decoded and only the region {StoredRegions(capture.Header)} was stored: the marker may have moved out of it, or the "
            + "recording goes on after the application stopped drawing it. Keep the marker at a fixed position, or store the whole frame "
            + "(--roi full)."
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

      var (traceJoins, recordChecks) = JoinTrace(session, capture, timeline, options, warnings, cancellationToken);

      var outputDirectory = options.OutputDirectory ?? Path.Combine(captureDirectory, AnalysisDirectoryName);
      Directory.CreateDirectory(outputDirectory);
      var report = new AnalysisReport(captureDirectory, outputDirectory, session, capture, timeline, warnings)
      {
        TraceJoins = traceJoins,
        TraceRecordChecks = recordChecks,
      };
      WriteReports(report, options);
      return report;
    }

    /// <summary>
    /// EXPERIMENTAL: the Perfetto trace an analysis of <paramref name="captureDirectory"/> with <paramref name="options"/> joins the capture
    /// with: the options', else the one capture.json names; null when there is none or the options say to join none.
    /// </summary>
    public static TraceJoinOptions? TraceOf(string captureDirectory, AnalysisOptions options) =>
      TraceOf(CaptureSessionInfo.TryLoad(captureDirectory), options);

    private static TraceJoinOptions? TraceOf(CaptureSessionInfo? session, AnalysisOptions options) =>
      options.NoTrace ? null : options.Trace ?? session?.Trace;

    /// <summary>
    /// EXPERIMENTAL (measure/doc/perfetto.md): each run joined with the capture's Perfetto trace, by the frame record the application
    /// logged (sdk/doc/trace-frame-record.md), and its markers held against that record. With only a trace of the application's own
    /// nothing is joined, and the markers are held against its record. Each trace is read once. What does not fit is a warning and
    /// the run has no join, but for a trace the options themselves name: that one not being readable stops the analysis.
    /// </summary>
    private static (IReadOnlyList<TraceRunJoin?> Joins, IReadOnlyList<FrameRecordCheck?> RecordChecks) JoinTrace(
      CaptureSessionInfo? session,
      DecodedCapture capture,
      TimelineResult timeline,
      AnalysisOptions options,
      List<string> warnings,
      CancellationToken cancellationToken
    )
    {
      var none = (Array.Empty<TraceRunJoin?>(), Array.Empty<FrameRecordCheck?>());
      if (TraceOf(session, options) is not { } trace)
        return none;
      bool asked = options.Trace != null;
      bool hasTrace = trace.Path.Length > 0;
      if (!hasTrace && string.IsNullOrEmpty(trace.AppTracePath))
        return none;
      string traceName = Path.GetFileName(hasTrace ? trace.Path : trace.AppTracePath!);
      if (options.TraceProcessorPath is not { } processor)
      {
        warnings.Add(
          $"The Perfetto trace '{traceName}' was not read: Perfetto's trace processor was not found (experimental; see measure/doc/perfetto.md)."
        );
        return none;
      }

      TraceData? data = null;
      IReadOnlyList<FrameRecordSlice>? appRecords = null;
      try
      {
        // A trace to join is read with the frame record of the application's own trace, where there is one; alone, that record is all
        if (hasTrace)
          data = TraceLoader.Load(processor, trace, cancellationToken);
        else
          appRecords = TraceLoader.LoadAppRecords(processor, trace, cancellationToken);
      }
      catch (TraceException ex) when (!asked)
      {
        warnings.Add(
          hasTrace
            ? $"The Perfetto trace '{traceName}' was not joined: {ex.Message}"
            : $"The application's trace '{traceName}' was not read: {ex.Message}"
        );
        return none;
      }

      var captures = capture.Rows.Select(r => r.ToCsvRow()).ToList();
      var joins = new List<TraceRunJoin?>(timeline.Runs.Count);
      var records = new List<FrameRecordCheck?>(timeline.Runs.Count);
      foreach (var run in timeline.Runs)
      {
        var rows = run.Frames.Select(f => f.ToRow()).ToList();
        TraceRunJoin? join = null;
        if (data != null)
        {
          long periodNs = (run.Pacing?.RefreshPeriod ?? timeline.CapturePeriod).Nanoseconds;
          try
          {
            join = TraceJoiner.Join(TraceRun.Create(run.RunId, run.SequenceId, run.Name, rows, captures, periodNs), data, trace);
          }
          catch (TraceException ex)
          {
            warnings.Add($"Run {run.RunId}: the Perfetto trace '{traceName}' was not joined: {ex.Message}");
          }
        }
        joins.Add(join);
        records.Add(join?.LineUp?.Record ?? (appRecords != null ? FrameRecordCheck.Of(run.RunId, rows, appRecords) : null));
      }
      if (joins.Any(join => join != null))
        warnings.Add(TraceNotice);
      return (data != null ? joins : Array.Empty<TraceRunJoin?>(), records);
    }

    /// <summary>What an analysis that joined a Perfetto trace says of it: the feature is experimental, and what its times rest on.</summary>
    public const string TraceNotice =
      "A Perfetto trace was joined (experimental): its times and the capture's are tied by the frames only, so a delay the whole run shares "
      + "is not in what the trace says of a frame.";

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

    /// <summary>The region a capture stored, or its two: "14,14,330,330 and 14,832,234,234".</summary>
    private static string StoredRegions(CaptureFileHeader header) =>
      header.SyncRoi.IsEmpty ? header.Roi.ToString() : $"{header.Roi} and {header.SyncRoi}";

    /// <summary>
    /// A capture that stored only a region relies on the marker staying inside it: more than <see cref="MovedMarkerUndecodableFraction"/>
    /// undecodable captures hint that it did not.
    /// </summary>
    private static bool MarkerMayHaveMoved(DecodedCapture capture)
    {
      if (capture.Header.Roi.IsEmpty)
        return false;
      // From the first capture with a marker on: what a recording shows before the application draws one has no marker to lose. And up
      // to an end marker, when that is the last marker seen: what follows it is after the run. Captures without a marker at the end
      // of anything else do count: that is what a marker that moved out of the region looks like
      var rows = capture.Rows;
      int first = 0;
      while (first < rows.Count && !HasMarker(rows[first]))
        ++first;
      int last = rows.Count - 1;
      int lastMarker = last;
      while (lastMarker > first && !HasMarker(rows[lastMarker]))
        --lastMarker;
      if (
        lastMarker >= first
        && lastMarker < rows.Count
        && rows[lastMarker].IsDecoded
        && rows[lastMarker].Payload.Kind == MarkerDecoding.MarkerKind.SequenceEnd
      )
        last = lastMarker;
      int recorded = 0;
      int undecodable = 0;
      for (int i = first; i <= last; ++i)
      {
        if (rows[i].Status != CaptureStatus.NotRecorded)
          ++recorded;
        if (rows[i].Status == CaptureStatus.Undecodable)
          ++undecodable;
      }
      return recorded > 0 && undecodable > recorded * MovedMarkerUndecodableFraction;
    }

    private static bool HasMarker(CaptureRow row) => row.Status is CaptureStatus.Decoded or CaptureStatus.Torn;

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

    /// <summary>The start of every report file of each of <paramref name="runs"/>, in their order (<see cref="RunFilePrefix"/>).</summary>
    public static IReadOnlyList<string> RunFilePrefixes(IReadOnlyList<RunAnalysis> runs)
    {
      var ordinals = new Dictionary<uint, int>();
      var prefixes = new List<string>(runs.Count);
      foreach (var run in runs)
      {
        int ordinal = ordinals.GetValueOrDefault(run.RunId);
        ordinals[run.RunId] = ordinal + 1;
        prefixes.Add(RunFilePrefix(run, ordinal));
      }
      return prefixes;
    }

    private static void WriteReports(AnalysisReport report, AnalysisOptions options)
    {
      bool camera = report.Session?.Camera != null;
      WriteCaptures(Path.Combine(report.OutputDirectory, CapturesFileName), report.Capture.Rows);
      var ordinals = new Dictionary<uint, int>();
      var runFiles = new List<string>();
      for (int index = 0; index < report.Timeline.Runs.Count; ++index)
      {
        var run = report.Timeline.Runs[index];
        int ordinal = ordinals.TryGetValue(run.RunId, out int seen) ? seen : 0;
        ordinals[run.RunId] = ordinal + 1;
        var name = RunFramesFileName(run, ordinal);
        runFiles.Add(name);
        WriteFrames(Path.Combine(report.OutputDirectory, name), run.Frames, camera);

        // What the trace says of the run's frames; a run that was not joined has no such file, also not an earlier analysis's
        string tracePath = Path.Combine(report.OutputDirectory, TraceFramesCsv.FileName(RunFilePrefix(run, ordinal)));
        string checkPath = Path.Combine(report.OutputDirectory, RunFilePrefix(run, ordinal) + TraceLineUp.FileSuffix);
        if (report.TraceJoinOf(index) is { } join)
          TraceFramesCsv.Write(tracePath, join.Rows);
        else if (File.Exists(tracePath))
          File.Delete(tracePath);
        // How well the markers and the trace line up, and what the markers say against the application's frame record: the numbers
        // as a text of its own, for a reader
        if (report.TraceCheckLines(index) is { } lines)
          File.WriteAllText(checkPath, string.Join('\n', lines) + "\n", new UTF8Encoding(false));
        else if (File.Exists(checkPath))
          File.Delete(checkPath);
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
        report.Timeline.CapturePeriod,
        report.Timeline.CapturePeriod,
        report.Timeline.ErrorThreshold,
        report.Capture.Layout.Locks.Select(l => new SummaryMarker(l.Bounds.ToString(), l.ModuleSizePx)).ToList(),
        report.Warnings,
        report.Timeline.Runs.Select((run, i) => run.ToSummary(runFiles[i])).ToList()
      ).Write(path);
    }
  }
}
