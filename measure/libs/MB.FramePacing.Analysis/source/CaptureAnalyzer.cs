//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Analyse a capture directory (captures.mbcd, the capture data, + capture.json) and write the reports to <capture>/analysis. A capture that
//* only has its frames (frames.mbfc) is decoded into captures.mbcd first, so the next analysis starts from the data:
//*   captures.csv                - one row per capture index
//*   run-<id>[-<n>]-frames.csv   - one row per presented application frame
//*   summary.json                - everything else (layout, counts, statistics, histograms, warnings)
//*
//* (c) 2026 Mana Battery
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

namespace MB.FramePacing.Analysis
{
  public static class CaptureAnalyzer
  {
    public const string AnalysisDirectoryName = "analysis";
    public const string SummaryFileName = "summary.json";
    public const string CapturesFileName = "captures.csv";
    public const double MovedMarkerUndecodableFraction = 0.05;

    /// <summary>A camera capture always loses a few captures per frame to the scanout crossing the marker; more than this is a problem.</summary>
    public const double CameraUndecodableFraction = 0.2;

    private static readonly JsonSerializerOptions g_jsonOptions = new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      Converters = { new JsonStringEnumConverter() },
    };

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
        {
          var buffer = new byte[4096 * CaptureDataRecord.Size];
          for (int first = 0; first < records.Count; first += 4096)
          {
            int count = Math.Min(4096, records.Count - first);
            for (int i = 0; i < count; ++i)
              records[first + i].Write(buffer.AsSpan(i * CaptureDataRecord.Size, CaptureDataRecord.Size));
            writer.WriteRecords(buffer.AsSpan(0, count * CaptureDataRecord.Size));
          }
        }
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
    public static string RunFilePrefix(RunAnalysis run, int ordinal) => ordinal == 0 ? $"run-{run.RunId}" : $"run-{run.RunId}-{ordinal + 1}";

    public static string RunFramesFileName(RunAnalysis run, int ordinal) => RunFilePrefix(run, ordinal) + "-frames.csv";

