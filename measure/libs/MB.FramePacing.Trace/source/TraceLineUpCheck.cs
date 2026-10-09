//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Works out how well a run's markers and a trace line up (TraceLineUp), three ways that do not depend on each other:
//*
//* - The CPU side: a marker says when its frame's CPU work began and how long it took, and the trace has when the application's thread
//*   was running. Lined up, the thread is awake for the markers' busy time; and matched a frame earlier or later it is awake for less of
//*   it, where the frames' work differs.
//* - The display side: the measured display time minus the system's own present time is the same for every frame when each frame of the
//*   capture is the frame of the trace it was matched to, a late one too.
//* - By id: where the application named its frames in the trace (FrameSection), a frame of the trace is known by its marker's frame
//*   index, and the marker's CPU start time and CPU busy time can be held against the section's start and length.
//*
//* Integer arithmetic on whole nanoseconds.
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
  /// <summary>Experimental. Checks how a run's markers and a trace line up.</summary>
  public static class TraceLineUpCheck
  {
    /// <summary>
    /// The numbers for <paramref name="frames"/> of the run <paramref name="runId"/> and their <paramref name="rows"/> of the join (a row
    /// per frame, in the frames' order) on the trace <paramref name="data"/> is of.
    /// </summary>
    public static TraceLineUp Check(uint runId, IReadOnlyList<FrameRow> frames, IReadOnlyList<TraceFrame> rows, TraceData data, long periodNs)
    {
      // The CPU side: each marker's busy time against the thread's states, where it is and begun where its neighbours' begin
      var stateStarts = data.ThreadStates.Select(state => state.StartNs).ToArray();
      var worked = new List<int>();
      for (int i = 0; i < rows.Count; ++i)
      {
        if (rows[i].CpuStartTraceNs != null && rows[i].CpuEndTraceNs != null)
          worked.Add(i);
      }
      int scheduled = 0;
      int mostlyAsleep = 0;
      long busyTotal = 0;
      long awake = 0;
      long awakeEarlier = 0;
      long awakeLater = 0;
      for (int position = 0; position < worked.Count; ++position)
      {
        var row = rows[worked[position]];
        if (row.CpuRunningNs is not { } running || row.CpuRunnableNs is not { } runnable || row.CpuSleepingNs is not { } sleeping)
          continue;
        long busy = row.CpuEndTraceNs!.Value - row.CpuStartTraceNs!.Value;
        ++scheduled;
        busyTotal += busy;
        awake += running + runnable;
        mostlyAsleep += sleeping * 2 > busy ? 1 : 0;
        if (position > 0)
          awakeEarlier += AwakeIn(data.ThreadStates, stateStarts, rows[worked[position - 1]].CpuStartTraceNs!.Value, busy);
        if (position + 1 < worked.Count)
          awakeLater += AwakeIn(data.ThreadStates, stateStarts, rows[worked[position + 1]].CpuStartTraceNs!.Value, busy);
      }

      // The display side
      var differences = rows.Where(row => row.DisplayMinusSystemPresentNs != null).Select(row => row.DisplayMinusSystemPresentNs!.Value).ToList();
      differences.Sort();
      long median = differences.Count > 0 ? differences[(differences.Count - 1) / 2] : 0;
      long half = periodNs / 2;
      int off = differences.Count(difference => Math.Abs(difference - median) > half);

      // By id: the first section of each frame index of this run
      var sections = new Dictionary<ulong, FrameSection>();
      int runSections = 0;
      foreach (var section in data.FrameSections)
      {
        if (section.RunId != runId)
          continue;
        ++runSections;
        sections.TryAdd(section.FrameIndex, section);
      }
      var startDifferences = new List<long>();
      var busyDifferences = new List<long>();
      int withStart = 0;
      for (int i = 0; i < frames.Count; ++i)
      {
        if (rows[i].CpuStartTraceNs is not { } start)
          continue;
        ++withStart;
        if (!sections.TryGetValue(frames[i].FrameIndex, out var section))
          continue;
        startDifferences.Add(start - section.StartNs);
        if (rows[i].CpuEndTraceNs is { } end)
          busyDifferences.Add(end - start - (section.EndNs - section.StartNs));
      }
      startDifferences.Sort();
      busyDifferences.Sort();
      return new TraceLineUp(
        scheduled,
        busyTotal,
        awake,
        awakeEarlier,
        awakeLater,
        mostlyAsleep,
        differences.Count,
        median,
        differences.Count > 0 ? differences[0] : 0,
        differences.Count > 0 ? differences[^1] : 0,
        off,
        runSections,
        startDifferences.Count,
        withStart,
        Middle(startDifferences),
        startDifferences.Count > 0 ? startDifferences[0] : 0,
        startDifferences.Count > 0 ? startDifferences[^1] : 0,
        Middle(busyDifferences),
        busyDifferences.Count > 0 ? busyDifferences[0] : 0,
        busyDifferences.Count > 0 ? busyDifferences[^1] : 0
      );
    }

    /// <summary>How long the thread was running or waiting for a CPU from <paramref name="fromNs"/> for <paramref name="lengthNs"/>.</summary>
    private static long AwakeIn(IReadOnlyList<ThreadState> states, IReadOnlyList<long> starts, long fromNs, long lengthNs) =>
      TraceJoin.ThreadTime(states, starts, fromNs, fromNs + lengthNs) is { } times ? times.RunningNs + times.RunnableNs : 0;

    private static long Middle(List<long> sorted) => sorted.Count > 0 ? sorted[(sorted.Count - 1) / 2] : 0;
  }
}
