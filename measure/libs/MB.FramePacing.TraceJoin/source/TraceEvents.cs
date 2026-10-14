//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A capture's measurements as the events of the tracks of a trace: what is added to a Perfetto trace, as plain values with nothing of
//* Perfetto in it (TraceTrackWriter writes them). A port of tools/trace_events.py: the same events, names and values, in the same order.
//*
//* The names say what the capture's data says and no more: a frame index that was skipped is not called dropped.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MB.FramePacing.Data;
using MB.FramePacing.Trace;

namespace MB.FramePacing.TraceJoin
{
  /// <summary>A run's frames and its capture's rows as the events of each <see cref="TraceTrack"/>.</summary>
  public static class TraceEvents
  {
    private const double NanosecondsPerMillisecond = 1_000_000.0;

    /// <summary>The name of a refresh whose capture has an even index: two names in turn, so that the viewer colours them in turn.</summary>
    public const string RefreshA = "refresh a";

    /// <summary>The name of a refresh whose capture has an odd index.</summary>
    public const string RefreshB = "refresh b";

    /// <summary>The name of a refresh whose capture was not decoded.</summary>
    public const string NotDecodedRefresh = "refresh, not decoded";

    /// <summary>One track's events of a run, in time order.</summary>
    public static IReadOnlyList<TraceEvent> Of(TraceTrack track, TraceRun run, TraceAlignment alignment) =>
      Of(track, run.RunId, run.Frames, run.Captures, alignment, run.PeriodNs, run.CapturePeriodNs, run.ErrorThresholdNs);

    /// <summary>
    /// One track's events, in time order. The refreshes are a capture card's captures only, whose period is the refresh period (a camera
    /// captures faster: its captures are not refreshes).
    /// </summary>
    public static IReadOnlyList<TraceEvent> Of(
      TraceTrack track,
      uint runId,
      IReadOnlyList<FrameRow> frames,
      IReadOnlyList<CaptureCsvRow> captures,
      TraceAlignment alignment,
      long periodNs,
      long capturePeriodNs,
      long errorThresholdNs
    ) =>
      track switch
      {
        TraceTrack.Refreshes => capturePeriodNs == periodNs ? Refreshes(captures, alignment, CaptureRangeOf(frames), periodNs) : [],
        TraceTrack.OnScreen => OnScreen(frames, runId, alignment, periodNs, errorThresholdNs),
        TraceTrack.Intended => Intended(frames, runId, alignment),
        TraceTrack.Cpu => Cpu(frames, runId, alignment),
        TraceTrack.AnimationError or TraceTrack.DisplayStep or TraceTrack.Lateness => Counters(track, frames, alignment),
        TraceTrack.FrameEvents => FrameEvents(frames, alignment),
        TraceTrack.CaptureEvents => CaptureEvents(captures, alignment, CaptureRangeOf(frames)),
        _ => throw new ArgumentOutOfRangeException(nameof(track)),
      };

    /// <summary>Nanoseconds as milliseconds: one division, so a whole number of nanoseconds gives the same double everywhere.</summary>
    public static double Milliseconds(long nanoseconds) => nanoseconds / NanosecondsPerMillisecond;

    public static string FrameName(ulong frameIndex) => "#" + frameIndex.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The name of a slice of a frame: the name the application's frame record gives it (mbfp frame &lt;run id&gt; &lt;frame index&gt;). The viewer
    /// highlights every slice of the name that is hovered or clicked, so a frame's own slice and the tools' slices of it go together.
    /// </summary>
    public static string RecordName(uint runId, ulong frameIndex) => FrameRecordName.Format(runId, frameIndex);

    /// <summary>
    /// A slice per frame for its time on screen, cut where the next frame's begins (one track holds no two slices at once). The slice's flow
    /// number is the frame's index plus one, as its CPU slice's is.
    /// </summary>
    /// <summary>
    /// What the analysis says of a frame, as one word for its colour, the first that fits: late, delta time jitter, off (an animation error
    /// beyond <paramref name="errorThresholdNs"/> otherwise), static (nothing animates while it is on screen), unknown (a step across a
    /// capture gap), or on time.
    /// </summary>
    public static string VerdictOf(FrameRow frame, long errorThresholdNs)
    {
      if (frame.Flags.Contains("Late"))
        return "late";
      if (frame.Flags.Contains("DeltaTimeJitter"))
        return "delta time jitter";
      if (frame.AnimationError is { } error && Math.Abs(error.Nanoseconds) > errorThresholdNs)
        return "off";
      if (frame.Flags.Contains("StaticAfter"))
        return "static";
      return frame.Flags.Contains("UncertainStep") ? "unknown" : "on time";
    }

