//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Reads a trace for a join (TraceData): starts Perfetto's trace processor on the file, asks what the options need, and ends it. What was
//* read last is kept: the GUI analyses a capture again on each option change, and a trace takes seconds to load.
//*
//* The frame record is read first (sdk/doc/trace-frame-record.md), from the trace and from a trace of the application's own where the
//* options name one: it says which process is the application, unless the options name one, and the join is placed by it.
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
using MB.FramePacing.Trace;

namespace MB.FramePacing.TraceJoin
{
  /// <summary>Experimental. Reads what a join needs of a trace.</summary>
  public static class TraceLoader
  {
    private static readonly object g_lock = new object();
    private static (string Key, TraceData Data)? g_last;
    private static (string Key, IReadOnlyList<FrameRecordSlice> Records)? g_lastRecords;

    /// <summary>
    /// What <paramref name="options"/> need of their trace, read with the trace processor at <paramref name="processorPath"/>, with the
    /// frame record of the application's own trace too where they name one. The same files with the same options are read once: asked
    /// for again, what was read is given back.
    /// </summary>
    public static TraceData Load(string processorPath, TraceJoinOptions options, CancellationToken cancellationToken = default)
    {
      string path = Path.GetFullPath(options.Path);
      var file = new FileInfo(path);
      if (!file.Exists)
        throw new TraceException($"The Perfetto trace does not exist: '{path}'");
      bool hasAppTrace = !string.IsNullOrEmpty(options.AppTracePath);
      var appRecords = hasAppTrace ? LoadAppRecords(processorPath, options, cancellationToken) : null;
      string key = string.Join(
        '\n',
        path,
        file.Length,
        file.LastWriteTimeUtc.Ticks,
        options.Process,
        options.Layer,
        options.Thread,
        options.VBlanks,
        hasAppTrace ? StampOf(options.AppTracePath!) : string.Empty
      );
      lock (g_lock)
      {
        if (g_last is { } last && last.Key == key)
          return last.Data;
      }
      TraceData data;
      using (var processor = TraceProcessor.Start(processorPath, path, cancellationToken: cancellationToken))
        data = Read(processor, options, appRecords);
      lock (g_lock)
        g_last = (key, data);
      return data;
    }

    /// <summary>
    /// What <paramref name="options"/> need of the trace <paramref name="trace"/> answers for. <paramref name="appRecords"/> is the frame
    /// record of the application's own trace, where there is one: it counts before the trace's own.
    /// </summary>
    public static TraceData Read(ITraceQueries trace, TraceJoinOptions options, IReadOnlyList<FrameRecordSlice>? appRecords = null)
    {
      var notes = new List<string>();
      var range = TraceReader.TraceRange(trace);
      var snapshots = TraceReader.ClockSnapshots(trace, FrameRecordText.ClockId);
      if (snapshots.Count == 0)
        notes.Add("the trace has no snapshot of the monotonic clock: its own time is taken to be that clock");
      var errors = TraceReader.Errors(trace);

      IReadOnlyList<FrameRecordSlice> records = OnTheRecordsClock(TraceReader.FrameRecords(trace), snapshots);
      if (appRecords != null)
        records = appRecords.Concat(records).ToList();
      // The application: the process the options name, else the one its frame record is on
      string? process = options.Process;
      if (process == null && ProcessOf(records) is { } named)
      {
        process = TraceReader.LiteralPattern(named);
        notes.Add("the application's process is the one its frame record is on");
      }

      var picked = PickVBlanks(options.VBlanks, TraceReader.VBlankSources(trace));
      IReadOnlyList<long> vblanks = picked != null ? TraceReader.VBlanks(trace, picked) : Array.Empty<long>();

      IReadOnlyList<SurfaceFrame> surfaceFrames = Array.Empty<SurfaceFrame>();
      IReadOnlyDictionary<long, long> presents = new Dictionary<long, long>();
      IReadOnlyList<ThreadState> states = Array.Empty<ThreadState>();
      if (process != null)
      {
        surfaceFrames = TraceReader.SurfaceFrames(trace, process, options.Layer ?? "*");
        if (surfaceFrames.Count > 0)
          presents = TraceReader.DisplayPresents(trace);
        var threads = TraceReader.Threads(trace, process);
        var wanted = threads.FirstOrDefault(thread => options.Thread != null ? thread.Thread == options.Thread : thread.IsMain);
        if (options.Thread != null && wanted == null)
          throw new TraceException($"No thread named '{options.Thread}' in the processes that fit '{options.Process ?? "the application's"}'.");
        if (wanted != null)
          states = TraceReader.ThreadStates(trace, wanted.Utid);
      }
      return new TraceData(range.FirstNs, range.LastNs, snapshots, errors, picked, vblanks, surfaceFrames, presents, states, notes)
      {
        FrameRecords = records,
      };
    }

    /// <summary>
    /// The frame record of a trace the application wrote itself (<see cref="TraceJoinOptions.AppTracePath"/>), with the times on the
    /// record's clock. What was read last is kept, as for a trace that is joined.
    /// </summary>
    public static IReadOnlyList<FrameRecordSlice> LoadAppRecords(
      string processorPath,
      TraceJoinOptions options,
      CancellationToken cancellationToken = default
    )
    {
      string path = Path.GetFullPath(options.AppTracePath ?? string.Empty);
      if (!File.Exists(path))
        throw new TraceException($"The application's trace does not exist: '{path}'");
      string key = StampOf(path);
      lock (g_lock)
      {
        if (g_lastRecords is { } last && last.Key == key)
          return last.Records;
      }
      IReadOnlyList<FrameRecordSlice> records;
      using (var processor = TraceProcessor.Start(processorPath, path, cancellationToken: cancellationToken))
        records = OnTheRecordsClock(TraceReader.FrameRecords(processor), TraceReader.ClockSnapshots(processor, FrameRecordText.ClockId));
      lock (g_lock)
        g_lastRecords = (key, records);
      return records;
    }

    /// <summary>The process most of the record's slices are on (the first of those with as many); null when no slice names one.</summary>
    public static string? ProcessOf(IReadOnlyList<FrameRecordSlice> records)
    {
      var counts = new Dictionary<string, int>(StringComparer.Ordinal);
      string? most = null;
      foreach (var record in records)
      {
        if (record.Process is not { } process)
          continue;
        int count = counts.GetValueOrDefault(process) + 1;
        counts[process] = count;
        if (most == null || count > counts[most])
          most = process;
      }
      return most;
    }

    /// <summary>The slices with their times taken from the trace's clock to the record's, which <paramref name="snapshots"/> are of.</summary>
    private static IReadOnlyList<FrameRecordSlice> OnTheRecordsClock(IReadOnlyList<FrameRecordSlice> records, IReadOnlyList<ClockSnapshot> snapshots)
    {
      var converted = new FrameRecordSlice[records.Count];
      for (int i = 0; i < converted.Length; ++i)
      {
        long start = TraceAlignmentUtil.FromTraceNs(records[i].StartNs, snapshots);
        converted[i] = records[i] with { StartNs = start, EndNs = start + (records[i].EndNs - records[i].StartNs) };
      }
      return converted;
    }

    /// <summary>What says a file is the one that was read: where it is, how long it is and when it was written.</summary>
    private static string StampOf(string path)
    {
      var file = new FileInfo(Path.GetFullPath(path));
      return string.Join('\n', file.FullName, file.Exists ? file.Length : -1, file.Exists ? file.LastWriteTimeUtc.Ticks : 0);
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
      {
        g_last = null;
        g_lastRecords = null;
      }
    }
  }
}
