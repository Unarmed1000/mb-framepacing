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
      m_events = events.Select(list => list.OrderBy(e => e.Time.Ticks).ToArray()).ToArray();
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

    /// <summary>Every event of <paramref name="kind"/>, in time order.</summary>
    public ReadOnlySpan<RunEvent> All(RunEventKind kind) => m_events[(int)kind];

    /// <summary>How many frames or refreshes all events of <paramref name="kind"/> stand for.</summary>
    public long Count(RunEventKind kind) => m_totals[(int)kind][^1];

    /// <summary>The events of <paramref name="kind"/> from <paramref name="from"/> up to (not including) <paramref name="to"/>.</summary>
    public ReadOnlySpan<RunEvent> In(RunEventKind kind, TickCount64 from, TickCount64 to)
    {
      var (start, end) = Range(kind, from, to);
      return m_events[(int)kind].AsSpan(start, end - start);
    }

    /// <summary>Whether <paramref name="kind"/> has an event from <paramref name="from"/> up to <paramref name="to"/>.</summary>
    public bool Any(RunEventKind kind, TickCount64 from, TickCount64 to)
    {
      var (start, end) = Range(kind, from, to);
      return end > start;
    }

    /// <summary>How many frames or refreshes the events of <paramref name="kind"/> in the range stand for.</summary>
    public long Count(RunEventKind kind, TickCount64 from, TickCount64 to)
    {
      var (start, end) = Range(kind, from, to);
      return m_totals[(int)kind][end] - m_totals[(int)kind][start];
    }

    private (int Start, int End) Range(RunEventKind kind, TickCount64 from, TickCount64 to)
    {
      var list = m_events[(int)kind];
      int start = FirstAt(list, 0, from);
      return (start, FirstAt(list, start, to));
    }

    /// <summary>
    /// The first event from <paramref name="start"/> on at or after <paramref name="time"/> (RunChartData.FirstWhere's search, inline: the
    /// events panel asks per pixel column and kind, and a predicate would be a new closure each time).
    /// </summary>
    private static int FirstAt(RunEvent[] list, int start, TickCount64 time)
    {
      int end = list.Length;
      while (start < end)
      {
        int middle = start + ((end - start) / 2);
        if (list[middle].Time >= time)
          end = middle;
        else
          start = middle + 1;
      }
      return start;
    }

    public static RunEvents Of(RunChartData data)
    {
      var chart = data.Run;
      var frames = data.Frames;
      var period = chart.CapturePeriod;
      var events = Enum.GetValues<RunEventKind>().Select(_ => new List<RunEvent>()).ToArray();
      void Add(RunEventKind kind, RunEvent e) => events[(int)kind].Add(e);

      for (int i = 0; i < frames.Count; ++i)
      {
        var frame = frames[i];
        // Where the dropped frames were due: the refresh before the frame after them
        if (data.DroppedBeforeFrame[i] is > 0 and var dropped)
          Add(RunEventKind.FramesDropped, new RunEvent(frame.FirstSeenTime - period, dropped, frame.FrameIndex));
        if (frame.OlderFrames is { } older)
        {
          foreach (var capture in older)
            Add(RunEventKind.OutOfOrder, new RunEvent(capture.CaptureTime, 1, capture.FrameIndex));
        }
        // A camera's tears are found per frame; a capture card's are its torn captures (below)
        if (chart.Camera && (frame.Flags & PresentedFrameFlags.Torn) != 0)
          Add(RunEventKind.Torn, new RunEvent(frame.FirstSeenTime, 1, frame.FrameIndex));
      }

      if (chart.Captures is { } rows && frames.Count > 0)
        AddCaptures(rows, frames, period, chart.Camera, Add);
      return new RunEvents(events, chart.Captures != null);
    }

    /// <summary>The capture rows from the run's first frame's first capture to its last frame's last sighting.</summary>
    private static void AddCaptures(
      IReadOnlyList<CaptureCsvRow> rows,
      IReadOnlyList<PresentedFrame> frames,
      TimeSpan period,
      bool camera,
      Action<RunEventKind, RunEvent> add
    )
    {
      long firstIndex = frames.Min(f => f.FirstCaptureIndex);
      var lastSeen = new TickCount64(frames.Max(f => f.LastSeenTime.Ticks));
      int start = RunChartData.FirstWhere(0, rows.Count, i => rows[i].CaptureIndex >= firstIndex);
      // A capture the recorder dropped has no time: it is placed a period per capture index after the last one recorded
      var knownTime = start < rows.Count ? rows[start].CaptureTime ?? frames[0].FirstSeenTime : default;
      long knownIndex = start < rows.Count ? rows[start].CaptureIndex : 0;
      for (int i = start; i < rows.Count; ++i)
      {
        var row = rows[i];
        var time = row.CaptureTime ?? knownTime + new TimeSpan((row.CaptureIndex - knownIndex) * period.Ticks);
        if (time > lastSeen)
          break;
        if (row.CaptureTime is { } recorded)
        {
          knownTime = recorded;
          knownIndex = row.CaptureIndex;
        }
        // Reported or found before this capture: in the refresh before it
        if (row.SourceDropsBefore > 0)
          add(RunEventKind.SourceDropped, new RunEvent(time - period, row.SourceDropsBefore));
        if (row.MissedBefore > 0)
          add(RunEventKind.Missed, new RunEvent(time - period, row.MissedBefore));
        switch (row.CaptureStatus)
        {
          case "NotRecorded":
            add(RunEventKind.NotRecorded, new RunEvent(time, 1));
            break;
          // A camera's zones legitimately show different frames or none while the scanout passes: not events of the capture
          case "Undecodable" when !camera:
            add(RunEventKind.NotDecoded, new RunEvent(time, 1));
            break;
          case "Torn" when !camera:
            add(RunEventKind.Torn, new RunEvent(time, 1, row.FrameIndex, row.SyncFrameIndex));
            break;
        }
      }
    }
  }
}
