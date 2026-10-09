//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Reads a trace for a join (TraceData): starts Perfetto's trace processor on the file, asks what the options need, and ends it. What was
//* read last is kept: the GUI analyses a capture again on each option change, and a trace takes seconds to load.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MB.FramePacing.Capture;

namespace MB.FramePacing.Trace
{
  /// <summary>Experimental. Reads what a join needs of a trace.</summary>
  public static class TraceLoader
  {
    private static readonly object g_lock = new object();
    private static (string Key, TraceData Data)? g_last;

    /// <summary>
    /// What <paramref name="options"/> need of their trace, read with the trace processor at <paramref name="processorPath"/>. The same
    /// file with the same options is read once: asked for again, what was read is given back.
    /// </summary>
    public static TraceData Load(string processorPath, TraceJoinOptions options, CancellationToken cancellationToken = default)
    {
      string path = Path.GetFullPath(options.Path);
      var file = new FileInfo(path);
      if (!file.Exists)
        throw new TraceException($"The Perfetto trace does not exist: '{path}'");
      string key = string.Join(
        '\n',
        path,
        file.Length,
        file.LastWriteTimeUtc.Ticks,
        options.Process,
        options.Layer,
        options.Thread,
        options.VBlanks,
        TraceAppClockUtil.Of(options)
      );
      lock (g_lock)
      {
        if (g_last is { } last && last.Key == key)
          return last.Data;
      }
      TraceData data;
      using (var processor = TraceProcessor.Start(processorPath, path, cancellationToken: cancellationToken))
        data = Read(processor, options);
      lock (g_lock)
        g_last = (key, data);
      return data;
    }

    /// <summary>What <paramref name="options"/> need of the trace <paramref name="trace"/> answers for.</summary>
    public static TraceData Read(ITraceQueries trace, TraceJoinOptions options)
    {
      var clock = TraceAppClockUtil.Of(options);
      var notes = new List<string>();
      var range = TraceReader.TraceRange(trace);
      var snapshots = TraceReader.ClockSnapshots(trace, TraceAppClockUtil.ClockId(clock));
      if (snapshots.Count == 0)
        notes.Add($"the trace has no snapshot of the {TraceAppClockUtil.NameOf(clock)} clock: its own time is taken to be that clock");
      var errors = TraceReader.Errors(trace);

      var picked = PickVBlanks(options.VBlanks, TraceReader.VBlankSources(trace));
      IReadOnlyList<long> vblanks = picked != null ? TraceReader.VBlanks(trace, picked) : Array.Empty<long>();

      IReadOnlyList<SurfaceFrame> surfaceFrames = Array.Empty<SurfaceFrame>();
      IReadOnlyDictionary<long, long> presents = new Dictionary<long, long>();
      IReadOnlyList<ThreadState> states = Array.Empty<ThreadState>();
      if (options.Process != null)
      {
        surfaceFrames = TraceReader.SurfaceFrames(trace, options.Process, options.Layer ?? "*");
        if (surfaceFrames.Count > 0)
          presents = TraceReader.DisplayPresents(trace);
        var threads = TraceReader.Threads(trace, options.Process);
        var wanted = threads.FirstOrDefault(thread => options.Thread != null ? thread.Thread == options.Thread : thread.IsMain);
        if (options.Thread != null && wanted == null)
          throw new TraceException($"No thread named '{options.Thread}' in the processes that fit '{options.Process}'.");
        if (wanted != null)
          states = TraceReader.ThreadStates(trace, wanted.Utid);
      }
      return new TraceData(range.FirstNs, range.LastNs, snapshots, errors, picked, vblanks, surfaceFrames, presents, states, notes)
      {
        FrameSections = options.Process != null ? TraceReader.FrameSections(trace, options.Process) : Array.Empty<FrameSection>(),
      };
    }

    /// <summary>
    /// The source of vertical blanks to use: the one named, none when "none" is named, and else the kernel's with the most. A name the
    /// trace does not have is a <see cref="TraceException"/> that lists what it has.
    /// </summary>
    public static VBlankSource? PickVBlanks(string? named, IReadOnlyList<VBlankSource> sources)
    {
      if (named == "none")
        return null;
      if (named != null)
      {
        var found = sources.FirstOrDefault(source => source.Name == named);
        if (found != null)
          return found;
        string listed = sources.Count > 0 ? string.Join(", ", sources.Select(source => source.Name)) : "none";
        throw new TraceException($"The trace has no vertical blanks named '{named}' (it has: {listed}).");
      }
      VBlankSource? most = null;
      foreach (var source in sources)
      {
        if (!source.IsCounter && (most == null || source.Count > most.Count))
          most = source;
      }
      return most;
    }

    /// <summary>Forget what was read last (the tests').</summary>
    internal static void Forget()
    {
      lock (g_lock)
        g_last = null;
    }
  }
}
