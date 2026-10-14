//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Reads what a join needs of a Perfetto trace, as SQL to Perfetto's trace processor: the trace's clocks, its processes, the display's
//* vertical blanks, the system's frame timeline and a thread's scheduling states. The queries are tools/trace_source.py's, word for
//* word. Every time is whole nanoseconds on the trace's clock.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MB.FramePacing.Trace;

namespace MB.FramePacing.TraceJoin
{
  /// <summary>The questions a join asks a trace.</summary>
  public static class TraceReader
  {
    /// <summary>The kernel's vertical blank events, as the trace processor shows them: slices named so on tracks named vblank-&lt;crtc&gt;.</summary>
    public const string DrmVBlankTracks = "vblank-*";

    public const string DrmVBlankSlice = "signal";

    /// <summary>A text as an SQL string.</summary>
    public static string Quoted(string text) => "'" + text.Replace("'", "''", System.StringComparison.Ordinal) + "'";

    /// <summary>The first and the last time of the trace.</summary>
    public static (long FirstNs, long LastNs) TraceRange(ITraceQueries trace)
    {
      var rows = trace.Query("SELECT start_ts, end_ts FROM trace_bounds");
      if (rows.RowCount == 0)
        throw new TraceException("The trace processor says nothing of where the trace begins and ends.");
      return (rows.Whole(0, 0), rows.Whole(0, 1));
    }

    /// <summary>The trace's snapshots of one system clock, ascending by that clock.</summary>
    public static IReadOnlyList<ClockSnapshot> ClockSnapshots(ITraceQueries trace, int clockId)
    {
      var rows = trace.Query(
        $"SELECT ts, clock_value FROM clock_snapshot WHERE clock_id = {clockId.ToString(CultureInfo.InvariantCulture)} ORDER BY clock_value, ts"
      );
      var snapshots = new List<ClockSnapshot>(rows.RowCount);
      for (int row = 0; row < rows.RowCount; ++row)
        snapshots.Add(new ClockSnapshot(rows.Whole(row, 0), rows.Whole(row, 1)));
      return snapshots;
    }

    /// <summary>The clocks the trace has snapshots of.</summary>
    public static IReadOnlyList<TraceClockInfo> Clocks(ITraceQueries trace)
    {
      var rows = trace.Query(
        "SELECT clock_id, clock_name, COUNT(*) AS snapshots FROM clock_snapshot GROUP BY clock_id, clock_name ORDER BY clock_id"
      );
      var clocks = new List<TraceClockInfo>(rows.RowCount);
      for (int row = 0; row < rows.RowCount; ++row)
        clocks.Add(new TraceClockInfo(rows.Whole(row, 0), rows.TextOrNull(row, 1), rows.Whole(row, 2)));
      return clocks;
    }

    /// <summary>What the trace processor counted as errors while it read the trace, by name.</summary>
    public static IReadOnlyDictionary<string, long> Errors(ITraceQueries trace)
    {
      var rows = trace.Query("SELECT name, SUM(value) AS total FROM stats WHERE severity = 'error' AND value > 0 GROUP BY name ORDER BY name");
      var errors = new SortedDictionary<string, long>(System.StringComparer.Ordinal);
      for (int row = 0; row < rows.RowCount; ++row)
        errors[rows.Text(row, 0)] = rows.Whole(row, 1);
      return errors;
    }

    /// <summary>The processes with a name that fits the pattern (* and ? as in a file name), by name.</summary>
    public static IReadOnlyList<TraceProcessInfo> Processes(ITraceQueries trace, string nameGlob = "*")
    {
      var rows = trace.Query($"SELECT upid, pid, name FROM process WHERE name GLOB {Quoted(nameGlob)} ORDER BY name, upid");
      var processes = new List<TraceProcessInfo>(rows.RowCount);
      for (int row = 0; row < rows.RowCount; ++row)
        processes.Add(new TraceProcessInfo(rows.Whole(row, 0), rows.WholeOrNull(row, 1), rows.Text(row, 2)));
      return processes;
    }

    /// <summary>The kernel's vertical blank events per display controller, then the counters with VSYNC in their name.</summary>
    public static IReadOnlyList<VBlankSource> VBlankSources(ITraceQueries trace)
    {
      var sources = new List<VBlankSource>();
      var drm = trace.Query(
        "SELECT t.name AS name, COUNT(*) AS count FROM slice s JOIN track t ON s.track_id = t.id "
          + $"WHERE t.name GLOB {Quoted(DrmVBlankTracks)} AND s.name = {Quoted(DrmVBlankSlice)} GROUP BY t.name ORDER BY t.name"
      );
      for (int row = 0; row < drm.RowCount; ++row)
        sources.Add(new VBlankSource(false, drm.Text(row, 0), drm.Whole(row, 1)));
      var counters = trace.Query(
        "SELECT t.name AS name, COUNT(*) AS count FROM counter c JOIN counter_track t ON c.track_id = t.id "
          + "WHERE t.name GLOB '*VSYNC*' OR t.name GLOB '*vsync*' GROUP BY t.name ORDER BY t.name"
      );
      for (int row = 0; row < counters.RowCount; ++row)
        sources.Add(new VBlankSource(true, counters.Text(row, 0), counters.Whole(row, 1)));
      return sources;
    }

