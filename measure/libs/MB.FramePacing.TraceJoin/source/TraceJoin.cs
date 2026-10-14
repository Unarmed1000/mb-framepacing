//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What a Perfetto trace says of each presented frame of a capture: the join, with nothing of Perfetto in it (TraceReader reads the
//* trace). A port of tools/trace_frames.py; the two give the same rows (measure/test-data/trace/frames.csv).
//*
//* Which frame of the trace is a frame of the capture is worked out from times alone (the end of the CPU's work), not from an id the two
//* share: a trace and a capture have none in common. No recorded trace has been through this yet.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using MB.FramePacing.Data;

namespace MB.FramePacing.TraceJoin
{
  /// <summary>Joins a run's frames with what a trace has: vertical blanks, the system's frame timeline, a thread's scheduling.</summary>
  public static class TraceJoin
  {
    /// <summary>How many of the application's frames that began before a moment are looked at for the one the moment lies in.</summary>
    public const int OverlappingFrames = 16;

    /// <summary>
    /// The application's frame that <paramref name="timeNs"/> lies in: of those that began at or before it and had not ended, the one that
    /// began last. <paramref name="starts"/> are the frames' start times, ascending as the frames are.
    /// </summary>
    public static SurfaceFrame? SurfaceFrameAt(IReadOnlyList<SurfaceFrame> surfaceFrames, IReadOnlyList<long> starts, long timeNs)
    {
      int index = TraceAlignmentUtil.UpperBound(starts, timeNs) - 1;
      for (int candidate = index; candidate > Math.Max(index - OverlappingFrames, -1); --candidate)
      {
        if (surfaceFrames[candidate].EndNs >= timeNs)
          return surfaceFrames[candidate];
      }
      return null;
    }

    /// <summary>
    /// How long a thread was running, waiting for a CPU and neither, from <paramref name="fromNs"/> to <paramref name="toNs"/>. Null when
    /// the trace has no state of the thread in that stretch. <paramref name="starts"/> are the states' start times, ascending.
    /// </summary>
    public static (long RunningNs, long RunnableNs, long SleepingNs)? ThreadTime(
      IReadOnlyList<ThreadState> states,
      IReadOnlyList<long> starts,
      long fromNs,
      long toNs
    )
    {
      long running = 0;
      long runnable = 0;
      long sleeping = 0;
      bool found = false;
      for (int index = Math.Max(TraceAlignmentUtil.UpperBound(starts, fromNs) - 1, 0); index < states.Count && states[index].StartNs < toNs; ++index)
      {
        var state = states[index];
        long overlap = Math.Min(state.EndNs, toNs) - Math.Max(state.StartNs, fromNs);
        if (overlap <= 0)
          continue;
        found = true;
        if (state.State == "Running")
          running += overlap;
        else if (state.State.StartsWith('R'))
          runnable += overlap;
        else
          sleeping += overlap;
      }
      return found ? (running, runnable, sleeping) : null;
    }

    /// <summary>
    /// A row per frame. <paramref name="vblanks"/> are on the trace's clock, ascending; <paramref name="surfaceFrames"/> and
    /// <paramref name="threadStates"/> ascending by their start; <paramref name="displayPresents"/> has, for a display frame token, when
    /// the system says that frame was presented.
    /// </summary>
    public static IReadOnlyList<TraceFrame> Join(
      IReadOnlyList<FrameRow> frames,
      TraceAlignment alignment,
      IReadOnlyList<ClockSnapshot> snapshots,
      long periodNs,
      IReadOnlyList<long> vblanks,
      IReadOnlyList<SurfaceFrame> surfaceFrames,
      IReadOnlyDictionary<long, long> displayPresents,
      IReadOnlyList<ThreadState> threadStates
    )
    {
      var surfaceStarts = new long[surfaceFrames.Count];
      for (int i = 0; i < surfaceStarts.Length; ++i)
        surfaceStarts[i] = surfaceFrames[i].StartNs;
      var stateStarts = new long[threadStates.Count];
      for (int i = 0; i < stateStarts.Length; ++i)
        stateStarts[i] = threadStates[i].StartNs;
      long half = periodNs / 2;

      var rows = new List<TraceFrame>(frames.Count);
      foreach (var frame in frames)
      {
        long display = TraceAlignmentUtil.ToTraceNs(alignment.DisplayTime(frame.FirstSeenNs()), snapshots);
        long? cpuStart = frame.CpuStartNs() is { } start ? TraceAlignmentUtil.ToTraceNs(alignment.MarkerTime(start), snapshots) : null;
        long? cpuEnd = cpuStart is { } begun && frame.CpuBusyNs() is { } busy ? begun + busy : null;

        long? blank = TraceAlignmentUtil.Nearest(vblanks, display);
        if (blank is { } nearest && Math.Abs(display - nearest) > half)
          blank = null;

        var surface = cpuEnd is { } ended ? SurfaceFrameAt(surfaceFrames, surfaceStarts, ended) : null;
        long? present = surface?.DisplayFrameToken is { } token && displayPresents.TryGetValue(token, out long presented) ? presented : null;
        var times = cpuStart is { } from && cpuEnd is { } to ? ThreadTime(threadStates, stateStarts, from, to) : null;

        rows.Add(
          new TraceFrame(
            frame.FrameIndex,
            display,
            cpuStart,
            cpuEnd,
            blank,
            blank is { } known ? display - known : null,
            surface?.SurfaceFrameToken,
            surface?.DisplayFrameToken,
            surface?.LayerName,
            surface?.PresentType,
            surface?.JankType,
            surface?.JankTag,
            surface?.OnTimeFinish,
            surface?.GpuComposition,
            present,
            present is { } at ? display - at : null,
            times?.RunningNs,
            times?.RunnableNs,
            times?.SleepingNs
          )
        );
      }
      return rows;
    }
  }
}
