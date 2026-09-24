//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Turns capture rows into runs, presented frames and animation error. Pure logic: no files, no decoding.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MB.FramePacing.Marker;

namespace MB.FramePacing.Analysis
{
  public static class TimelineAnalyzer
  {
    // The slow capture warning needs enough presented frames to tell a pattern from a few real skips
    private const int SlowCaptureMinFrames = 20;

    public static TimelineResult Analyze(IReadOnlyList<CaptureRow> rows, TimelineOptions? options = null)
    {
      options ??= new TimelineOptions();
      var warnings = new List<string>();
      long period = EstimateCapturePeriod(rows);

      var runRows = SplitIntoRuns(rows, warnings);
      var runs = new List<RunAnalysis>();
      foreach (var run in runRows)
      {
        if (options.RunId.HasValue && run.RunId != options.RunId.Value)
          continue;
        runs.Add(AnalyzeRun(run, period, options));
      }
      if (runs.Count == 0)
        warnings.Add(options.RunId.HasValue ? $"Run {options.RunId} was not found in the capture" : "No frame markers were found in the capture");
      return new TimelineResult(period, runs, warnings);
    }

    /// <summary>Median interval between consecutive recorded captures.</summary>
    public static long EstimateCapturePeriod(IReadOnlyList<CaptureRow> rows)
    {
      var deltas = new List<long>();
      CaptureRow? previous = null;
      foreach (var row in rows)
      {
        if (row.Status == CaptureStatus.NotRecorded)
        {
          previous = null;
          continue;
        }
        if (previous is { } p && row.CaptureIndex == p.CaptureIndex + 1 && row.CaptureTicks > p.CaptureTicks)
          deltas.Add(row.CaptureTicks - p.CaptureTicks);
        previous = row;
      }
      if (deltas.Count == 0)
        return 0;
      deltas.Sort();
      return deltas[deltas.Count / 2];
    }

    private sealed class RunRows
    {
      public uint RunId;
      public string? Name;
      public DateTime? StartTimeUtc;
      public bool HasStart;
      public bool HasEnd;
      public readonly List<CaptureRow> Rows = new List<CaptureRow>();
      public readonly List<string> Warnings = new List<string>();
    }

    private static List<RunRows> SplitIntoRuns(IReadOnlyList<CaptureRow> rows, List<string> warnings)
    {
      var runs = new List<RunRows>();
      bool hasStartMarkers = rows.Any(r => r.IsDecoded && r.Payload.Kind == MarkerKind.SequenceStart);

      if (!hasStartMarkers)
      {
        warnings.Add("No start markers found: the whole capture is analysed (one run per run id). Use start/end markers to measure an exact window.");
        var byRun = new Dictionary<uint, RunRows>();
        uint? currentRun = null;
        foreach (var row in rows)
        {
          if (row.IsDecoded && row.Payload.Kind == MarkerKind.Frame)
            currentRun = row.Payload.RunId;
          if (currentRun is not { } runId || (row.IsDecoded && row.Payload.Kind != MarkerKind.Frame))
            continue;
          if (!byRun.TryGetValue(runId, out var run))
          {
            run = new RunRows { RunId = runId };
            byRun.Add(runId, run);
            runs.Add(run);
          }
          run.Rows.Add(row);
        }
        return runs;
      }

      RunRows? current = null;
      bool measuring = false;
      foreach (var row in rows)
      {
        if (row.IsDecoded)
        {
          var payload = row.Payload;
          switch (payload.Kind)
          {
            case MarkerKind.SequenceStart:
              if (current != null && measuring)
              {
                CloseRun(
                  current,
                  runs,
                  current.RunId == payload.RunId
                    ? $"Run {current.RunId}: start marker seen again before the end marker, the run restarts"
                    : $"Run {current.RunId} ended without an end marker (run {payload.RunId} started)"
                );
                current = null;
                measuring = false;
              }
              // Consecutive captures of the same start marker keep the pending run
              if (current == null || current.RunId != payload.RunId)
              {
                current = new RunRows
                {
                  RunId = payload.RunId,
                  Name = row.Start?.Name,
                  StartTimeUtc = row.Start?.StartTimeUtc,
                  HasStart = true,
                };
              }
              continue;
            case MarkerKind.SequenceEnd:
              if (current != null && current.RunId == payload.RunId)
              {
                current.HasEnd = true;
                if (measuring)
                  runs.Add(current);
                else
                  current.Warnings.Add("End marker directly after the start marker: no frames measured");
                current = null;
                measuring = false;
              }
              continue;
            default:
              if (current == null)
                continue;
              if (payload.RunId != current.RunId)
              {
                if (measuring)
                  CloseRun(current, runs, $"Run {current.RunId} ended without an end marker (frames of run {payload.RunId} followed)");
                current = null;
                measuring = false;
                continue;
              }
              measuring = true;
              current.Rows.Add(row);
              continue;
          }
        }
        if (current != null && measuring)
          current.Rows.Add(row);
      }
      if (current != null && measuring)
        CloseRun(current, runs, $"Run {current.RunId} has no end marker (the capture ended first)");
      return runs;
    }