    /// <summary>The times of one source's vertical blanks, ascending.</summary>
    public static IReadOnlyList<long> VBlanks(ITraceQueries trace, VBlankSource source)
    {
      string sql = source.IsCounter
        ? "SELECT DISTINCT c.ts AS ts FROM counter c JOIN counter_track t ON c.track_id = t.id "
          + $"WHERE t.name = {Quoted(source.Name)} ORDER BY c.ts"
        : "SELECT DISTINCT s.ts AS ts FROM slice s JOIN track t ON s.track_id = t.id "
          + $"WHERE t.name = {Quoted(source.Name)} AND s.name = {Quoted(DrmVBlankSlice)} ORDER BY s.ts";
      var rows = trace.Query(sql);
      var times = new long[rows.RowCount];
      for (int row = 0; row < times.Length; ++row)
        times[row] = rows.Whole(row, 0);
      return times;
    }

    /// <summary>
    /// The frame timeline's actual frames of the application (the processes and layers that fit the patterns), ascending by their start. A
    /// frame that had not ended when the trace did is left out.
    /// </summary>
    public static IReadOnlyList<SurfaceFrame> SurfaceFrames(ITraceQueries trace, string processGlob, string layerGlob = "*")
    {
      var rows = trace.Query(
        "SELECT a.ts AS ts, a.dur AS dur, a.surface_frame_token AS surface_token, a.display_frame_token AS display_token, "
          + "a.layer_name AS layer_name, a.present_type AS present_type, a.jank_type AS jank_type, a.jank_tag AS jank_tag, "
          + "a.on_time_finish AS on_time_finish, a.gpu_composition AS gpu_composition "
          + "FROM actual_frame_timeline_slice a JOIN process p USING (upid) "
          + $"WHERE p.name GLOB {Quoted(processGlob)} AND IFNULL(a.layer_name, '') GLOB {Quoted(layerGlob)} "
          + "AND IFNULL(a.surface_frame_token, 0) != 0 AND a.dur >= 0 ORDER BY a.ts, a.surface_frame_token"
      );
      var frames = new List<SurfaceFrame>(rows.RowCount);
      for (int row = 0; row < rows.RowCount; ++row)
      {
        long start = rows.Whole(row, 0);
        frames.Add(
          new SurfaceFrame(
            start,
            start + rows.Whole(row, 1),
            rows.Whole(row, 2),
            rows.WholeOrNull(row, 3),
            rows.TextOrNull(row, 4),
            rows.TextOrNull(row, 5),
            rows.TextOrNull(row, 6),
            rows.TextOrNull(row, 7),
            rows.WholeOrNull(row, 8) is { } onTime ? onTime != 0 : null,
            rows.WholeOrNull(row, 9) is { } gpu ? gpu != 0 : null
          )
        );
      }
      return frames;
    }

    /// <summary>The frame timeline's layers of the processes that fit the pattern, the one with the most frames first.</summary>
    public static IReadOnlyList<TraceLayerInfo> Layers(ITraceQueries trace, string processGlob)
    {
      var rows = trace.Query(
        "SELECT p.name AS process, IFNULL(a.layer_name, '') AS layer, COUNT(*) AS frames "
          + "FROM actual_frame_timeline_slice a JOIN process p USING (upid) "
          + $"WHERE p.name GLOB {Quoted(processGlob)} AND IFNULL(a.surface_frame_token, 0) != 0 GROUP BY p.name, a.layer_name ORDER BY frames DESC"
      );
      var layers = new List<TraceLayerInfo>(rows.RowCount);
      for (int row = 0; row < rows.RowCount; ++row)
        layers.Add(new TraceLayerInfo(rows.Text(row, 0), rows.Text(row, 1), rows.Whole(row, 2)));
      return layers;
    }

    /// <summary>
    /// When the system says each of its display frames was presented, by the frame's token: the end of the frame timeline's actual display
    /// frame, which runs from the compositor's start on it to the update on screen.
    /// </summary>
    public static IReadOnlyDictionary<long, long> DisplayPresents(ITraceQueries trace)
    {
      var rows = trace.Query(
        "SELECT display_frame_token AS token, MAX(ts + dur) AS presented FROM actual_frame_timeline_slice "
          + "WHERE IFNULL(surface_frame_token, 0) = 0 AND display_frame_token IS NOT NULL AND dur >= 0 GROUP BY display_frame_token"
      );
      var presents = new Dictionary<long, long>(rows.RowCount);
      for (int row = 0; row < rows.RowCount; ++row)
        presents[rows.Whole(row, 0)] = rows.Whole(row, 1);
      return presents;
    }

