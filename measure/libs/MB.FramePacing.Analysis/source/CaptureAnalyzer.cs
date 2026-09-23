//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Analyse a capture directory (frames.mbfc + capture.json) and write the reports to <capture>/analysis:
//*   captures.csv                - one row per capture index
//*   run-<id>[-<n>]-frames.csv   - one row per presented application frame
//*   summary.json                - everything else (layout, counts, statistics, histograms, warnings)
//*
//* (c) 2026 Mana Battery
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
      var framesPath = Path.Combine(captureDirectory, CaptureSessionInfo.FramesFileName);
      if (!File.Exists(framesPath))
        throw new FileNotFoundException($"'{captureDirectory}' does not contain a capture ({CaptureSessionInfo.FramesFileName})", framesPath);

      var session = CaptureSessionInfo.TryLoad(captureDirectory);
      DecodedCapture capture;
      using (var reader = new CaptureFileReader(framesPath))
        capture = CaptureDecoder.Decode(reader, options.TimeSource, progress, cancellationToken);

      var timeline = TimelineAnalyzer.Analyze(capture.Rows, options.Timeline);
      var warnings = new List<string>(capture.Layout.Warnings);
      warnings.AddRange(timeline.Warnings);
      if (capture.TimeSource == TimeSource.Host)
        warnings.Add("Host timestamps are used (the capture has no device timestamps): expect extra jitter from process scheduling.");
      if (session is { FramesDroppedByRecorder: > 0 })
        warnings.Add($"The recorder dropped {session.FramesDroppedByRecorder} frames (disk too slow?); they appear as NotRecorded captures.");
      if (session is { FramesDroppedBySource: > 0 })
        warnings.Add($"The capture device/ffmpeg reported {session.FramesDroppedBySource} dropped frames.");

      var outputDirectory = options.OutputDirectory ?? Path.Combine(captureDirectory, AnalysisDirectoryName);
      Directory.CreateDirectory(outputDirectory);
      var report = new AnalysisReport(captureDirectory, outputDirectory, session, capture, timeline, warnings);
      WriteReports(report, options);
      return report;
    }

    public static string RunFramesFileName(RunAnalysis run, int ordinal) =>
      ordinal == 0 ? $"run-{run.RunId}-frames.csv" : $"run-{run.RunId}-{ordinal + 1}-frames.csv";

    private static void WriteReports(AnalysisReport report, AnalysisOptions options)
    {
      WriteCaptures(Path.Combine(report.OutputDirectory, CapturesFileName), report.Capture.Rows);
      var ordinals = new Dictionary<uint, int>();
      var runFiles = new List<string>();
      foreach (var run in report.Timeline.Runs)
      {
        int ordinal = ordinals.TryGetValue(run.RunId, out int seen) ? seen : 0;
        ordinals[run.RunId] = ordinal + 1;
        var name = RunFramesFileName(run, ordinal);
        runFiles.Add(name);
        WriteFrames(Path.Combine(report.OutputDirectory, name), run.Frames);
      }
      WriteSummary(Path.Combine(report.OutputDirectory, SummaryFileName), report, options, runFiles);
    }

    private static void WriteCaptures(string path, IReadOnlyList<CaptureRow> rows)
    {
      using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
      writer.WriteLine("captureIndex,captureMs,status,kind,runId,frameIndex,animationMs,sourceDropBefore");
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
            row.SourceDropBefore ? "1" : "0"
          )
        );
      }
    }

    private static void WriteFrames(string path, IReadOnlyList<PresentedFrame> frames)
    {
      using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
      writer.WriteLine(
        "segment,frameIndex,animationMs,firstCaptureIndex,firstSeenMs,onScreenMs,captures,skippedBefore,displayDeltaMs,animationDeltaMs,animationErrorMs,driftMs,flags"
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
            frame.Flags == PresentedFrameFlags.None ? string.Empty : frame.Flags.ToString().Replace(", ", "|", StringComparison.Ordinal)
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
        analysedUtc = DateTime.UtcNow,
        captureDirectory = Path.GetFullPath(report.CaptureDirectory),
        capture = report.Session,
        frameSize = $"{report.Capture.Header.Width}x{report.Capture.Header.Height}",
        timeSource = report.Capture.TimeSource,
        capturePeriodMs = report.CapturePeriodMs,
        measurementResolutionMs = report.CapturePeriodMs,
        markers = layout.Locks.Select(l => new { bounds = l.Bounds.ToString(), moduleSizePx = l.ModuleSizePx }),
        warnings = report.Warnings,
        runs = report.Timeline.Runs.Select(
          (run, i) =>
            new
            {
              run.RunId,
              run.Name,
              run.StartTimeUtc,
              run.HasStartMarker,
              run.HasEndMarker,
              framesFile = runFiles[i],
              run.Counts,
              run.Statistics,
              histograms = RunHistograms.Create(run, report.Timeline.CapturePeriodTicks),
              run.Warnings,
            }
        ),
      };
      File.WriteAllText(path, JsonSerializer.Serialize(summary, g_jsonOptions));
    }

    private static string Ms(long ticks) => (ticks / (double)TimeSpan.TicksPerMillisecond).ToString("0.####", CultureInfo.InvariantCulture);
  }
}