    public static IReadOnlyList<TraceEvent> OnScreen(
      IReadOnlyList<FrameRow> frames,
      uint runId,
      TraceAlignment alignment,
      long periodNs,
      long errorThresholdNs
    )
    {
      var slices = new List<TraceEvent>(frames.Count);
      for (int index = 0; index < frames.Count; ++index)
      {
        var frame = frames[index];
        long start = alignment.DisplayTime(frame.FirstSeenNs());
        long end = start + Math.Max(frame.OnScreenNs(), 0);
        if (index + 1 < frames.Count)
          end = Math.Min(end, alignment.DisplayTime(frames[index + 1].FirstSeenNs()));
        slices.Add(
          TraceEvent.Slice(
            TraceTrack.OnScreen,
            start,
            Math.Max(end, start),
            RecordName(runId, frame.FrameIndex),
            FrameArguments(frame, periodNs),
            frame.HasCpuWork() ? frame.FrameIndex + 1 : 0,
            VerdictOf(frame, errorThresholdNs)
          )
        );
      }
      return slices;
    }

    /// <summary>
    /// A slice per frame from its intended display time to the next frame's that is not before it. The last frame's is as long as its
    /// target frame time, or has no length. A frame whose intended display time is before the one before it (a clock that went back, a
    /// marker that is wrong) gets none: a track is in time order.
    /// </summary>
    public static IReadOnlyList<TraceEvent> Intended(IReadOnlyList<FrameRow> frames, uint runId, TraceAlignment alignment)
    {
      var scheduled = frames.Where(f => f.IntendedDisplayNs() != null).ToList();
      var slices = new List<TraceEvent>(scheduled.Count);
      long? lastEnd = null;
      for (int index = 0; index < scheduled.Count; ++index)
      {
        var frame = scheduled[index];
        long start = alignment.MarkerTime(frame.IntendedDisplayNs()!.Value);
        if (lastEnd is { } before && start < before)
          continue;
        long end = start + Math.Max(frame.TargetFrameTime?.Nanoseconds ?? 0, 0);
        for (int later = index + 1; later < scheduled.Count; ++later)
        {
          long following = alignment.MarkerTime(scheduled[later].IntendedDisplayNs()!.Value);
          if (following >= start)
          {
            end = following;
            break;
          }
        }
        lastEnd = end;
        slices.Add(TraceEvent.Slice(TraceTrack.Intended, start, end, RecordName(runId, frame.FrameIndex), new[] { FrameIndexArgument(frame) }));
      }
      return slices;
    }

    /// <summary>
    /// A slice per frame from its CPU start time for its CPU busy time, cut where the next frame's begins. A frame whose CPU start is before
    /// the one before it (a clock that went back, a marker that is wrong) gets none: a track is in time order.
    /// </summary>
    public static IReadOnlyList<TraceEvent> Cpu(IReadOnlyList<FrameRow> frames, uint runId, TraceAlignment alignment)
    {
      var worked = frames.Where(f => f.HasCpuWork()).ToList();
      var slices = new List<TraceEvent>(worked.Count);
      long? lastEnd = null;
      for (int index = 0; index < worked.Count; ++index)
      {
        var frame = worked[index];
        long busy = frame.CpuBusyNs()!.Value;
        long start = alignment.MarkerTime(frame.CpuStartNs()!.Value);
        if (lastEnd is { } before && start < before)
          continue;
        long end = start + busy;
        for (int later = index + 1; later < worked.Count; ++later)
        {
          long following = alignment.MarkerTime(worked[later].CpuStartNs()!.Value);
          if (following >= start)
          {
            end = Math.Min(end, following);
            break;
          }
        }
        lastEnd = end;
        var arguments = new[] { FrameIndexArgument(frame), TraceArgument.Of("CPU busy (ms)", Milliseconds(busy)) };
        slices.Add(TraceEvent.Slice(TraceTrack.Cpu, start, end, RecordName(runId, frame.FrameIndex), arguments, frame.FrameIndex + 1));
      }
      return slices;
    }

    /// <summary>One of the three per-frame values, each at its frame's display time.</summary>
    public static IReadOnlyList<TraceEvent> Counters(TraceTrack track, IReadOnlyList<FrameRow> frames, TraceAlignment alignment)
    {
      var values = new List<TraceEvent>(frames.Count);
      foreach (var frame in frames)
      {
        long? value = track switch
        {
          TraceTrack.AnimationError => frame.AnimationError?.Nanoseconds,
          TraceTrack.DisplayStep => frame.DisplayDelta?.Nanoseconds,
          TraceTrack.Lateness => frame.LatenessNs(),
          _ => throw new ArgumentOutOfRangeException(nameof(track)),
        };
        if (value is { } known)
          values.Add(TraceEvent.Counter(track, alignment.DisplayTime(frame.FirstSeenNs()), Milliseconds(known)));
      }
      return values;
    }