    private static void WriteReports(AnalysisReport report, AnalysisOptions options)
    {
      bool camera = report.Session?.Camera != null;
      WriteCaptures(Path.Combine(report.OutputDirectory, CapturesFileName), report.Capture.Rows, camera);
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

    private static void WriteCaptures(string path, IReadOnlyList<CaptureRow> rows, bool camera)
    {
      using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
      writer.WriteLine(
        "captureIndex,captureMs,status,kind,runId,frameIndex,animationMs,sourceDropBefore,hostMs,deviceMs,payloadHex"
          + (camera ? ",secondZoneFrameIndex" : "")
      );
      foreach (var row in rows)
      {
        bool hasMarker = row.Status is CaptureStatus.Decoded or CaptureStatus.Torn && row.Payload != default;
        writer.WriteLine(
          string.Join(
            ',',
            row.CaptureIndex.ToString(CultureInfo.InvariantCulture),
            row.Status == CaptureStatus.NotRecorded ? string.Empty : Ms(row.CaptureTicks),
            row.Status,
            hasMarker ? row.Payload.Kind.ToString() : string.Empty,
            hasMarker ? row.Payload.RunId.ToString(CultureInfo.InvariantCulture) : string.Empty,
            hasMarker ? row.Payload.FrameIndex.ToString(CultureInfo.InvariantCulture) : string.Empty,
            hasMarker ? Ms(row.Payload.AnimationTicks) : string.Empty,
            row.SourceDropBefore ? "1" : "0",
            row.HostTicks is { } host ? Ms(host) : string.Empty,
            row.DeviceTicks is { } device ? Ms(device) : string.Empty,
            row.MarkerBytes != null ? Convert.ToHexString(row.MarkerBytes) : string.Empty
          ) + (camera ? "," + (row.SecondaryFrameIndex?.ToString(CultureInfo.InvariantCulture) ?? string.Empty) : string.Empty)
        );
      }
    }

    private static void WriteFrames(string path, IReadOnlyList<PresentedFrame> frames, bool camera)
    {
      using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
      writer.WriteLine(
        "segment,frameIndex,animationMs,firstCaptureIndex,firstSeenMs,onScreenMs,captures,skippedBefore,displayDeltaMs,animationDeltaMs,animationErrorMs,driftMs,flags,"
          + "intendedDisplayMs,markerTargetMs,targetMs,pacingErrorMs,predictionErrorMs,latenessMs,lastSeenMs"
          + (camera ? ",mainMarkerFirstSeenMs,scanoutDelayMs" : "")
      );
      foreach (var frame in frames)
      {
        writer.WriteLine(
          string.Join(
            ',',
            frame.Segment.ToString(CultureInfo.InvariantCulture),
            frame.FrameIndex.ToString(CultureInfo.InvariantCulture),
            Ms(frame.AnimationTicks),
            frame.FirstCaptureIndex.ToString(CultureInfo.InvariantCulture),
            Ms(frame.FirstSeenTicks),
            Ms(frame.OnScreenTicks),
            frame.CaptureCount.ToString(CultureInfo.InvariantCulture),
            frame.SkippedBefore.ToString(CultureInfo.InvariantCulture),
            frame.DisplayDeltaTicks is { } display ? Ms(display) : string.Empty,
            frame.AnimationDeltaTicks is { } animation ? Ms(animation) : string.Empty,
            frame.AnimationErrorTicks is { } error ? Ms(error) : string.Empty,
            Ms(frame.DriftTicks),
            frame.Flags == PresentedFrameFlags.None ? string.Empty : frame.Flags.ToString().Replace(", ", "|", StringComparison.Ordinal),
            frame.IntendedDisplayTicks != 0 ? Ms(frame.IntendedDisplayTicks) : string.Empty,
            frame.MarkerTargetFrameTicks != 0 ? Ms(frame.MarkerTargetFrameTicks) : string.Empty,
            frame.TargetTicks is { } target ? Ms(target) : string.Empty,
            frame.PacingErrorTicks is { } pacing ? Ms(pacing) : string.Empty,
            frame.PredictionErrorTicks is { } prediction ? Ms(prediction) : string.Empty,
            frame.LatenessTicks is { } lateness ? Ms(lateness) : string.Empty,
            Ms(frame.LastSeenTicks)
          )
            + (
              camera
                ? frame.FirstSeenMainTicks is { } main
                  ? "," + Ms(main) + "," + Ms(frame.FirstSeenTicks - main)
                  : ",,"
                : string.Empty
            )
        );
      }
    }

    private static void WriteSummary(string path, AnalysisReport report, AnalysisOptions options, List<string> runFiles)
    {
      var layout = report.Capture.Layout;
      var summary = new
      {
        toolVersion = options.ToolVersion,
        experimental = report.Session?.Camera != null ? "Camera capture: " + Capture.Camera.CameraRig.ExperimentalNotice : null,
        scanout = report.Session?.Camera != null ? ScanoutModel.Camera : ScanoutModel.SingleScanout,
        analysedUtc = DateTime.UtcNow,
        captureDirectory = Path.GetFullPath(report.CaptureDirectory),
        capture = report.Session,
        frameSize = $"{report.Capture.Header.Width}x{report.Capture.Header.Height}",
        timeSource = report.Capture.TimeSource,
        capturePeriodMs = report.CapturePeriodMs,
        measurementResolutionMs = report.CapturePeriodMs,
        errorThresholdMs = report.ErrorThresholdMs,
        markers = layout.Locks.Select(l => new { bounds = l.Bounds.ToString(), moduleSizePx = l.ModuleSizePx }),
        warnings = report.Warnings,
        runs = report.Timeline.Runs.Select(
          (run, i) =>
            new
            {
              run.RunId,
              run.SequenceId,
              run.StartTimeUtc,
              run.HasStartMarker,
              run.HasEndMarker,
              framesFile = runFiles[i],
              run.Counts,
              run.Statistics,
              run.Pacing,
              histograms = RunHistograms.Create(run),
              camera = run.Camera,
              run.Warnings,
            }
        ),
      };
      File.WriteAllText(path, JsonSerializer.Serialize(summary, g_jsonOptions));
    }

    private static string Ms(long ticks) => (ticks / (double)TimeSpan.TicksPerMillisecond).ToString("0.####", CultureInfo.InvariantCulture);
  }
}
