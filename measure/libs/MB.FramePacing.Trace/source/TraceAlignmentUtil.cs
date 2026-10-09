//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Where a capture's frames are on the clocks of a Perfetto trace of the same run: the clock arithmetic of the join, with nothing of
//* Perfetto in it. A port of tools/trace_alignment.py, which says what each step can and can not claim; the two give the same numbers
//* (measure/test-data/trace).
//*
//* Three clocks meet. The capture's: a frame's display time. The application's steady clock, which the markers carry the CPU start time
//* and the pacer's intended display time on. And the trace's, which ties the system's clocks together with its own clock snapshots.
//* Nothing ties the capture's clock to the trace's but the frames: a delay the whole run shares can not be told from the clocks'
//* difference. Every result comes from integer arithmetic.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using MB.FramePacing.Data;

namespace MB.FramePacing.Trace
{
  /// <summary>Experimental. The clock arithmetic that places a capture's frames on a trace's clock.</summary>
  public static class TraceAlignmentUtil
  {
    /// <summary>How many display times need a vertical blank within a refresh before them for the vertical blanks to be used.</summary>
    public const int MinVBlankFrames = 8;

    /// <summary>
    /// The capture's clock minus the application's, from the frames of one run. With a schedule it is the analysis's own number, which every
    /// frame that has an intended display time and a lateness gives alike. Without one it is the smallest of display time minus the end of
    /// the CPU's work, so that no frame is on screen before its work ended. Without either the markers carry no times: the offset is zero
    /// and the anchor says so.
    /// </summary>
    public static (TraceAnchor Anchor, long OffsetNs) CaptureMinusApp(IReadOnlyList<FrameRow> frames)
    {
      long? offset = null;
      bool scheduled = false;
      foreach (var frame in frames)
      {
        if (frame.IntendedDisplayNs() is not { } intended || frame.LatenessNs() is not { } lateness)
          continue;
        scheduled = true;
        long own = frame.FirstSeenNs() - intended - lateness;
        if (offset is { } known && known != own)
        {
          int values = frames
            .Where(f => f.IntendedDisplayNs() != null && f.LatenessNs() != null)
            .Select(f => f.FirstSeenNs() - f.IntendedDisplayNs()!.Value - f.LatenessNs()!.Value)
            .Distinct()
            .Count();
          throw new TraceException(
            "The frames of this run do not share one offset between the capture's clock and the pacer's "
              + $"(firstSeenNs - intendedDisplayNs - latenessNs has {values} values): analyse the capture again."
          );
        }
        offset = own;
      }
      if (scheduled)
        return (TraceAnchor.Schedule, offset!.Value);

      long? smallest = null;
      foreach (var frame in frames)
      {
        if (frame.CpuStartNs() is not { } start || frame.CpuBusyNs() is not { } busy)
          continue;
        long own = frame.FirstSeenNs() - (start + busy);
        smallest = smallest is { } known ? Math.Min(known, own) : own;
      }
      return smallest is { } found ? (TraceAnchor.CpuWork, found) : (TraceAnchor.Nothing, 0);
    }

    /// <summary>The alignment of a run's frames, before any vertical blanks: by the offset that was given, or else by the frames.</summary>
    public static TraceAlignment Align(IReadOnlyList<FrameRow> frames, long appClockOffsetNs = 0, long? givenCaptureMinusAppNs = null)
    {
      if (givenCaptureMinusAppNs is { } given)
        return new TraceAlignment(TraceAnchor.Given, given, appClockOffsetNs);
      var (anchor, offset) = CaptureMinusApp(frames);
      return new TraceAlignment(anchor, offset, appClockOffsetNs);
    }

    /// <summary>
    /// A time on a system clock, on the trace's clock: by the newest snapshot at or before it (the first one for a time before them all), as
    /// the trace's own reader converts. <paramref name="snapshots"/> are ascending by their clock time. Without any, the system clock is
    /// taken to be the trace's clock.
    /// </summary>
    public static long ToTraceNs(long clockNs, IReadOnlyList<ClockSnapshot> snapshots)
    {
      if (snapshots.Count == 0)
        return clockNs;
      // The first snapshot after clockNs, less one
      int low = 0;
      int high = snapshots.Count;
      while (low < high)
      {
        int middle = low + ((high - low) / 2);
        if (snapshots[middle].ClockNs <= clockNs)
          low = middle + 1;
        else
          high = middle;
      }
      var snapshot = snapshots[Math.Max(low - 1, 0)];
      return snapshot.TraceNs + (clockNs - snapshot.ClockNs);
    }

