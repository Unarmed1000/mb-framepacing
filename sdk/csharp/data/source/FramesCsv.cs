//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Reads and writes a run's frames CSV (run-<id>-frames.csv): a header line, then one line per presented frame, comma separated, UTF-8 without
//* a byte order mark. Reading goes by column name. Camera captures add two columns. Every time is written as its 100 ns ticks, a whole
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
      "segment,frameIndex,animationTicks,firstCaptureIndex,firstSeenTicks,onScreenTicks,captures,skippedBefore,displayDeltaTicks,animationDeltaTicks,animationErrorTicks,driftTicks,flags,"
      + "intendedDisplayTicks,markerTargetTicks,targetTicks,markerPreferredTicks,preferredTicks,pacingErrorTicks,predictionErrorTicks,latenessTicks,lastSeenTicks,cpuStartTicks,cpuBusyTicks,frameTimeTicks,cpuWaitTicks,olderFrames";

    /// <summary>The columns an EXPERIMENTAL camera capture adds.</summary>
    public const string CameraColumns = ",mainMarkerFirstSeenTicks,scanoutDelayTicks";

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
        line.Add(row.AnimationTime.Ticks);
        line.Add(row.FirstCaptureIndex);
        line.Add(row.FirstSeenTime.Ticks);
        line.Add(row.OnScreen.Ticks);
        line.Add(row.Captures);
        line.Add(row.SkippedBefore);
        line.Add(row.DisplayDelta?.Ticks);
        line.Add(row.AnimationDelta?.Ticks);
        line.Add(row.AnimationError?.Ticks);
        line.Add(row.Drift.Ticks);
        line.Cell();
        for (int i = 0; i < row.Flags.Count; ++i)
        {
          if (i > 0)
            line.Append('|');
          line.Append(row.Flags[i]);
        }
        line.Add(row.IntendedDisplayTime?.Ticks);
        line.Add((ulong?)row.MarkerTargetFrameTime?.Ticks);
        line.Add(row.TargetFrameTime?.Ticks);
        line.Add((ulong?)row.MarkerPreferredFrameTime?.Ticks);
        line.Add(row.PreferredFrameTime?.Ticks);
        line.Add(row.PacingError?.Ticks);
        line.Add(row.PredictionError?.Ticks);
        line.Add(row.Lateness?.Ticks);
        line.Add(row.LastSeenTime?.Ticks);
        line.Add(row.CpuStartTime?.Ticks);
        line.Add((ulong?)row.CpuBusy?.Ticks);
        line.Add(row.FrameTime?.Ticks);
        line.Add(row.CpuWait?.Ticks);
        line.Cell();
        for (int i = 0; i < row.OlderFrames.Count; ++i)
        {
          if (i > 0)
            line.Append('|');
          line.Append(row.OlderFrames[i].FrameIndex);
          line.Append('@');
          line.Append(row.OlderFrames[i].CaptureTime.Ticks);
        }
        if (camera)
        {
          line.Add(row.MainMarkerFirstSeenTime?.Ticks);
          line.Add(row.ScanoutDelay?.Ticks);
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
      int animation = Column("animationTicks");
      int firstCapture = Column("firstCaptureIndex");
      int firstSeen = Column("firstSeenTicks");
      int onScreen = Column("onScreenTicks");
      int captures = Column("captures");
      int skipped = Column("skippedBefore");
      int display = Column("displayDeltaTicks");
      int animationDelta = Column("animationDeltaTicks");
      int error = Column("animationErrorTicks");
      int drift = Column("driftTicks");
      int flags = Column("flags");
      int intended = Column("intendedDisplayTicks");
      int markerTarget = Column("markerTargetTicks");
      int target = Column("targetTicks");
      int markerPreferred = Column("markerPreferredTicks");
      int preferred = Column("preferredTicks");
      int pacing = Column("pacingErrorTicks");
      int prediction = Column("predictionErrorTicks");
      int lateness = Column("latenessTicks");
      int lastSeen = Column("lastSeenTicks");
      int cpuStart = Column("cpuStartTicks");
      int cpuBusy = Column("cpuBusyTicks");
      int frameTime = Column("frameTimeTicks");
      int cpuWait = Column("cpuWaitTicks");
      int older = Column("olderFrames");
      int mainSeen = Column("mainMarkerFirstSeenTicks");
      int scanoutDelay = Column("scanoutDelayTicks");

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
              row.Span32(markerTarget),
              row.Span(target),
              row.Span32(markerPreferred),
              row.Span(preferred),
              row.Span(pacing),
              row.Span(prediction),
              row.Span(lateness),
              row.Time(lastSeen),
              row.Time(cpuStart),
              row.Span32(cpuBusy),
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

    /// <summary>A flags cell that is not empty: names separated by <c>|</c>.</summary>
    private static string[] Flags(string cell)
    {
      string[] flags = cell.Split('|');
      if (Array.IndexOf(flags, string.Empty) >= 0)
        throw new InvalidDataException($"An empty entry in flags '{cell}'");
      return flags;
    }

    /// <summary>The olderFrames cell: <c>frameIndex@captureTicks</c> entries separated by <c>|</c>, empty when none.</summary>
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
        frames[i] = new OlderFrame(CsvRow.ParseULong(entry[..at], ulong.MaxValue), new TickCount64(CsvRow.ParseLong(entry[(at + 1)..])));
      }
      return frames;
    }
  }
}
