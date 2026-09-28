//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Reads an analysis output folder back (summary.json and every run's run-<id>-frames.csv), so reports can be drawn from what an analysis
//* wrote without the capture: the runs, their pacing, statistics and counts, and every presented frame. The CSV's milliseconds have four
//* decimals, which is whole 100 ns ticks, so the frames come back to the tick.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MB.FramePacing.Analysis;

namespace MB.FramePacing.Charts
{
  public static class AnalysisOutput
  {
    private static readonly JsonSerializerOptions g_jsonOptions = new JsonSerializerOptions
    {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      PropertyNameCaseInsensitive = true,
      Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The analysis output folder of <paramref name="folder"/>: the folder itself, or the capture folder's analysis folder.</summary>
    public static string Directory(string folder)
    {
      if (File.Exists(Path.Combine(folder, CaptureAnalyzer.SummaryFileName)))
        return folder;
      string analysis = Path.Combine(folder, CaptureAnalyzer.AnalysisDirectoryName);
      if (File.Exists(Path.Combine(analysis, CaptureAnalyzer.SummaryFileName)))
        return analysis;
      throw new FileNotFoundException(
        $"'{folder}' holds no analysis ({CaptureAnalyzer.SummaryFileName}, or {CaptureAnalyzer.AnalysisDirectoryName}/{CaptureAnalyzer.SummaryFileName}): "
          + "run 'analyze' first"
      );
    }

    /// <summary>Every run of the analysis in <paramref name="folder"/> (an analysis output folder, or a capture folder that has one).</summary>
    public static IReadOnlyList<AnalysisOutputRun> Read(string folder)
    {
      string directory = Directory(folder);
      using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, CaptureAnalyzer.SummaryFileName)));
      var root = document.RootElement;
      long capturePeriod = Ticks(root.GetProperty("capturePeriodMs").GetDouble());
      long threshold = Ticks(root.GetProperty("errorThresholdMs").GetDouble());
      bool camera = root.TryGetProperty("scanout", out var scanout) && scanout.GetString() == nameof(ScanoutModel.Camera);

      var runs = new List<AnalysisOutputRun>();
      foreach (var run in root.GetProperty("runs").EnumerateArray())
      {
        string framesFile = run.GetProperty("framesFile").GetString()!;
        var frames = ReadFrames(Path.Combine(directory, framesFile), capturePeriod);
        var analysis = new RunAnalysis(
          run.GetProperty("runId").GetUInt32(),
          run.TryGetProperty("name", out var name) ? name.GetString() : null,
          run.TryGetProperty("startTimeUtc", out var start) ? start.GetDateTime() : null,
          run.GetProperty("hasStartMarker").GetBoolean(),
          run.GetProperty("hasEndMarker").GetBoolean(),
          run.GetProperty("counts").Deserialize<RunCounts>(g_jsonOptions)!,
          run.GetProperty("statistics").Deserialize<RunStatistics>(g_jsonOptions)!,
          frames,
          run.TryGetProperty("warnings", out var warnings) ? warnings.Deserialize<string[]>(g_jsonOptions)! : Array.Empty<string>(),
          Pacing: run.TryGetProperty("pacing", out var pacing) ? pacing.Deserialize<RunPacing>(g_jsonOptions) : null
        );
        string prefix = framesFile.EndsWith("-frames.csv", StringComparison.Ordinal) ? framesFile[..^"-frames.csv".Length] : $"run-{analysis.RunId}";
        runs.Add(new AnalysisOutputRun(new ChartRun(analysis, capturePeriod, threshold, camera), prefix));
      }
      return runs;
    }

    /// <summary>The presented frames of a run-&lt;id&gt;-frames.csv, by column name.</summary>
    public static IReadOnlyList<PresentedFrame> ReadFrames(string path, long capturePeriodTicks)
    {
      using var reader = new StreamReader(path);
      var header = (reader.ReadLine() ?? throw new InvalidDataException($"'{path}' is empty")).Split(',');
      var column = header.Select((name, index) => (name, index)).ToDictionary(c => c.name, c => c.index);
      int Column(string name) => column.TryGetValue(name, out int index) ? index : -1;
      int segment = Column("segment");
      int frameIndex = Column("frameIndex");
      int animation = Column("animationMs");
      int firstCapture = Column("firstCaptureIndex");
      int firstSeen = Column("firstSeenMs");
      int onScreen = Column("onScreenMs");
      int captures = Column("captures");
      int skipped = Column("skippedBefore");
      int display = Column("displayDeltaMs");
      int animationDelta = Column("animationDeltaMs");
      int error = Column("animationErrorMs");
      int drift = Column("driftMs");
      int flags = Column("flags");
      int intended = Column("intendedDisplayMs");
      int markerTarget = Column("markerTargetMs");
      int target = Column("targetMs");
      int pacing = Column("pacingErrorMs");
      int prediction = Column("predictionErrorMs");
      int lateness = Column("latenessMs");
      int lastSeen = Column("lastSeenMs");
      int mainSeen = Column("mainMarkerFirstSeenMs");

      var frames = new List<PresentedFrame>();
      string? line;
      while ((line = reader.ReadLine()) != null)
      {
        if (line.Length == 0)
          continue;
        var cells = line.Split(',');
        string Cell(int index) => index >= 0 && index < cells.Length ? cells[index] : string.Empty;
        long? Optional(int index) => Cell(index) is { Length: > 0 } text ? Ticks(text) : null;
        long first = Ticks(Cell(firstSeen));
        long screen = Ticks(Cell(onScreen));
        frames.Add(
          new PresentedFrame(
            int.Parse(Cell(segment), CultureInfo.InvariantCulture),
            ulong.Parse(Cell(frameIndex), CultureInfo.InvariantCulture),
            Ticks(Cell(animation)),
            long.Parse(Cell(firstCapture), CultureInfo.InvariantCulture),
            first,
            // Written since the capture data; older reports: the last capture the time on screen allows
            Optional(lastSeen) ?? first + Math.Max(0, screen - capturePeriodTicks),
            int.Parse(Cell(captures), CultureInfo.InvariantCulture),
            screen,
            ulong.Parse(Cell(skipped), CultureInfo.InvariantCulture),
            Optional(display),
            Optional(animationDelta),
            Optional(error),
            Ticks(Cell(drift)),
            ParseFlags(Cell(flags)),
            Optional(mainSeen),
            Optional(intended) ?? 0,
            (uint)(Optional(markerTarget) ?? 0),
            Optional(target),
            Optional(pacing),
            Optional(prediction),
            Optional(lateness)
          )
        );
      }
      return frames;
    }

    private static PresentedFrameFlags ParseFlags(string text) =>
      text.Length == 0
        ? PresentedFrameFlags.None
        : text.Split('|').Aggregate(PresentedFrameFlags.None, (flags, name) => flags | Enum.Parse<PresentedFrameFlags>(name));

    private static long Ticks(string ms) => Ticks(double.Parse(ms, CultureInfo.InvariantCulture));

    private static long Ticks(double ms) => (long)Math.Round(ms * TimeSpan.TicksPerMillisecond);
  }
}