    /// <summary>What the capture found about the frames, in time order. A frame out of order is at the capture that showed it.</summary>
    public static IReadOnlyList<TraceEvent> FrameEvents(IReadOnlyList<FrameRow> frames, TraceAlignment alignment)
    {
      var events = new List<TraceEvent>();
      foreach (var frame in frames)
      {
        long time = alignment.DisplayTime(frame.FirstSeenNs());
        string name = FrameName(frame.FrameIndex);
        var indexArgument = new[] { FrameIndexArgument(frame) };
        if (frame.SkippedBefore > 0)
        {
          string plural = frame.SkippedBefore == 1 ? "index" : "indices";
          events.Add(
            TraceEvent.Instant(
              TraceTrack.FrameEvents,
              time,
              $"{frame.SkippedBefore.ToString(CultureInfo.InvariantCulture)} frame {plural} skipped before {name}",
              indexArgument
            )
          );
        }
        if (frame.Flags.Contains("Late"))
        {
          var lateArguments = frame.LatenessNs() is { } lateness
            ? new[] { FrameIndexArgument(frame), TraceArgument.Of("lateness (ms)", Milliseconds(lateness)) }
            : indexArgument;
          events.Add(TraceEvent.Instant(TraceTrack.FrameEvents, time, $"late {name}", lateArguments));
        }
        if (frame.Flags.Contains("Torn"))
          events.Add(TraceEvent.Instant(TraceTrack.FrameEvents, time, $"torn {name}", indexArgument));
        foreach (var older in frame.OlderFrames)
        {
          long olderTime = alignment.CaptureTime(older.CaptureTime.Nanoseconds) - alignment.VBlankShiftNs;
          events.Add(
            TraceEvent.Instant(
              TraceTrack.FrameEvents,
              olderTime,
              $"out of order: {FrameName(older.FrameIndex)} while {name} was the newest",
              indexArgument
            )
          );
        }
      }
      // A stable sort: events of one moment stay in the order they were found
      return events.OrderBy(e => e.StartNs).ToList();
    }

    /// <summary>
    /// The capture's own gaps, of the captures from <paramref name="captureRange"/>'s first time to its last on the capture's clock, in
    /// time order. A capture that was not recorded has no time of its own: it is counted at the next capture that has one.
    /// </summary>
    public static IReadOnlyList<TraceEvent> CaptureEvents(
      IReadOnlyList<CaptureCsvRow> captures,
      TraceAlignment alignment,
      (long FirstNs, long LastNs) captureRange
    )
    {
      var events = new List<TraceEvent>();
      long notRecorded = 0;
      foreach (var capture in captures)
      {
        if (capture.CaptureTime is not { } captureTime)
        {
          notRecorded += capture.CaptureStatus == "NotRecorded" ? 1 : 0;
          continue;
        }
        long captureNs = captureTime.Nanoseconds;
        if (captureNs < captureRange.FirstNs || captureNs > captureRange.LastNs)
        {
          notRecorded = 0;
          continue;
        }
        long time = alignment.CaptureTime(captureNs) - alignment.VBlankShiftNs;
        var arguments = new[] { TraceArgument.Of("capture index", capture.CaptureIndex) };
        if (notRecorded > 0)
          events.Add(TraceEvent.Instant(TraceTrack.CaptureEvents, time, Invariant($"not recorded: {notRecorded} before this capture"), arguments));
        notRecorded = 0;
        if (capture.SourceDropsBefore > 0)
          events.Add(
            TraceEvent.Instant(
              TraceTrack.CaptureEvents,
              time,
              Invariant($"dropped by the source: {capture.SourceDropsBefore} before this capture"),
              arguments
            )
          );
        if (capture.MissedBefore > 0)
          events.Add(TraceEvent.Instant(TraceTrack.CaptureEvents, time, Invariant($"missed: {capture.MissedBefore} before this capture"), arguments));
        if (capture.CaptureStatus == "Undecodable")
          events.Add(TraceEvent.Instant(TraceTrack.CaptureEvents, time, "not decoded", arguments));
      }
      return events.OrderBy(e => e.StartNs).ToList();
    }

