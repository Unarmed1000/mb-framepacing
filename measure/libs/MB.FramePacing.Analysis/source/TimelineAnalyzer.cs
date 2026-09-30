//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Turns capture rows into runs, presented frames and animation error. Pure logic: no files, no decoding.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MB.FramePacing.Capture.Camera;
using MB.FramePacing.MarkerDecoding;

namespace MB.FramePacing.Analysis
{
  public static class TimelineAnalyzer
  {
    // The slow capture warning needs enough presented frames to tell a pattern from a few real skips
    private const int SlowCaptureMinFrames = 20;

    /// <summary>How far the refresh rate may differ from the expected or calibrated one before the analysis warns (relative).</summary>
    public const double RefreshTolerance = 0.01;

    /// <summary>The default |animation error| above which a frame counts as off (<see cref="TimelineOptions.ErrorThresholdTicks"/>): 1 ms.</summary>
    public const long DefaultErrorThresholdTicks = TimeSpan.TicksPerMillisecond;

    public static TimelineResult Analyze(IReadOnlyList<CaptureRow> rows, TimelineOptions? options = null)
    {
      options ??= new TimelineOptions();
      var warnings = new List<string>();
      long period = EstimateCapturePeriod(rows);
      long threshold = options.ErrorThresholdTicks;
      // The report is the same for every source; a camera's coarser timing shows as noise in the errors, and the warning says how much
      if (options.Scanout == ScanoutModel.Camera && period > threshold)
        warnings.Add(
          $"The camera times each frame to about one camera period ({period / (double)TimeSpan.TicksPerMillisecond:0.##} ms), more than the "
            + $"{threshold / (double)TimeSpan.TicksPerMillisecond:0.###} ms error threshold: smaller animation errors are measurement noise. "
            + $"--error-threshold-ms {period / (double)TimeSpan.TicksPerMillisecond:0.##} counts only the errors the camera resolves."
        );

      var runRows = SplitIntoRuns(rows, warnings);
      var runs = new List<RunAnalysis>();
      foreach (var run in runRows)
      {
        if (options.RunId.HasValue && run.RunId != options.RunId.Value)
          continue;
        runs.Add(AnalyzeRun(run, period, threshold, options));
      }
      if (runs.Count == 0)
        warnings.Add(options.RunId.HasValue ? $"Run {options.RunId} was not found in the capture" : "No frame markers were found in the capture");
      return new TimelineResult(period, threshold, runs, warnings);
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
      public string? SequenceId;
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
                  SequenceId = row.Start?.SequenceText,
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
      public long IntendedDisplayTicks;
      public uint TargetFrameTicks;
      public long CpuStartTicks;
      public uint CpuBusyTicks;
      public uint PreferredFrameTicks;
      public bool StaticAfter;
      public bool StaticBefore;
      public long FirstCaptureIndex;
      public long FirstSeenTicks;
      public long LastSeenTicks;
      public int CaptureCount;
      public ulong SkippedBefore;
      public bool UncertainStart;
      public int GapCaptures;
      public long? FirstSeenSecondaryTicks;
      public bool Torn;
      public List<OlderFrameCapture>? OlderFrames;
    }