    /// <summary>
    /// Fails when the markers' CPU start times, taken as times on the system clock, are not inside the trace: the application's clock is
    /// then not the clock that was named, or the trace is of another run. A trace may begin after the run did or end before it: at least
    /// half of the frames have to be inside.
    /// </summary>
    public static void CheckInsideTrace(
      IReadOnlyList<FrameRow> frames,
      TraceAlignment alignment,
      IReadOnlyList<ClockSnapshot> snapshots,
      long traceFirstNs,
      long traceLastNs
    )
    {
      var starts = new List<long>();
      foreach (var frame in frames)
      {
        if (frame.CpuStartNs() is { } start)
          starts.Add(ToTraceNs(alignment.MarkerTime(start), snapshots));
      }
      if (starts.Count == 0)
        return;
      int inside = starts.Count(start => traceFirstNs <= start && start <= traceLastNs);
      if (inside * 2L < starts.Count)
        throw new TraceException(
          $"Only {inside} of {starts.Count} CPU start times of the markers lie inside the trace "
            + $"(the markers have {starts.Min()} to {starts.Max()} ns on the trace's clock, the trace runs from {traceFirstNs} to {traceLastNs}). "
            + "Either the trace is of another run, or the application's clock is not the one named: "
            + "see the application clock and its offset."
        );
    }

    /// <summary>The one of <paramref name="sortedTimes"/> nearest to <paramref name="time"/> (the earlier of two that are as near).</summary>
    public static long? Nearest(IReadOnlyList<long> sortedTimes, long time)
    {
      if (sortedTimes.Count == 0)
        return null;
      int index = LowerBound(sortedTimes, time);
      if (index == 0)
        return sortedTimes[0];
      if (index == sortedTimes.Count)
        return sortedTimes[^1];
      long before = sortedTimes[index - 1];
      long after = sortedTimes[index];
      return time - before <= after - time ? before : after;
    }

    /// <summary>
    /// How far the display times are off the vertical blanks, as one part of a refresh for all of them. Both are on the same clock, the
    /// vertical blanks ascending. Each display time's distance past the vertical blank at or before it is a point on a circle of one
    /// refresh; the points of a run lie together, so the circle is cut in its widest empty stretch and the shift is the median of what is
    /// then a line. Null when fewer than <paramref name="minFrames"/> display times have a vertical blank within a refresh before them: the
    /// trace has no vertical blanks for the run.
    /// </summary>
    public static VBlankFit? FitVBlanks(IReadOnlyList<long> displayTimes, IReadOnlyList<long> vblanks, long periodNs, int minFrames = MinVBlankFrames)
    {
      if (periodNs <= 0)
        throw new TraceException($"The refresh period has to be more than zero nanoseconds: {periodNs}.");
      var phases = new List<long>();
      foreach (long time in displayTimes)
      {
        int index = UpperBound(vblanks, time) - 1;
        if (index >= 0 && time - vblanks[index] < periodNs)
          phases.Add(time - vblanks[index]);
      }
      if (phases.Count < minFrames)
        return null;
      phases.Sort();
      // The widest stretch of the circle that no point is in: between two neighbours, or across the end of the refresh
      int cut = 0;
      long widest = phases[0] + periodNs - phases[^1];
      for (int index = 1; index < phases.Count; ++index)
      {
        if (phases[index] - phases[index - 1] > widest)
        {
          widest = phases[index] - phases[index - 1];
          cut = index;
        }
      }
      // The line: the points from the cut on, then the ones before it a refresh later
      int middle = (phases.Count - 1) / 2;
      long onLine = middle < phases.Count - cut ? phases[cut + middle] : phases[middle - (phases.Count - cut)] + periodNs;
      long median = onLine % periodNs;
      // Past the middle of a refresh, the display times are a little before the next vertical blank, not long after this one
      long shift = median * 2 <= periodNs ? median : median - periodNs;

      long half = periodNs / 2;
      int used = 0;
      long spread = 0;
      foreach (long time in displayTimes)
      {
        if (Nearest(vblanks, time - shift) is { } blank && Math.Abs(time - shift - blank) <= half)
        {
          ++used;
          spread = Math.Max(spread, Math.Abs(time - shift - blank));
        }
      }
      return new VBlankFit(shift, used, spread);
    }

    /// <summary>The first index of <paramref name="sorted"/> whose value is not below <paramref name="value"/>.</summary>
    internal static int LowerBound(IReadOnlyList<long> sorted, long value)
    {
      int low = 0;
      int high = sorted.Count;
      while (low < high)
      {
        int middle = low + ((high - low) / 2);
        if (sorted[middle] < value)
          low = middle + 1;
        else
          high = middle;
      }
      return low;
    }

    /// <summary>The first index of <paramref name="sorted"/> whose value is above <paramref name="value"/>.</summary>
    internal static int UpperBound(IReadOnlyList<long> sorted, long value)
    {
      int low = 0;
      int high = sorted.Count;
      while (low < high)
      {
        int middle = low + ((high - low) / 2);
        if (sorted[middle] <= value)
          low = middle + 1;
        else
          high = middle;
      }
      return low;
    }
  }
}
