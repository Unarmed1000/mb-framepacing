//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The golden data's digest (digest.json): counts and sums of what a reader reads from every file, in integers where the file has them.
//* The C#, Python and C++ data libraries compute the same digest from the same files and compare it with digest.json, so all three read the
//* same values.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace MB.FramePacing.Data.UnitTest
{
  internal static class DataDigest
  {
    // The frames CSV's columns, as the digest names them (their CSV names), and how to read each from a row
    private static readonly (string Name, Func<FrameRow, long?> Value)[] g_frameColumns =
    {
      ("segment", r => r.Segment),
      ("frameIndex", r => (long)r.FrameIndex),
      ("animationMs", r => r.AnimationTicks),
      ("firstCaptureIndex", r => r.FirstCaptureIndex),
      ("firstSeenMs", r => r.FirstSeenTicks),
      ("onScreenMs", r => r.OnScreenTicks),
      ("captures", r => r.Captures),
      ("skippedBefore", r => (long)r.SkippedBefore),
      ("displayDeltaMs", r => r.DisplayDeltaTicks),
      ("animationDeltaMs", r => r.AnimationDeltaTicks),
      ("animationErrorMs", r => r.AnimationErrorTicks),
      ("driftMs", r => r.DriftTicks),
      ("intendedDisplayMs", r => r.IntendedDisplayTicks),
      ("markerTargetMs", r => r.MarkerTargetTicks),
      ("targetMs", r => r.TargetTicks),
      ("pacingErrorMs", r => r.PacingErrorTicks),
      ("predictionErrorMs", r => r.PredictionErrorTicks),
      ("latenessMs", r => r.LatenessTicks),
      ("lastSeenMs", r => r.LastSeenTicks),
      ("cpuStartMs", r => r.CpuStartTicks),
      ("cpuBusyMs", r => r.CpuBusyTicks),
      ("frameTimeMs", r => r.FrameTimeTicks),
      ("cpuWaitMs", r => r.CpuWaitTicks),
      ("mainMarkerFirstSeenMs", r => r.MainMarkerFirstSeenTicks),
      ("scanoutDelayMs", r => r.ScanoutDelayTicks),
    };

    public static JsonObject Compute(string clipDirectory)
    {
      string analysis = Path.Combine(clipDirectory, AnalysisFiles.DirectoryName);
      var summary = AnalysisSummary.Read(Path.Combine(analysis, AnalysisFiles.SummaryFileName));
      return new JsonObject
      {
        ["captureData"] = CaptureData(Path.Combine(clipDirectory, CaptureDataHeader.FileName)),
        ["summary"] = Summary(summary),
        ["frames"] = new JsonArray(summary.Runs.Select(r => (JsonNode)Frames(Path.Combine(analysis, r.FramesFile))).ToArray()),
        ["captures"] = Captures(Path.Combine(analysis, AnalysisFiles.CapturesFileName)),
      };
    }

    private static JsonObject CaptureData(string path)
    {
      using var reader = new CaptureDataReader(path);
      var header = reader.Header;
      var records = reader.ReadAll();
      long frameIndexSum = 0;
      long animationTicksSum = 0;
      int decodedPayloads = 0;
      foreach (var record in records)
      {
        if (record.TryDecodeMain(out var payload, out _))
        {
          ++decodedPayloads;
          frameIndexSum += (long)payload.FrameIndex;
          animationTicksSum += payload.AnimationTicks;
        }
      }
      return new JsonObject
      {
        ["header"] = new JsonObject
        {
          ["width"] = header.Width,
          ["height"] = header.Height,
          ["frameRateNumerator"] = header.FrameRateNumerator,
          ["frameRateDenominator"] = header.FrameRateDenominator,
          ["sourceWidth"] = header.SourceWidth,
          ["sourceHeight"] = header.SourceHeight,
          ["region"] = Rect(header.Region),
          ["markers"] = new JsonArray(
            header.Markers.Select(m => (JsonNode)new JsonObject { ["bounds"] = Rect(m.Bounds), ["moduleSizePx"] = m.ModuleSizePx }).ToArray()
          ),
          ["framesStored"] = header.FramesStored,
          ["camera"] = header.Camera,
        },
        ["recordCount"] = records.Length,
        ["captureIndexSum"] = records.Sum(r => r.CaptureIndex),
        ["hostTicksSum"] = records.Sum(r => r.HostTicks),
        ["deviceTicksCount"] = records.Count(r => r.HasDeviceTicks),
        ["deviceTicksSum"] = records.Where(r => r.HasDeviceTicks).Sum(r => r.DeviceTicks),
        ["sourceDropCount"] = records.Count(r => (r.Flags & CaptureRecordFlags.SourceDropBefore) != 0),
        ["statusCounts"] = Counts(records.Select(r => r.Status.ToString())),
        ["mainByteCount"] = records.Sum(r => r.MainBytes?.Length ?? 0),
        ["secondByteCount"] = records.Sum(r => r.SecondBytes?.Length ?? 0),
        ["decodedMainPayloads"] = decodedPayloads,
        ["frameIndexSum"] = frameIndexSum,
        ["animationTicksSum"] = animationTicksSum,
      };
    }

    private static JsonObject Summary(AnalysisSummary summary) =>
      new JsonObject
      {
        ["formatVersion"] = summary.FormatVersion,
        ["scanout"] = summary.Scanout,
        ["timeSource"] = summary.TimeSource,
        ["capturePeriodMs"] = summary.CapturePeriodMs,
        ["errorThresholdMs"] = summary.ErrorThresholdMs,
        ["markerCount"] = summary.Markers.Count,
        ["warningCount"] = summary.Warnings.Count,
        ["runs"] = new JsonArray(
          summary
            .Runs.Select(r =>
              (JsonNode)
                new JsonObject
                {
                  ["runId"] = r.RunId,
                  ["sequenceId"] = r.SequenceId,
                  ["framesFile"] = r.FramesFile,
                  ["hasStartMarker"] = r.HasStartMarker,
                  ["hasEndMarker"] = r.HasEndMarker,
                  ["presentedFrames"] = r.Counts.PresentedFrames,
                  ["captures"] = r.Counts.Captures,
                  ["displayDeltaCount"] = r.Statistics.DisplayDeltaMs.Count,
                  ["displayDeltaP50"] = r.Statistics.DisplayDeltaMs.P50,
                  ["animationErrorMax"] = r.Statistics.AnimationErrorMs.Max,
                  ["averageFps"] = r.Statistics.AverageFps,
                  ["cpuBusyCount"] = r.Statistics.CpuBusyMs?.Count ?? 0,
                  ["pacingSource"] = r.Pacing?.Source,
                  ["lateFrames"] = r.Pacing?.LateFrames ?? 0,
                  ["histogramBins"] = r.Histograms?.AnimationErrorMs.Bins.Count ?? 0,
                }
            )
            .ToArray()
        ),
      };

    private static JsonObject Frames(string path)
    {
      var rows = FramesCsv.Read(path);
      var columns = new JsonObject();
      foreach (var (name, value) in g_frameColumns)
      {
        var values = rows.Select(value).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        columns[name] = new JsonObject { ["count"] = values.Count, ["sum"] = values.Sum() };
      }
      return new JsonObject
      {
        ["file"] = Path.GetFileName(path),
        ["rowCount"] = rows.Count,
        ["columns"] = columns,
        ["flagCounts"] = Counts(rows.SelectMany(r => r.Flags)),
      };
    }

    private static JsonObject Captures(string path)
    {
      var rows = CapturesCsv.Read(path);
      return new JsonObject
      {
        ["rowCount"] = rows.Count,
        ["statusCounts"] = Counts(rows.Select(r => r.Status)),
        ["kindCounts"] = Counts(rows.Where(r => r.Kind != null).Select(r => r.Kind!)),
        ["captureTicksSum"] = rows.Sum(r => r.CaptureTicks ?? 0),
        ["frameIndexSum"] = rows.Sum(r => (long)(r.FrameIndex ?? 0)),
        ["hostTicksSum"] = rows.Sum(r => r.HostTicks ?? 0),
        ["sourceDropCount"] = rows.Count(r => r.SourceDropBefore),
        ["payloadByteCount"] = rows.Sum(r => r.Payload?.Length ?? 0),
      };
    }

    private static JsonArray Rect(DataRect rect) => new JsonArray(rect.X, rect.Y, rect.Width, rect.Height);

    /// <summary>How often each name occurs, by name in ordinal order.</summary>
    private static JsonObject Counts(IEnumerable<string> names)
    {
      var counts = new JsonObject();
      foreach (var group in names.GroupBy(n => n).OrderBy(g => g.Key, StringComparer.Ordinal))
        counts[group.Key] = group.Count();
      return counts;
    }
  }
}