    private static RunAnalysis AnalyzeRun(RunRows run, long period, long threshold, TimelineOptions options)
    {
      var warnings = new List<string>(run.Warnings);
      long decoded = 0;
      long undecodable = 0;
      long torn = 0;
      long notRecorded = 0;
      long sourceDrops = 0;
      long missed = 0;
      long outOfOrder = 0;

      // Trailing undecodable rows after the last frame belong to the transition to the end marker; ignore them.
      var rows = run.Rows;
      int lastDecoded = rows.FindLastIndex(r => r.IsDecoded);
      var builders = new List<FrameBuilder>();
      FrameBuilder? current = null;
      int segment = 0;
      bool gapSinceLastFrame = false;
      int gapCaptures = 0;
      bool camera = options.Scanout == ScanoutModel.Camera;
      var firstSecondary = new Dictionary<ulong, long>();

      for (int i = 0; i <= lastDecoded; ++i)
      {
        var row = rows[i];
        // The source dropped captures before this one, or the capture's clock says refreshes were missed: a gap in the capture, as a
        // capture it could not record
        if (row.SourceDrops > 0 || row.MissedBefore > 0)
        {
          sourceDrops += row.SourceDrops;
          missed += row.MissedBefore;
          gapSinceLastFrame = true;
        }
        // The second zone may still show the previous run's frames: only this run's frame indices count
        if (camera && row.Sync is { } secondary && secondary.RunId == run.RunId && row.Status != CaptureStatus.NotRecorded)
          firstSecondary.TryAdd(secondary.FrameIndex, row.CaptureTicks);
        switch (row.Status)
        {
          case CaptureStatus.Undecodable:
            ++undecodable;
            gapSinceLastFrame = true;
            ++gapCaptures;
            continue;
          case CaptureStatus.Torn:
            ++torn;
            gapSinceLastFrame = true;
            ++gapCaptures;
            continue;
          case CaptureStatus.NotRecorded:
            ++notRecorded;
            gapSinceLastFrame = true;
            ++gapCaptures;
            continue;
        }
        ++decoded;
        var payload = row.Payload;
        if (current != null && payload.FrameIndex == current.FrameIndex)
        {
          current.LastSeenTicks = row.CaptureTicks;
          current.CaptureCount++;
          gapSinceLastFrame = false;
          gapCaptures = 0;
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
              // An older frame shown again: kept with the newest frame, which stays the one presented
              ++outOfOrder;
              (current.OlderFrames ??= new List<OlderFrameCapture>()).Add(new OlderFrameCapture(row.CaptureTicks, payload.FrameIndex));
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
          IntendedDisplayTicks = payload.IntendedDisplayTicks,
          TargetFrameTicks = payload.TargetFrameTicks,
          CpuStartTicks = payload.CpuStartTicks,
          CpuBusyTicks = payload.CpuBusyTicks,
          PreferredFrameTicks = payload.PreferredFrameTicks,
          StaticAfter = payload.IsStaticAfter,
          StaticBefore = payload.IsStaticBefore,
          FirstCaptureIndex = row.CaptureIndex,
          FirstSeenTicks = row.CaptureTicks,
          LastSeenTicks = row.CaptureTicks,
          CaptureCount = 1,
          SkippedBefore = builders.Count > 0 && builders[^1].Segment == segment ? skipped : 0,
          UncertainStart = gapSinceLastFrame,
          GapCaptures = gapCaptures,
        };
        gapSinceLastFrame = false;
        gapCaptures = 0;
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

      var cameraStatistics = camera ? AnalyzeCamera(builders, firstSecondary, period, warnings) : null;
      if (cameraStatistics != null)
        TimeBySyncMarker(builders, cameraStatistics);
      var frames = BuildFrames(builders, period, camera);
      var pacing = AnalyzePacing(frames, period, threshold, camera, options, warnings);
      var statistics = RunStatistics.From(frames, threshold, period);
      var counts = new RunCounts(
        rows.Count,
        decoded,
        undecodable,
        torn,
        notRecorded,
        sourceDrops,
        missed,
        frames.Count,
        frames.Aggregate(0L, (sum, f) => sum + (long)f.SkippedBefore),
        DroppedFrames.Before(frames).Sum(),
        outOfOrder,
        frames.Count > 0 ? frames[^1].Segment + 1 : 0
      );
      if (period <= 0)
        warnings.Add("The capture period could not be determined");
      if (SlowCaptureWarning(frames, period) is { } slowCapture)
        warnings.Add(slowCapture);
      return new RunAnalysis(
        run.RunId,
        run.SequenceId,
        run.StartTimeUtc,
        run.HasStart,
        run.HasEnd,
        counts,
        statistics,
        frames,
        warnings,
        cameraStatistics,
        pacing
      );
    }

    /// <summary>
    /// EXPERIMENTAL camera captures. The scanout reaches the second (lower) zone a roughly constant time after the timing zone. A frame that
    /// reaches the second zone clearly earlier than that was presented while the scanout was between the zones (vsync off): the camera saw a
    /// tear. Frame indices only the second zone shows were replaced before the next scanout reached the timing zone. Frames normally follow a
    /// few undecodable captures (the scanout crossing the marker, the panel switching), so a start only counts as uncertain when the gap is
    /// clearly longer than usual.
    /// </summary>
    private static CameraRunStatistics AnalyzeCamera(
      List<FrameBuilder> builders,
      Dictionary<ulong, long> firstSecondary,
      long period,
      List<string> warnings
    )
    {
      var delays = new List<long>();
      foreach (var builder in builders)
      {
        if (firstSecondary.TryGetValue(builder.FrameIndex, out long secondary))
        {
          builder.FirstSeenSecondaryTicks = secondary;
          delays.Add(secondary - builder.FirstSeenTicks);
        }
      }

      long typical = Median(delays);
      long tornFrames = 0;
      if (delays.Count >= 3 && period > 0 && typical > 4 * period)
      {
        long margin = Math.Max(3 * period, typical / 2);
        foreach (var builder in builders)
        {
          if (builder.FirstSeenSecondaryTicks is { } secondary && secondary - builder.FirstSeenTicks < typical - margin)
          {
            builder.Torn = true;
            ++tornFrames;
          }
        }
      }
      else if (delays.Count >= 3)
      {
        warnings.Add(
          "Camera capture: the scanout reaches both marker zones less than 4 camera frames apart, so tears between them can not be told apart. "
            + "Film at a higher frame rate or keep the zones further apart."
        );
      }

      long secondZoneOnly = 0;
      if (builders.Count > 1)
      {
        var seen = builders.Select(b => b.FrameIndex).ToHashSet();
        ulong first = builders.Min(b => b.FrameIndex);
        ulong last = builders.Max(b => b.FrameIndex);
        secondZoneOnly = firstSecondary.Keys.LongCount(k => k > first && k < last && !seen.Contains(k));
      }

      var gaps = builders.Skip(1).Select(b => (long)b.GapCaptures).ToList();
      long usualGap = Median(gaps);
      foreach (var builder in builders)
        builder.UncertainStart = builder.GapCaptures > usualGap + 2;

      var normal = builders
        .Where(b => b.FirstSeenSecondaryTicks.HasValue && !b.Torn)
        .Select(b => b.FirstSeenSecondaryTicks!.Value - b.FirstSeenTicks);
      return new CameraRunStatistics(Statistics.FromTicks(normal), delays.Count, tornFrames, secondZoneOnly);
    }

    /// <summary>
    /// EXPERIMENTAL camera captures: the sync marker times the frames. The scanout crosses the small marker quickly, so the first camera
    /// frame that shows it complete is sharp; the main marker is taller and only identifies the frame (its payload). A frame the sync marker
    /// missed is timed by its main marker plus the usual scanout delay. Afterwards <see cref="FrameBuilder.FirstSeenSecondaryTicks"/> holds
    /// the main marker's first-seen time.
    /// </summary>
    private static void TimeBySyncMarker(List<FrameBuilder> builders, CameraRunStatistics camera)
    {
      long typicalDelay = (long)Math.Round(camera.ScanoutDelay.P50 * TimeSpan.TicksPerMillisecond);
      foreach (var builder in builders)
      {
        long main = builder.FirstSeenTicks;
        long sync = builder.FirstSeenSecondaryTicks ?? (main + typicalDelay);
        builder.LastSeenTicks += sync - main;
        builder.FirstSeenTicks = sync;
        builder.FirstSeenSecondaryTicks = main;
      }
    }

    /// <summary>
    /// Late frames and the pacing verdict. A capture card captures at the display's refresh rate, so the refresh period is the capture period;
    /// an EXPERIMENTAL camera films faster, so the refresh is calculated from when it first saw each frame.
    /// </summary>
    private static RunPacing? AnalyzePacing(
      List<PresentedFrame> frames,
      long period,
      long threshold,
      bool camera,
      TimelineOptions options,
      List<string> warnings
    )
    {
      if (period <= 0)
        return null;
      double? expected = options.ExpectedRefreshHz is > 0 ? options.ExpectedRefreshHz : null;
      long refresh = period;
      if (camera)
      {
        // The user's expected rate settles an ambiguous estimate (a steady game below the refresh rate) before the rig's calibration
        double? hintHz = expected ?? (options.CalibratedRefreshHz is > 0 ? options.CalibratedRefreshHz : null);
        double? hint = hintHz is { } hz ? TimeSpan.TicksPerSecond / hz : null;
        if (RefreshEstimator.EstimatePeriodTicks(CameraIntervals(frames), period, hint) is not { } estimate)
        {
          warnings.Add(
            "Camera capture: the display's refresh rate could not be calculated from the frames (too few, or the camera is too slow), so late frames are not marked."
          );
          return null;
        }
        refresh = (long)Math.Round(estimate);
        double calculatedHz = TimeSpan.TicksPerSecond / estimate;
        if (expected is { } expectedHz && !WithinTolerance(calculatedHz, expectedHz))
          warnings.Add(
            string.Create(
              CultureInfo.InvariantCulture,
              $"Camera capture: the frames give a {calculatedHz:0.##} Hz display, but {expectedHz:0.##} Hz was expected. Check the display's refresh rate (or the camera's frame rate, --recorded-fps for slow motion clips)."
            )
          );
        else if (expected == null && options.CalibratedRefreshHz is > 0 && !WithinTolerance(calculatedHz, options.CalibratedRefreshHz.Value))
          warnings.Add(
            string.Create(
              CultureInfo.InvariantCulture,
              $"Camera capture: the frames give a {calculatedHz:0.#} Hz display, the camera rig measured {options.CalibratedRefreshHz:0.#} Hz at calibration. Was the refresh rate changed?"
            )
          );
      }
      else if (expected is { } expectedHz && !WithinTolerance(TimeSpan.TicksPerSecond / (double)period, expectedHz))
      {
        warnings.Add(
          string.Create(
            CultureInfo.InvariantCulture,
            $"The capture runs at {TimeSpan.TicksPerSecond / (double)period:0.##} fps, but a {expectedHz:0.##} Hz display was expected. A capture card must capture at the display's refresh rate: check the card's mode and the display's refresh rate."
          )
        );
      }
      // A camera's animation error is the difference of two first-seen times, each good to about one camera period: the verdict only
      // attributes errors beyond that noise
      return PacingAnalyzer.Analyze(frames, refresh, camera, options.TargetFps, camera ? 2 * threshold : threshold) with
      {
        ExpectedRefreshHz = expected,
      };
    }

    private static bool WithinTolerance(double actualHz, double expectedHz) => Math.Abs((actualHz / expectedHz) - 1) <= RefreshTolerance;

    /// <summary>First-seen intervals a camera measured reliably: not across a tear or an uncertain start.</summary>
    private static IEnumerable<double> CameraIntervals(List<PresentedFrame> frames)
    {
      const PresentedFrameFlags Unreliable = PresentedFrameFlags.Torn | PresentedFrameFlags.UncertainStart;
      for (int i = 1; i < frames.Count; ++i)
      {
        if (frames[i].DisplayDeltaTicks is { } delta && (frames[i].Flags & Unreliable) == 0 && (frames[i - 1].Flags & PresentedFrameFlags.Torn) == 0)
          yield return delta;
      }
    }

    private static long Median(List<long> values)
    {
      if (values.Count == 0)
        return 0;
      var sorted = values.OrderBy(v => v).ToList();
      return sorted[sorted.Count / 2];
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

    private static List<PresentedFrame> BuildFrames(List<FrameBuilder> builders, long period, bool camera)
    {
      var frames = new List<PresentedFrame>(builders.Count);
      // Static is a frame's time on screen, said by the frame itself (StaticAfter) or, when the application only knew it one frame later,
      // by the next frame (StaticBefore). The next frame speaks only for the frame index before it: a frame never shown has no time on screen
      for (int i = 1; i < builders.Count; ++i)
      {
        if (builders[i].StaticBefore && builders[i - 1].Segment == builders[i].Segment && builders[i - 1].FrameIndex + 1 == builders[i].FrameIndex)
          builders[i - 1].StaticAfter = true;
      }
      int segmentStart = 0;
      long drift = 0;
      for (int i = 0; i < builders.Count; ++i)
      {
        var b = builders[i];
        if (i > 0 && b.Segment != builders[i - 1].Segment)
        {
          segmentStart = i;
          drift = 0;
        }
        bool hasPrevious = i > segmentStart;
        var previous = hasPrevious ? builders[i - 1] : null;
        bool hasNext = i + 1 < builders.Count && builders[i + 1].Segment == b.Segment;

        long? displayDelta = previous != null ? b.FirstSeenTicks - previous.FirstSeenTicks : null;
        long? animationDelta = previous != null ? b.AnimationTicks - previous.AnimationTicks : null;
        // A step whose start or end was first seen after a capture gap (captures not decoded, not recorded, or dropped by the source) has
        // an uncertain display time: the frame may have appeared in the refresh the capture missed. A capture card's gaps only; a camera
        // decides uncertain starts itself (AnalyzeCamera), where gaps are part of every scanout
        bool uncertainStep = !camera && previous != null && (b.UncertainStart || previous.UncertainStart);
        // A step from a static frame has no motion to be off (an animation clock may pause while nothing animates): not judged, and the drift
        // adds up only the judged errors (without static steps that is animation time minus display time since the segment began). The step
        // into a static frame is judged: that frame may still have moved (it often reaches the rest pose)
        bool staticStep = previous is { StaticAfter: true };
        long? error = displayDelta.HasValue && !staticStep && !uncertainStep ? animationDelta!.Value - displayDelta.Value : null;
        drift += error ?? 0;
        long onScreen = hasNext ? builders[i + 1].FirstSeenTicks - b.FirstSeenTicks : b.LastSeenTicks - b.FirstSeenTicks + period;
        // The frametime reaches to the next frame's CPU start: only known when the next frame index was captured
        long? frameTime =
          i + 1 < builders.Count && builders[i + 1].FrameIndex == b.FrameIndex + 1 && b.CpuStartTicks != 0 && builders[i + 1].CpuStartTicks != 0
            ? builders[i + 1].CpuStartTicks - b.CpuStartTicks
            : null;
        long? cpuWait = frameTime.HasValue && b.CpuBusyTicks != 0 ? frameTime.Value - b.CpuBusyTicks : null;
        var flags = PresentedFrameFlags.None;
        if (b.SkippedBefore > 0)
          flags |= PresentedFrameFlags.SkippedBefore;
        if (b.UncertainStart && hasPrevious)
          flags |= PresentedFrameFlags.UncertainStart;
        if (b.Torn)
          flags |= PresentedFrameFlags.Torn;
        if (b.StaticAfter)
          flags |= PresentedFrameFlags.StaticAfter;
        if (staticStep)
          flags |= PresentedFrameFlags.StaticBefore;
        if (uncertainStep)
          flags |= PresentedFrameFlags.UncertainStep;

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
            error,
            drift,
            flags,
            b.FirstSeenSecondaryTicks,
            b.IntendedDisplayTicks,
            b.TargetFrameTicks,
            CpuStartTicks: b.CpuStartTicks,
            CpuBusyTicks: b.CpuBusyTicks,
            FrameTimeTicks: frameTime,
            CpuWaitTicks: cpuWait,
            MarkerPreferredFrameTicks: b.PreferredFrameTicks,
            OlderFrames: b.OlderFrames
          )
        );
      }
      return frames;
    }
  }
}
