//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The golden data's digest (digest.json): counts and sums of what a reader reads from every file, in integers where the file has them.
//* The C#, Python and C++ data libraries compute the same digest from the same files and compare it with digest.json, so all three read the
//* same values.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
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
      ("animationTicks", r => r.AnimationTime.Ticks),
      ("firstCaptureIndex", r => r.FirstCaptureIndex),
      ("firstSeenTicks", r => r.FirstSeenTime.Ticks),
      ("onScreenTicks", r => r.OnScreen.Ticks),
      ("captures", r => r.Captures),
      ("skippedBefore", r => (long)r.SkippedBefore),
      ("displayDeltaTicks", r => r.DisplayDelta?.Ticks),
      ("animationDeltaTicks", r => r.AnimationDelta?.Ticks),
      ("animationErrorTicks", r => r.AnimationError?.Ticks),
      ("driftTicks", r => r.Drift.Ticks),
      ("intendedDisplayTicks", r => r.IntendedDisplayTime?.Ticks),
      ("markerTargetTicks", r => r.MarkerTargetFrameTime?.Ticks),
      ("targetTicks", r => r.TargetFrameTime?.Ticks),
      ("markerPreferredTicks", r => r.MarkerPreferredFrameTime?.Ticks),
      ("preferredTicks", r => r.PreferredFrameTime?.Ticks),
      ("pacingErrorTicks", r => r.PacingError?.Ticks),
      ("predictionErrorTicks", r => r.PredictionError?.Ticks),
      ("latenessTicks", r => r.Lateness?.Ticks),
      ("lastSeenTicks", r => r.LastSeenTime?.Ticks),
      ("cpuStartTicks", r => r.CpuStartTime?.Ticks),
      ("cpuBusyTicks", r => r.CpuBusy?.Ticks),
      ("frameTimeTicks", r => r.FrameTime?.Ticks),
      ("cpuWaitTicks", r => r.CpuWait?.Ticks),
      ("mainMarkerFirstSeenTicks", r => r.MainMarkerFirstSeenTime?.Ticks),
      ("scanoutDelayTicks", r => r.ScanoutDelay?.Ticks),
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
      long animationNsSum = 0;
      int decodedPayloads = 0;
      foreach (var record in records)
      {
        if (record.TryDecodeMain(out var payload, out _))
        {
          ++decodedPayloads;
          frameIndexSum += (long)payload.FrameIndex;
          animationNsSum += payload.AnimationTime.Nanoseconds;
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
          ["syncRegion"] = Rect(header.SyncRegion),
          ["markers"] = new JsonArray(
            header.Markers.Select(m => (JsonNode)new JsonObject { ["bounds"] = Rect(m.Bounds), ["moduleSizePx"] = m.ModuleSizePx }).ToArray()
          ),
          ["framesStored"] = header.FramesStored,
          ["camera"] = header.Camera,
        },
        ["recordCount"] = records.Length,
        ["captureIndexSum"] = records.Sum(r => r.CaptureIndex),
        ["hostTicksSum"] = records.Sum(r => r.HostTime.Ticks),
        ["deviceTicksCount"] = records.Count(r => r.DeviceTime.HasValue),
        ["deviceTicksSum"] = records.Sum(r => r.DeviceTime?.Ticks ?? 0),
        ["sourceDropsSum"] = records.Sum(r => (long)r.SourceDrops),
        ["statusCounts"] = Counts(records.Select(r => r.CaptureStatus.ToString())),
        ["mainByteCount"] = records.Sum(r => r.MainBytes?.Length ?? 0),
        ["secondByteCount"] = records.Sum(r => r.SecondBytes?.Length ?? 0),
        ["decodedMainPayloads"] = decodedPayloads,
        ["frameIndexSum"] = frameIndexSum,
        ["animationNsSum"] = animationNsSum,
      };
    }

    private static JsonObject Summary(AnalysisSummary summary) =>
      new JsonObject
      {
        ["formatVersion"] = summary.FormatVersion,
        ["scanout"] = summary.Scanout,
        ["timeSource"] = summary.TimeSource,
        ["capturePeriodTicks"] = summary.CapturePeriod.Ticks,
        ["measurementResolutionTicks"] = summary.MeasurementResolution.Ticks,
        ["errorThresholdTicks"] = summary.ErrorThreshold.Ticks,
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
                  ["droppedFrames"] = r.Counts.DroppedFrames,
                  ["sourceDroppedFrames"] = r.Counts.SourceDroppedFrames,
                  ["missedCaptures"] = r.Counts.MissedCaptures,
                  ["captures"] = r.Counts.Captures,
                  ["displayDeltaCount"] = r.Statistics.DisplayDeltaMs.Count,
                  ["displayDeltaP50"] = r.Statistics.DisplayDeltaMs.P50,
                  ["animationErrorMax"] = r.Statistics.AnimationErrorMs.Max,
                  ["averageFps"] = r.Statistics.AverageFps,
                  ["excludedStaticFrames"] = r.Statistics.ExcludedStaticFrames,
                  ["uncertainSteps"] = r.Statistics.UncertainSteps,
                  ["cpuBusyCount"] = r.Statistics.CpuBusyMs?.Count ?? 0,
                  ["pacingSource"] = r.Pacing?.Source,
                  ["lateFrames"] = r.Pacing?.LateFrames ?? 0,
                  ["refreshPeriodTicks"] = r.Pacing?.RefreshPeriod.Ticks ?? 0,
                  ["targetFrameTicks"] = r.Pacing?.TargetFrameTime.Ticks ?? 0,
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
        ["olderFrameCount"] = rows.Sum(r => r.OlderFrames.Count),
        ["olderFrameIndexSum"] = rows.SelectMany(r => r.OlderFrames).Aggregate(0UL, (sum, o) => sum + o.FrameIndex),
      };
    }

    private static JsonObject Captures(string path)
    {
      var rows = CapturesCsv.Read(path);
      return new JsonObject
      {
        ["rowCount"] = rows.Count,
        ["statusCounts"] = Counts(rows.Select(r => r.CaptureStatus)),
        ["kindCounts"] = Counts(rows.Where(r => r.Kind != null).Select(r => r.Kind!)),
        ["captureTicksSum"] = rows.Sum(r => r.CaptureTime?.Ticks ?? 0),
        ["frameIndexSum"] = rows.Sum(r => (long)(r.FrameIndex ?? 0)),
        ["hostTicksSum"] = rows.Sum(r => r.HostTime?.Ticks ?? 0),
        ["sourceDropsSum"] = rows.Sum(r => r.SourceDropsBefore),
        ["missedSum"] = rows.Sum(r => r.MissedBefore),
        ["syncCount"] = rows.Count(r => r.SyncFrameIndex.HasValue),
        ["syncFrameIndexSum"] = rows.Sum(r => (long)(r.SyncFrameIndex ?? 0)),
        ["payloadByteCount"] = rows.Sum(r => r.Payload?.Length ?? 0),
      };
    }

    private static JsonArray Rect(Rectangle rect) => new JsonArray(rect.X, rect.Y, rect.Width, rect.Height);

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
