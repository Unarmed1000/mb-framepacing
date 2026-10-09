//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL. Reads a frame log of the frame pacer's first integration (its sample's own CSV: a row per frame with the moments of the
//* frame on the application's CPU clock, in ticks of 100 ns), alone or inside a capture session's zip (pacer-captures). The pacer's own
//* simulation writes a log with the same columns (pacer-sim --loop).
//*
//* Every cell that is a whole number is kept; a moment's zero is "not known", as an empty cell. The frames are the rows that have a start.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace MB.FramePacing.Charts.Stages
{
  /// <summary>Experimental. Reads the frame logs of the pacer's first integration.</summary>
  public static class FrameLog
  {
    /// <summary>The last frames of a log have no display time yet: their results had not come back when the run ended.</summary>
    public const int SkipLast = 8;

    /// <summary>What a reader of a chart has to know about a log that has the values of a frame's start one row early.</summary>
    public const string RowEarlyNote =
      "This log has the values of a frame's start one row early (an OpenGL ES log of the sample before its fix): they are read from the row before.";

    /// <summary>A moment of a frame is a time on the CPU clock: zero is "not known", as an empty cell.</summary>
    private static readonly HashSet<string> g_moments = new HashSet<string>(StringComparer.Ordinal)
    {
      "hostCpuStartTicks",
      "hostUpdateEndTicks",
      "frameWaitStartTicks",
      "acquireCallTicks",
      "acquireReturnTicks",
      "frameStartTicks",
      "markerDrawTicks",
      "submitCallTicks",
      "submitReturnTicks",
      "frameSlotWaitBeginTicks",
      "frameSlotWaitEndTicks",
      "frameWaitTargetTicks",
      "presentWaitBeginTicks",
      "presentWaitTargetTicks",
      "presentWaitEndTicks",
      "endFrameTicks",
      "presentCallTicks",
      "presentReturnTicks",
      "gpuWorkBeginTicks",
      "gpuWorkEndTicks",
      "firstPixelOutTicks",
      "feedbackDisplayTicks",
      "displayVSyncTicks",
      "intendedDisplayTicks",
      "nextFrameStartTicks",
    };

    /// <summary>
    /// The sample's OpenGL ES logs up to 2026-10-06 have what it logs at a frame's start one row early (the row is opened later in that
    /// loop): these columns of frame N are in the row of frame N - 1. Such a log is known by its pacer frame id, which is two ahead of the
    /// row's frame index where a correct log's is one ahead.
    /// </summary>
    private static readonly string[] g_rowEarly =
    {
      "frameStartTicks",
      "frameWaitStartTicks",
      "pacerOn",
      "swapInterval",
      "preferredSwapInterval",
      "pacerChange",
      "animationStepTicks",
      "pacerWindowFrames",
      "pacerWindowLateFrames",
      "pacerWindowAverageWorkTicks",
      "pacerWindowSpanTicks",
      "pacerWindowFull",
      "pacerFrameId",
      "nextFrameStartTicks",
      "pacerFeedbackOn",
      "pacerFeedbackUsed",
      "pacerFeedbackRefused",
      "pacerFeedbackNotShown",
      "pacerFeedbackMissing",
      "pacerFeedbackLateRefreshes",
      "cpuLoadMs",
      "gpuLoadSteps",
    };

    /// <summary>
    /// The frames of a log's <paramref name="text"/> that have a start, and what a reader of the chart has to know about how the log was
    /// read.
    /// </summary>
    public static (IReadOnlyList<FrameLogFrame> Frames, IReadOnlyList<string> Notes) Read(string text)
    {
      var lines = text.Split('\n');
      var header = lines.Length > 0 ? Cells(lines[0]) : Array.Empty<string>();
      var rows = new List<Dictionary<string, long>>();
      for (int line = 1; line < lines.Length; ++line)
      {
        var cells = Cells(lines[line]);
        if (cells.Length != header.Length)
          continue;
        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        for (int column = 0; column < header.Length; ++column)
        {
          if (
            IsWholeNumber(cells[column]) && long.TryParse(cells[column], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value)
          )
          {
            if (!(value == 0 && g_moments.Contains(header[column])))
              values[header[column]] = value;
          }
        }
        if (values.ContainsKey("frameIndex"))
          rows.Add(values);
      }

      var notes = new List<string>();
      var ahead = rows.Where(values => values.ContainsKey("pacerFrameId")).Select(values => values["pacerFrameId"] - values["frameIndex"]).ToList();
      if (ahead.Count > 0 && FrameLogStages.Median(ahead) == 2)
      {
        for (int position = rows.Count - 1; position >= 0; --position)
        {
          foreach (string name in g_rowEarly)
          {
            rows[position].Remove(name);
            if (position > 0 && rows[position - 1].TryGetValue(name, out long before))
              rows[position][name] = before;
          }
        }
        notes.Add(RowEarlyNote);
      }
      var frames = rows.Where(values => values.ContainsKey("frameStartTicks"))
        .Select(values => new FrameLogFrame(values["frameIndex"], values))
        .ToList();
      return (frames, notes);
    }

    /// <summary>The frame logs of a capture session's zip, as &lt;folder&gt;/&lt;name&gt;.</summary>
    public static IReadOnlyList<string> RunsIn(string zipPath)
    {
      using var archive = ZipFile.OpenRead(zipPath);
      return archive
        .Entries.Select(entry => entry.FullName)
        .Where(name =>
          name.EndsWith(".csv", StringComparison.Ordinal) && !name.EndsWith(".events.csv", StringComparison.Ordinal) && name.Count(c => c == '/') == 2
        )
        .Select(name => name[(name.IndexOf('/', StringComparison.Ordinal) + 1)..^".csv".Length])
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToList();
    }

    /// <summary>
    /// The run's name, its frames and the notes on how it was read: a CSV as it is, or the run named <paramref name="run"/> in a session's
    /// zip. A zip that does not have exactly that run is an <see cref="ArgumentException"/>.
    /// </summary>
    public static (string Name, IReadOnlyList<FrameLogFrame> Frames, IReadOnlyList<string> Notes) ReadSource(string source, string run)
    {
      if (!string.Equals(Path.GetExtension(source), ".zip", StringComparison.OrdinalIgnoreCase))
      {
        var (frames, notes) = Read(File.ReadAllText(source, Encoding.UTF8));
        return (Path.GetFileNameWithoutExtension(source), frames, notes);
      }
      using var archive = ZipFile.OpenRead(source);
      var matches = archive.Entries.Where(entry => entry.FullName.EndsWith($"/{run}.csv", StringComparison.Ordinal)).ToList();
      if (matches.Count != 1)
        throw new ArgumentException($"{Path.GetFileName(source)} has {matches.Count} runs named '{run}': use --list for the names");
      using var reader = new StreamReader(matches[0].Open(), Encoding.UTF8);
      var read = Read(reader.ReadToEnd());
      return (run, read.Frames, read.Notes);
    }

    /// <summary>A line's cells; a line end's carriage return is not part of the last.</summary>
    private static string[] Cells(string line) => line.TrimEnd('\r').Split(',');

    /// <summary>Digits, with '-' in front or not: what the log writes a whole number as.</summary>
    private static bool IsWholeNumber(string cell)
    {
      var digits = cell.AsSpan().TrimStart('-');
      if (digits.IsEmpty)
        return false;
      foreach (char c in digits)
      {
        if (c is < '0' or > '9')
          return false;
      }
      return true;
    }
  }
}