    /// <summary>The threads of the processes that fit the pattern: by process, the main thread first.</summary>
    public static IReadOnlyList<TraceThreadInfo> Threads(ITraceQueries trace, string processGlob)
    {
      var rows = trace.Query(
        "SELECT t.utid AS utid, p.name AS process, IFNULL(t.name, '') AS thread, IFNULL(t.is_main_thread, 0) AS is_main "
          + $"FROM thread t JOIN process p USING (upid) WHERE p.name GLOB {Quoted(processGlob)} ORDER BY p.name, is_main DESC, t.utid"
      );
      var threads = new List<TraceThreadInfo>(rows.RowCount);
      for (int row = 0; row < rows.RowCount; ++row)
        threads.Add(new TraceThreadInfo(rows.Whole(row, 0), rows.Text(row, 1), rows.Text(row, 2), rows.Whole(row, 3) != 0));
      return threads;
    }

    /// <summary>One thread's scheduling states, ascending. A state that had not ended when the trace did is left out.</summary>
    public static IReadOnlyList<ThreadState> ThreadStates(ITraceQueries trace, long utid)
    {
      var rows = trace.Query(
        $"SELECT ts, dur, state FROM thread_state WHERE utid = {utid.ToString(CultureInfo.InvariantCulture)} AND dur > 0 ORDER BY ts"
      );
      var states = new List<ThreadState>(rows.RowCount);
      for (int row = 0; row < rows.RowCount; ++row)
      {
        long start = rows.Whole(row, 0);
        states.Add(new ThreadState(start, start + rows.Whole(row, 1), rows.Text(row, 2)));
      }
      return states;
    }

    /// <summary>
    /// The frame record (<see cref="FrameRecordSlice"/>): the slices named for a frame on any track, ascending by their start, with the
    /// annotations they have and the process their track belongs to. Their times are on the trace's own clock here (TraceLoader takes
    /// them to the record's). A slice that had not ended when the trace did, or whose name is not a frame's, is left out, and so are the
    /// tools' own slices of a frame (an augmented trace's tracks, under a track named for a run).
    /// </summary>
    public static IReadOnlyList<FrameRecordSlice> FrameRecords(ITraceQueries trace)
    {
      string annotations = string.Join(
        ", ",
        FrameRecordSlice.AnnotationNames.Select(name => $"EXTRACT_ARG(s.arg_set_id, {Quoted("debug." + name)})")
      );
      var rows = trace.Query(
        $"SELECT s.ts AS ts, s.dur AS dur, s.name AS name, {annotations}, p.name AS process FROM slice s "
          + "LEFT JOIN thread_track tt ON tt.id = s.track_id LEFT JOIN thread th ON th.utid = tt.utid "
          + "LEFT JOIN process_track pt ON pt.id = s.track_id LEFT JOIN process p ON p.upid = COALESCE(pt.upid, th.upid) "
          + "LEFT JOIN track own ON own.id = s.track_id LEFT JOIN track parent ON parent.id = own.parent_id "
          + $"WHERE s.name GLOB {Quoted(FrameRecordText.NamePrefix + "*")} AND s.dur >= 0 "
          + $"AND IFNULL(parent.name, '') NOT GLOB {Quoted(TraceTrackWriter.RootNamePrefix + "*")} ORDER BY s.ts"
      );
      var records = new List<FrameRecordSlice>(rows.RowCount);
      for (int row = 0; row < rows.RowCount; ++row)
      {
        if (!FrameRecordName.TryParse(rows.Text(row, 2), out uint runId, out ulong frameIndex))
          continue;
        records.Add(
          new FrameRecordSlice(runId, frameIndex, rows.Whole(row, 0), rows.Whole(row, 0) + rows.Whole(row, 1))
          {
            AnimationNs = rows.WholeOrNull(row, 3),
            MarkerPreferredNs = rows.WholeOrNull(row, 4),
            MarkerTargetNs = rows.WholeOrNull(row, 5),
            IntendedDisplayNs = rows.WholeOrNull(row, 6),
            Process = rows.TextOrNull(row, 7),
          }
        );
      }
      return records;
    }

    /// <summary>
    /// A pattern that fits exactly <paramref name="text"/> where a pattern is taken (* and ? as in a file name): the characters a
    /// pattern gives a meaning are written as themselves.
    /// </summary>
    public static string LiteralPattern(string text) => text.Replace("[", "[[]").Replace("*", "[*]").Replace("?", "[?]");