    private static void CloseRun(RunRows run, List<RunRows> runs, string warning)
    {
      run.Warnings.Add(warning);
      runs.Add(run);
    }

    private sealed class FrameBuilder
    {
      public int Segment;
      public ulong FrameIndex;
      public long AnimationTicks;
      public long FirstCaptureIndex;
      public long FirstSeenTicks;
      public long LastSeenTicks;
      public int CaptureCount;
      public ulong SkippedBefore;
      public bool UncertainStart;
    }

    private static RunAnalysis AnalyzeRun(RunRows run, long period, TimelineOptions options)
    {
      var warnings = new List<string>(run.Warnings);
      long decoded = 0;
      long undecodable = 0;
      long torn = 0;
      long notRecorded = 0;
      long sourceDrops = 0;
      long outOfOrder = 0;

      // Trailing undecodable rows after the last frame belong to the transition to the end marker; ignore them.
      var rows = run.Rows;
      int lastDecoded = rows.FindLastIndex(r => r.IsDecoded);
      var builders = new List<FrameBuilder>();
      FrameBuilder? current = null;
      int segment = 0;
      bool gapSinceLastFrame = false;

      for (int i = 0; i <= lastDecoded; ++i)
      {
        var row = rows[i];
        if (row.SourceDropBefore)
          ++sourceDrops;
        switch (row.Status)
        {
          case CaptureStatus.Undecodable:
            ++undecodable;
            gapSinceLastFrame = true;
            continue;
          case CaptureStatus.Torn:
            ++torn;
            gapSinceLastFrame = true;
            continue;
          case CaptureStatus.NotRecorded:
            ++notRecorded;
            gapSinceLastFrame = true;
            continue;
        }
        ++decoded;
        var payload = row.Payload;
        if (current != null && payload.FrameIndex == current.FrameIndex)
        {
          current.LastSeenTicks = row.CaptureTicks;
          current.CaptureCount++;
          gapSinceLastFrame = false;
          continue;
        }

        ulong skipped = 0;
        if (current != null)
        {
          if (payload.FrameIndex < current.FrameIndex)
          {
            if (current.FrameIndex - payload.FrameIndex > options.RestartThresholdFrames)
            {
              ++segment;
              warnings.Add($"Frame index jumped back from {current.FrameIndex} to {payload.FrameIndex} at capture {row.CaptureIndex}: new segment");
            }
            else
            {
              ++outOfOrder;
              continue;
            }
          }
          else
            skipped = payload.FrameIndex - current.FrameIndex - 1;
        }

        current = new FrameBuilder
        {
          Segment = segment,
          FrameIndex = payload.FrameIndex,
          AnimationTicks = payload.AnimationTicks,
          FirstCaptureIndex = row.CaptureIndex,
          FirstSeenTicks = row.CaptureTicks,
          LastSeenTicks = row.CaptureTicks,
          CaptureCount = 1,
          SkippedBefore = builders.Count > 0 && builders[^1].Segment == segment ? skipped : 0,
          UncertainStart = gapSinceLastFrame,
        };
        gapSinceLastFrame = false;
        builders.Add(current);
      }

      // Rows after the last decoded frame (end transition) still count towards the capture totals
      for (int i = lastDecoded + 1; i < rows.Count; ++i)
      {
        switch (rows[i].Status)
        {
          case CaptureStatus.Undecodable:
            ++undecodable;
            break;
          case CaptureStatus.Torn:
            ++torn;
            break;
          case CaptureStatus.NotRecorded:
            ++notRecorded;
            break;
        }
      }

      var frames = BuildFrames(builders, period);
      var withMetrics = frames.Where(f => f.AnimationErrorTicks.HasValue).ToList();
      var statistics = new RunStatistics(
        Statistics.FromTicks(withMetrics.Select(f => f.DisplayDeltaTicks!.Value)),
        Statistics.FromTicks(withMetrics.Select(f => f.AnimationDeltaTicks!.Value)),
        Statistics.FromTicks(withMetrics.Select(f => f.AnimationErrorTicks!.Value)),
        Statistics.FromTicks(withMetrics.Select(f => Math.Abs(f.AnimationErrorTicks!.Value))),
        Statistics.FromTicks(frames.Select(f => f.DriftTicks)),
        Statistics.FromTicks(frames.Select(f => f.OnScreenTicks)),
        withMetrics.LongCount(f => period > 0 && Math.Abs(f.AnimationErrorTicks!.Value) > period)
      );
      var counts = new RunCounts(
        rows.Count,
        decoded,
        undecodable,
        torn,
        notRecorded,
        sourceDrops,
        frames.Count,
        frames.Aggregate(0L, (sum, f) => sum + (long)f.SkippedBefore),
        outOfOrder,
        frames.Count > 0 ? frames[^1].Segment + 1 : 0
      );
      if (period <= 0)
        warnings.Add("The capture period could not be determined; animation error thresholds are unavailable");
      if (SlowCaptureWarning(frames, period) is { } slowCapture)
        warnings.Add(slowCapture);
      return new RunAnalysis(run.RunId, run.Name, run.StartTimeUtc, run.HasStart, run.HasEnd, counts, statistics, frames, warnings);
    }

