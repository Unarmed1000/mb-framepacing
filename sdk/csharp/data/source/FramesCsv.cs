//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Reads and writes a run's frames CSV (run-<id>-frames.csv): a header line, then one line per presented frame, comma separated, UTF-8 without
//* a byte order mark. Reading goes by column name. Camera captures add two columns. Every time is written as its nanoseconds, a whole
//* number: a marker's value is in the file exactly as the marker carried it.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MB.FramePacing.Data
{
  public static class FramesCsv
  {
    public const string Header =
      "segment,frameIndex,animationNs,firstCaptureIndex,firstSeenNs,onScreenNs,captures,skippedBefore,displayDeltaNs,animationDeltaNs,animationErrorNs,driftNs,flags,"
      + "intendedDisplayNs,markerTargetNs,targetNs,markerPreferredNs,preferredNs,pacingErrorNs,predictionErrorNs,latenessNs,lastSeenNs,cpuStartNs,cpuBusyNs,frameTimeNs,cpuWaitNs,olderFrames";

    /// <summary>The columns an EXPERIMENTAL camera capture adds.</summary>
    public const string CameraColumns = ",mainMarkerFirstSeenNs,scanoutDelayNs";

    public static void Write(string path, IEnumerable<FrameRow> rows, bool camera)
    {
      using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
      Write(writer, rows, camera);
    }

    public static void Write(TextWriter writer, IEnumerable<FrameRow> rows, bool camera)
    {
      writer.WriteLine(Header + (camera ? CameraColumns : string.Empty));
      // One line at a time in a buffer of its own: no string per cell (a run of an hour has a million lines)
      var line = new CsvLineWriter();
      foreach (var row in rows)
      {
        line.Add(row.Segment);
        line.Add(row.FrameIndex);
        line.Add(row.AnimationTime.Nanoseconds);
        line.Add(row.FirstCaptureIndex);
        line.Add(row.FirstSeenTime.Nanoseconds);
        line.Add(row.OnScreen.Nanoseconds);
        line.Add(row.Captures);
        line.Add(row.SkippedBefore);
        line.Add(row.DisplayDelta?.Nanoseconds);
        line.Add(row.AnimationDelta?.Nanoseconds);
        line.Add(row.AnimationError?.Nanoseconds);
        line.Add(row.Drift.Nanoseconds);
        line.Cell();
        for (int i = 0; i < row.Flags.Count; ++i)
        {
          if (i > 0)
            line.Append('|');
          line.Append(row.Flags[i]);
        }
        line.Add(row.IntendedDisplayTime?.Nanoseconds);
        line.Add(MarkerNanoseconds(row.MarkerTargetFrameTime, "markerTargetNs"));
        line.Add(row.TargetFrameTime?.Nanoseconds);
        line.Add(MarkerNanoseconds(row.MarkerPreferredFrameTime, "markerPreferredNs"));
        line.Add(row.PreferredFrameTime?.Nanoseconds);
        line.Add(row.PacingError?.Nanoseconds);
        line.Add(row.PredictionError?.Nanoseconds);
        line.Add(row.Lateness?.Nanoseconds);
        line.Add(row.LastSeenTime?.Nanoseconds);
        line.Add(row.CpuStartTime?.Nanoseconds);
        line.Add(MarkerNanoseconds(row.CpuBusy, "cpuBusyNs"));
        line.Add(row.FrameTime?.Nanoseconds);
        line.Add(row.CpuWait?.Nanoseconds);
        line.Cell();
        for (int i = 0; i < row.OlderFrames.Count; ++i)
        {
          if (i > 0)
            line.Append('|');
          line.Append(row.OlderFrames[i].FrameIndex);
          line.Append('@');
          line.Append(row.OlderFrames[i].CaptureTime.Nanoseconds);
        }
        if (camera)
        {
          line.Add(row.MainMarkerFirstSeenTime?.Nanoseconds);
          line.Add(row.ScanoutDelay?.Nanoseconds);
        }
        line.End(writer);
      }
    }

    /// <summary>Read a frames CSV. Throws <see cref="InvalidDataException"/> for content that is not one.</summary>
    public static IReadOnlyList<FrameRow> Read(string path)
    {
      using var reader = new StreamReader(path);
      return Read(reader, path);
    }

    public static IReadOnlyList<FrameRow> Read(TextReader reader, string name = "frames CSV")
    {
      // Line by line from the reader's buffer, the cells read where they are: no string per line or per cell
      using var lines = new CsvLineReader(reader);
      if (!lines.TryReadHeader(out string? header))
        throw new InvalidDataException($"'{name}' is empty");
      var column = CsvRow.Columns(header, out int columnCount);
      int Column(string columnName) => column.TryGetValue(columnName, out int index) ? index : -1;
      int segment = Column("segment");
      int frameIndex = Column("frameIndex");
      int animation = Column("animationNs");
      int firstCapture = Column("firstCaptureIndex");
      int firstSeen = Column("firstSeenNs");
      int onScreen = Column("onScreenNs");
      int captures = Column("captures");
      int skipped = Column("skippedBefore");
      int display = Column("displayDeltaNs");
      int animationDelta = Column("animationDeltaNs");
      int error = Column("animationErrorNs");
      int drift = Column("driftNs");
      int flags = Column("flags");
      int intended = Column("intendedDisplayNs");
      int markerTarget = Column("markerTargetNs");
      int target = Column("targetNs");
      int markerPreferred = Column("markerPreferredNs");
      int preferred = Column("preferredNs");
      int pacing = Column("pacingErrorNs");
      int prediction = Column("predictionErrorNs");
      int lateness = Column("latenessNs");
      int lastSeen = Column("lastSeenNs");
      int cpuStart = Column("cpuStartNs");
      int cpuBusy = Column("cpuBusyNs");
      int frameTime = Column("frameTimeNs");
      int cpuWait = Column("cpuWaitNs");
      int older = Column("olderFrames");
      int mainSeen = Column("mainMarkerFirstSeenNs");
      int scanoutDelay = Column("scanoutDelayNs");

      var rows = new List<FrameRow>();
      // A run repeats a few sets of flags on every line: each is made once
      var flagSets = new CsvTextCache<string[]>(Flags);
      int cellRoom = CsvRow.CellRoom(columnCount);
      Span<Range> cells = cellRoom <= MaxCellsOnStack ? stackalloc Range[MaxCellsOnStack] : new Range[cellRoom];
      cells = cells[..cellRoom];
      int lineNumber = 1;
      while (lines.TryReadLine(out var line))
      {
        ++lineNumber;
        if (line.Length == 0)
          continue;
        var row = new CsvRow(line, cells);
        try
        {
          var flagText = row.Cell(flags);
          rows.Add(
            new FrameRow(
              row.RequiredInt(segment),
              row.RequiredULong(frameIndex),
              row.RequiredSpan(animation),
              row.RequiredLong(firstCapture),
              row.RequiredTime(firstSeen),
              row.RequiredSpan(onScreen),
              row.RequiredInt(captures),
              row.RequiredULong(skipped),
              row.Span(display),
              row.Span(animationDelta),
              row.Span(error),
              row.RequiredSpan(drift),
              flagText.Length == 0 ? Array.Empty<string>() : flagSets.Get(flagText),
              row.Time(intended),
              row.MarkerDuration(markerTarget),
              row.Span(target),
              row.MarkerDuration(markerPreferred),
              row.Span(preferred),
              row.Span(pacing),
              row.Span(prediction),
              row.Span(lateness),
              row.Time(lastSeen),
              row.Time(cpuStart),
              row.MarkerDuration(cpuBusy),
              row.Span(frameTime),
              row.Span(cpuWait),
              OlderFrames(row.Cell(older)),
              row.Time(mainSeen),
              row.Span(scanoutDelay)
            )
          );
        }
        catch (InvalidDataException exception)
        {
          throw new InvalidDataException($"'{name}' line {lineNumber}: {exception.Message}", exception);
        }
      }
      return rows;
    }

    /// <summary>The cells a line may have for the room to be on the stack.</summary>
    private const int MaxCellsOnStack = 64;

    /// <summary>
    /// A marker's duration as its column holds it, a u32: null is an empty cell. Throws <see cref="ArgumentOutOfRangeException"/> for a
    /// duration no marker carries (more than 4294967295 ns), which no reader would take.
    /// </summary>
    private static ulong? MarkerNanoseconds(NanosecondTimeDuration? duration, string column)
    {
      if (duration is not { } value)
        return null;
      if (value.UnsignedNanoseconds > uint.MaxValue)
        throw new ArgumentOutOfRangeException(nameof(duration), value.Nanoseconds, $"{column} is a 32-bit number of nanoseconds");
      return value.UnsignedNanoseconds;
    }

    /// <summary>A flags cell that is not empty: names separated by <c>|</c>.</summary>
    private static string[] Flags(string cell)
    {
      string[] flags = cell.Split('|');
      if (Array.IndexOf(flags, string.Empty) >= 0)
        throw new InvalidDataException($"An empty entry in flags '{cell}'");
      return flags;
    }

    /// <summary>The olderFrames cell: <c>frameIndex@captureNs</c> entries separated by <c>|</c>, empty when none.</summary>
    private static IReadOnlyList<OlderFrame> OlderFrames(ReadOnlySpan<char> cell)
    {
      if (cell.Length == 0)
        return Array.Empty<OlderFrame>();
      var frames = new OlderFrame[cell.Count('|') + 1];
      for (int i = 0; i < frames.Length; ++i)
      {
        int bar = cell.IndexOf('|');
        var entry = bar >= 0 ? cell[..bar] : cell;
        cell = bar >= 0 ? cell[(bar + 1)..] : default;
        int at = entry.IndexOf('@');
        if (at <= 0)
          throw new InvalidDataException($"Invalid olderFrames entry '{entry}'");
        frames[i] = new OlderFrame(CsvRow.ParseULong(entry[..at], ulong.MaxValue), new NanosecondTickCount(CsvRow.ParseLong(entry[(at + 1)..])));
      }
      return frames;
    }
  }
}