    /// <summary>
    /// What a trace has under the root track of that name, per track name: its slices (a moment is a slice without length there), and its
    /// counter values.
    /// </summary>
    public static (IReadOnlyDictionary<string, long> Slices, IReadOnlyDictionary<string, long> Values) Added(ITraceQueries trace, string root)
    {
      string parent = $"(SELECT id FROM track WHERE name = {Quoted(root)})";
      var slices = trace.Query(
        $"SELECT t.name AS name, COUNT(*) AS count FROM slice s JOIN track t ON s.track_id = t.id WHERE t.parent_id IN {parent} GROUP BY t.name"
      );
      var values = trace.Query(
        $"SELECT t.name AS name, COUNT(*) AS count FROM counter c JOIN counter_track t ON c.track_id = t.id WHERE t.parent_id IN {parent} GROUP BY t.name"
      );
      return (Counts(slices), Counts(values));
    }

    /// <summary>
    /// Where the tracks the tools added are, on the trace's own clock: from the first of their slices, moments and values to the end of
    /// the last; null for a trace without them.
    /// </summary>
    public static (long FirstNs, long LastNs)? AddedRange(ITraceQueries trace)
    {
      string added =
        $"(SELECT t.id FROM track t JOIN track root ON t.parent_id = root.id WHERE root.name GLOB {Quoted(TraceTrackWriter.RootNamePrefix + "*")})";
      var rows = trace.Query(
        "SELECT MIN(first) AS first, MAX(last) AS last FROM ("
          + $"SELECT MIN(ts) AS first, MAX(ts + dur) AS last FROM slice WHERE track_id IN {added} "
          + $"UNION ALL SELECT MIN(ts) AS first, MAX(ts) AS last FROM counter WHERE track_id IN {added})"
      );
      return rows.RowCount > 0 && rows.WholeOrNull(0, 0) is { } first && rows.WholeOrNull(0, 1) is { } last ? (first, last) : null;
    }

    /// <summary>What a trace has to go by. The threads are those of the processes that fit <paramref name="processGlob"/> when one is given.</summary>
    public static TraceInfo Inspect(ITraceQueries trace, string? processGlob = null)
    {
      string pattern = processGlob ?? "*";
      var range = TraceRange(trace);
      var layers = Layers(trace, pattern);
      var frames = layers.GroupBy(layer => layer.Process).ToDictionary(group => group.Key, group => group.Sum(layer => layer.Frames));
      var processes = Processes(trace, pattern).Select(process => process with { TimelineFrames = frames.GetValueOrDefault(process.Name) }).ToList();
      return new TraceInfo(
        range.FirstNs,
        range.LastNs,
        Clocks(trace),
        VBlankSources(trace),
        processes,
        layers,
        processGlob != null ? Threads(trace, pattern) : new List<TraceThreadInfo>(),
        Errors(trace)
      )
      {
        FrameRecords = FrameRecords(trace).Count,
        FrameRecordDetails = FrameRecordDetails(trace),
      };
    }

    /// <summary>The names of the frame record's second part, without their trailing space: what <see cref="FrameRecordDetails"/> counts by.</summary>
    public static IReadOnlyList<string> FrameRecordDetailNames { get; } =
      new[]
      {
        FrameRecordText.WaitNamePrefix,
        FrameRecordText.PresentNamePrefix,
        FrameRecordText.GpuNamePrefix,
        FrameRecordText.DisplayNamePrefix,
        FrameRecordText.NotPresentedNamePrefix,
        FrameRecordText.EnvironmentName,
        FrameRecordText.SwapChainRecreatedName,
        FrameRecordText.SwapIntervalChangeName,
      }
        .Select(name => name.TrimEnd())
        .ToArray();

    /// <summary>
    /// How many slices and moments of the frame record's second part the trace has, by the start of their name (only those it has any of).
    /// Counted only: nothing of the second part is checked against the capture.
    /// </summary>
    public static IReadOnlyDictionary<string, long> FrameRecordDetails(ITraceQueries trace)
    {
      var counts = new Dictionary<string, long>();
      foreach (string name in FrameRecordDetailNames)
      {
        // A name with ids has a space after the start; one without is the start itself
        var rows = trace.Query($"SELECT COUNT(*) AS count FROM slice WHERE name = {Quoted(name)} OR name GLOB {Quoted(name + " *")}");
        long count = rows.RowCount > 0 ? rows.Whole(0, 0) : 0;
        if (count > 0)
          counts[name] = count;
      }
      return counts;
    }

    private static Dictionary<string, long> Counts(TraceQueryResult rows)
    {
      var counts = new Dictionary<string, long>(rows.RowCount, System.StringComparer.Ordinal);
      for (int row = 0; row < rows.RowCount; ++row)
        counts[rows.Text(row, 0)] = rows.Whole(row, 1);
      return counts;
    }
  }
}
