//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A run's events (RunEventKind) per kind, in time order, with running totals: any time range's events and their count in a few binary
//* searches, so the events lanes cost per pixel column, not per event. The frames' events come from the frames; the capture's from the capture
//* rows (ChartRun.Captures, captures.csv) from the run's first frame to its last sighting: without them the capture lane stays empty.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using MB.FramePacing.Analysis;
using MB.FramePacing.Data;

namespace MB.FramePacing.Charts
{
  public sealed class RunEvents
  {
    /// <summary>The kinds of the frames lane, the one that outweighs the others in a column first.</summary>
    public static readonly IReadOnlyList<RunEventKind> FrameKinds = new[] { RunEventKind.FramesDropped, RunEventKind.OutOfOrder, RunEventKind.Torn };

    /// <summary>The kinds of the capture lane, the one that outweighs the others in a column first.</summary>
    public static readonly IReadOnlyList<RunEventKind> CaptureKinds = new[]
    {
      RunEventKind.NotRecorded,
      RunEventKind.SourceDropped,
      RunEventKind.Missed,
      RunEventKind.NotDecoded,
    };

    private readonly RunEvent[][] m_events;
    private readonly long[][] m_totals;

    private RunEvents(List<RunEvent>[] events, bool capturesKnown)
    {
      m_events = events.Select(list => list.OrderBy(e => e.Ticks).ToArray()).ToArray();
      m_totals = m_events
        .Select(list =>
        {
          var totals = new long[list.Length + 1];
          for (int i = 0; i < list.Length; ++i)
            totals[i + 1] = totals[i] + list[i].Count;
          return totals;
        })
        .ToArray();
      CapturesKnown = capturesKnown;
    }

    /// <summary>The capture rows were given: the capture lane says what the capture missed (else it is unknown).</summary>
    public bool CapturesKnown { get; }

    /// <summary>The events of <paramref name="kind"/> from <paramref name="fromTicks"/> up to (not including) <paramref name="toTicks"/>.</summary>
    public ReadOnlySpan<RunEvent> In(RunEventKind kind, long fromTicks, long toTicks)
    {
      var (start, end) = Range(kind, fromTicks, toTicks);
      return m_events[(int)kind].AsSpan(start, end - start);
    }

    /// <summary>Whether <paramref name="kind"/> has an event from <paramref name="fromTicks"/> up to <paramref name="toTicks"/>.</summary>
    public bool Any(RunEventKind kind, long fromTicks, long toTicks)
    {
      var (start, end) = Range(kind, fromTicks, toTicks);
      return end > start;
    }

    /// <summary>How many frames or refreshes the events of <paramref name="kind"/> in the range stand for.</summary>
    public long Count(RunEventKind kind, long fromTicks, long toTicks)
    {
      var (start, end) = Range(kind, fromTicks, toTicks);
      return m_totals[(int)kind][end] - m_totals[(int)kind][start];
    }

    private (int Start, int End) Range(RunEventKind kind, long fromTicks, long toTicks)
    {
      var list = m_events[(int)kind];
      int start = RunChartData.FirstWhere(0, list.Length, i => list[i].Ticks >= fromTicks);
      int end = RunChartData.FirstWhere(start, list.Length, i => list[i].Ticks >= toTicks);
      return (start, end);
    }

    public static RunEvents Of(RunChartData data)
    {
      var chart = data.Run;
      var frames = data.Frames;
      long period = chart.CapturePeriodTicks;
      var events = Enum.GetValues<RunEventKind>().Select(_ => new List<RunEvent>()).ToArray();
      void Add(RunEventKind kind, RunEvent e) => events[(int)kind].Add(e);

      for (int i = 0; i < frames.Count; ++i)
      {
        var frame = frames[i];
        // Where the dropped frames were due: the refresh before the frame after them
        if (data.DroppedBeforeFrame[i] is > 0 and var dropped)
          Add(RunEventKind.FramesDropped, new RunEvent(frame.FirstSeenTicks - period, dropped, frame.FrameIndex));
        if (frame.OlderFrames is { } older)
        {
          foreach (var capture in older)
            Add(RunEventKind.OutOfOrder, new RunEvent(capture.CaptureTicks, 1, capture.FrameIndex));
        }
        // A camera's tears are found per frame; a capture card's are its torn captures (below)
        if (chart.Camera && (frame.Flags & PresentedFrameFlags.Torn) != 0)
          Add(RunEventKind.Torn, new RunEvent(frame.FirstSeenTicks, 1, frame.FrameIndex));
      }

      if (chart.Captures is { } rows && frames.Count > 0)
        AddCaptures(rows, frames, period, chart.Camera, Add);
      return new RunEvents(events, chart.Captures != null);
    }

    /// <summary>The capture rows from the run's first frame's first capture to its last frame's last sighting.</summary>
    private static void AddCaptures(
      IReadOnlyList<CaptureCsvRow> rows,
      IReadOnlyList<PresentedFrame> frames,
      long period,
      bool camera,
      Action<RunEventKind, RunEvent> add
    )
    {
      long firstIndex = frames.Min(f => f.FirstCaptureIndex);
      long lastTicks = frames.Max(f => f.LastSeenTicks);
      int start = RunChartData.FirstWhere(0, rows.Count, i => rows[i].CaptureIndex >= firstIndex);
      // A capture the recorder dropped has no time: it is placed a period per capture index after the last one recorded
      long knownTicks = start < rows.Count ? rows[start].CaptureTime?.Ticks ?? frames[0].FirstSeenTicks : 0;
      long knownIndex = start < rows.Count ? rows[start].CaptureIndex : 0;
      for (int i = start; i < rows.Count; ++i)
      {
        var row = rows[i];
        long ticks = row.CaptureTime?.Ticks ?? knownTicks + ((row.CaptureIndex - knownIndex) * period);
        if (ticks > lastTicks)
          break;
        if (row.CaptureTime?.Ticks is { } recorded)
        {
          knownTicks = recorded;
          knownIndex = row.CaptureIndex;
        }
        // Reported or found before this capture: in the refresh before it
        if (row.SourceDropsBefore > 0)
          add(RunEventKind.SourceDropped, new RunEvent(ticks - period, row.SourceDropsBefore));
        if (row.MissedBefore > 0)
          add(RunEventKind.Missed, new RunEvent(ticks - period, row.MissedBefore));
        switch (row.Status)
        {
          case "NotRecorded":
            add(RunEventKind.NotRecorded, new RunEvent(ticks, 1));
            break;
          // A camera's zones legitimately show different frames or none while the scanout passes: not events of the capture
          case "Undecodable" when !camera:
            add(RunEventKind.NotDecoded, new RunEvent(ticks, 1));
            break;
          case "Torn" when !camera:
            add(RunEventKind.Torn, new RunEvent(ticks, 1, row.FrameIndex, row.SyncFrameIndex));
            break;
        }
      }
    }
  }
}