    /// <summary>
    /// A slice per capture from <paramref name="captureRange"/>'s first time to its last on the capture's clock: a capture card captures once
    /// per refresh, so each is a refresh, a period long and cut where the next capture begins. Named a and b by the capture's index, so the
    /// viewer colours them in turn (a capture not decoded is named so); a capture that was not recorded or was missed has none, and shows as
    /// a gap. A capture before the end of the one before it (a clock that went back) gets none: a track is in time order.
    /// </summary>
    public static IReadOnlyList<TraceEvent> Refreshes(
      IReadOnlyList<CaptureCsvRow> captures,
      TraceAlignment alignment,
      (long FirstNs, long LastNs) captureRange,
      long periodNs
    )
    {
      var timed = captures
        .Where(c => c.CaptureTime is { } time && time.Nanoseconds >= captureRange.FirstNs && time.Nanoseconds <= captureRange.LastNs)
        .ToList();
      long StartOf(CaptureCsvRow capture) => alignment.CaptureTime(capture.CaptureTime!.Value.Nanoseconds) - alignment.VBlankShiftNs;
      var slices = new List<TraceEvent>(timed.Count);
      long? lastEnd = null;
      for (int index = 0; index < timed.Count; ++index)
      {
        var capture = timed[index];
        long start = StartOf(capture);
        if (lastEnd is { } before && start < before)
          continue;
        long end = start + Math.Max(periodNs, 0);
        for (int later = index + 1; later < timed.Count; ++later)
        {
          long following = StartOf(timed[later]);
          if (following >= start)
          {
            end = Math.Min(end, following);
            break;
          }
        }
        lastEnd = end;
        string name =
          capture.CaptureStatus == "Undecodable" ? NotDecodedRefresh
          : capture.CaptureIndex % 2 == 0 ? RefreshA
          : RefreshB;
        slices.Add(TraceEvent.Slice(TraceTrack.Refreshes, start, end, name, new[] { TraceArgument.Of("capture index", capture.CaptureIndex) }));
      }
      return slices;
    }

    /// <summary>From the first frame's display time to the end of the last frame's time on screen, on the capture's clock.</summary>
    public static (long FirstNs, long LastNs) CaptureRangeOf(IReadOnlyList<FrameRow> frames)
    {
      if (frames.Count == 0)
        return (0, -1);
      return (frames.Min(f => f.FirstSeenNs()), frames.Max(f => f.FirstSeenNs() + Math.Max(f.OnScreenNs(), 0)));
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static TraceArgument FrameIndexArgument(FrameRow frame) => TraceArgument.Of("frame index", unchecked((long)frame.FrameIndex));

    private static List<TraceArgument> FrameArguments(FrameRow frame, long periodNs)
    {
      var arguments = new List<TraceArgument>
      {
        FrameIndexArgument(frame),
        TraceArgument.Of("on screen (ms)", Milliseconds(frame.OnScreenNs())),
        TraceArgument.Of("captures", frame.Captures),
      };
      if (frame.DisplayDelta is { } displayDelta)
      {
        arguments.Add(TraceArgument.Of("display time step (ms)", Milliseconds(displayDelta.Nanoseconds)));
        if (periodNs > 0)
          arguments.Add(TraceArgument.Of("display time step (refreshes)", FloorDivide(displayDelta.Nanoseconds + (periodNs / 2), periodNs)));
      }
      if (frame.AnimationDelta is { } animationDelta)
        arguments.Add(TraceArgument.Of("animation time step (ms)", Milliseconds(animationDelta.Nanoseconds)));
      if (frame.AnimationError is { } animationError)
        arguments.Add(TraceArgument.Of("animation error (ms)", Milliseconds(animationError.Nanoseconds)));
      if (frame.LatenessNs() is { } lateness)
        arguments.Add(TraceArgument.Of("lateness (ms)", Milliseconds(lateness)));
      if (frame.TargetFrameTime is { } target)
        arguments.Add(TraceArgument.Of("target frame time (ms)", Milliseconds(target.Nanoseconds)));
      if (frame.SkippedBefore > 0)
        arguments.Add(TraceArgument.Of("frame indices skipped before", unchecked((long)frame.SkippedBefore)));
      if (frame.Flags.Count > 0)
        arguments.Add(TraceArgument.Of("flags", string.Join(", ", frame.Flags)));
      return arguments;
    }

    /// <summary>The quotient rounded down, also for a negative value (the divisor is more than zero).</summary>
    private static long FloorDivide(long value, long divisor)
    {
      long quotient = value / divisor;
      return value % divisor < 0 ? quotient - 1 : quotient;
    }
  }
}