    /// <summary>
    /// A capture at the display's refresh rate (with vsync) sees the next frame index in every refresh, so gaps are rare (stalls and
    /// skipped frames). When most presented frames come after frame indices that were never captured, the capture is slower than the
    /// display, or vsync is off: the results then describe what the capture saw, not what the display showed.
    /// </summary>
    private static string? SlowCaptureWarning(List<PresentedFrame> frames, long period)
    {
      int followers = 0;
      int afterGap = 0;
      double indices = 0;
      double seconds = 0;
      for (int i = 1; i < frames.Count; ++i)
      {
        if (frames[i].Segment != frames[i - 1].Segment)
          continue;
        ++followers;
        if (frames[i].SkippedBefore > 0)
          ++afterGap;
        indices += frames[i].FrameIndex - frames[i - 1].FrameIndex;
        seconds += (frames[i].FirstSeenTicks - frames[i - 1].FirstSeenTicks) / (double)TimeSpan.TicksPerSecond;
      }
      if (followers < SlowCaptureMinFrames || afterGap * 2 < followers)
        return null;

      string rates =
        seconds > 0 && period > 0
          ? string.Create(
            CultureInfo.InvariantCulture,
            $" The frame index advances about {indices / seconds:0} times per second; the capture records {TimeSpan.TicksPerSecond / (double)period:0} frames per second."
          )
          : string.Empty;
      return $"{afterGap * 100 / followers}% of the presented frames come after frame indices that were never captured: the capture is most likely slower than the display's refresh rate, or vsync is off, so the results describe what the capture saw, not what the display showed.{rates} Capture at the display's refresh rate with vsync on.";
    }

    private static List<PresentedFrame> BuildFrames(List<FrameBuilder> builders, long period)
    {
      var frames = new List<PresentedFrame>(builders.Count);
      int segmentStart = 0;
      for (int i = 0; i < builders.Count; ++i)
      {
        var b = builders[i];
        if (i > 0 && b.Segment != builders[i - 1].Segment)
          segmentStart = i;
        var first = builders[segmentStart];
        bool hasPrevious = i > segmentStart;
        var previous = hasPrevious ? builders[i - 1] : null;
        bool hasNext = i + 1 < builders.Count && builders[i + 1].Segment == b.Segment;

        long? displayDelta = previous != null ? b.FirstSeenTicks - previous.FirstSeenTicks : null;
        long? animationDelta = previous != null ? b.AnimationTicks - previous.AnimationTicks : null;
        long onScreen = hasNext ? builders[i + 1].FirstSeenTicks - b.FirstSeenTicks : b.LastSeenTicks - b.FirstSeenTicks + period;
        var flags = PresentedFrameFlags.None;
        if (b.SkippedBefore > 0)
          flags |= PresentedFrameFlags.SkippedBefore;
        if (b.UncertainStart && hasPrevious)
          flags |= PresentedFrameFlags.UncertainStart;

        frames.Add(
          new PresentedFrame(
            b.Segment,
            b.FrameIndex,
            b.AnimationTicks,
            b.FirstCaptureIndex,
            b.FirstSeenTicks,
            b.LastSeenTicks,
            b.CaptureCount,
            onScreen,
            b.SkippedBefore,
            displayDelta,
            animationDelta,
            displayDelta.HasValue ? animationDelta!.Value - displayDelta.Value : null,
            (b.AnimationTicks - first.AnimationTicks) - (b.FirstSeenTicks - first.FirstSeenTicks),
            flags
          )
        );
      }
      return frames;
    }
  }
}
