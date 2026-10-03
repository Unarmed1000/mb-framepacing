//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Reads and writes captures.csv: a header line, then one line per capture index, comma separated, UTF-8 without a byte order mark. Reading
//* goes by column name. Every time is written as its 100 ns ticks, a whole number.
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
  public static class CapturesCsv
  {
    public const string Header =
      "captureIndex,captureTicks,status,kind,runId,frameIndex,animationTicks,sourceDropsBefore,missedBefore,syncRunId,syncFrameIndex,hostTicks,deviceTicks,payloadHex";

    public static void Write(string path, IEnumerable<CaptureCsvRow> rows)
    {
      using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
      Write(writer, rows);
    }

    public static void Write(TextWriter writer, IEnumerable<CaptureCsvRow> rows)
    {
      writer.WriteLine(Header);
      // One line at a time in a buffer of its own: no string per cell (an hour at 240 Hz has 864,000 lines)
      var line = new CsvLineWriter();
      foreach (var row in rows)
      {
        line.Add(row.CaptureIndex);
        line.Add(row.CaptureTime?.Ticks);
        line.Add(row.CaptureStatus);
        line.Add(row.Kind ?? string.Empty);
        line.Add((ulong?)row.RunId);
        line.Add(row.FrameIndex);
        line.Add(row.AnimationTime?.Ticks);
        line.Add(row.SourceDropsBefore);
        line.Add(row.MissedBefore);
        line.Add((ulong?)row.SyncRunId);
        line.Add(row.SyncFrameIndex);
        line.Add(row.HostTime?.Ticks);
        line.Add(row.DeviceTime?.Ticks);
        line.AddHex(row.Payload);
        line.End(writer);
      }
    }

    /// <summary>Read captures.csv. Throws <see cref="InvalidDataException"/> for content that is not one.</summary>
    public static IReadOnlyList<CaptureCsvRow> Read(string path)
    {
      using var reader = new StreamReader(path);
      return Read(reader, path);
    }

    public static IReadOnlyList<CaptureCsvRow> Read(TextReader reader, string name = "captures.csv")
    {
      // Line by line from the reader's buffer, the cells read where they are: no string per line or per cell
      using var lines = new CsvLineReader(reader);
      if (!lines.TryReadHeader(out string? header))
        throw new InvalidDataException($"'{name}' is empty");
      var column = CsvRow.Columns(header, out int columnCount);
      int Column(string columnName) => column.TryGetValue(columnName, out int index) ? index : -1;
      int captureIndex = Column("captureIndex");
      int capture = Column("captureTicks");
      int status = Column("status");
      int kind = Column("kind");
      int runId = Column("runId");
      int frameIndex = Column("frameIndex");
      int animation = Column("animationTicks");
      int sourceDrops = Column("sourceDropsBefore");
      int missed = Column("missedBefore");
      int syncRunId = Column("syncRunId");
      int syncFrameIndex = Column("syncFrameIndex");
      int host = Column("hostTicks");
      int device = Column("deviceTicks");
      int payload = Column("payloadHex");

      var rows = new List<CaptureCsvRow>();
      // Every line names one of a few statuses and kinds: each text is made once
      var names = new CsvTextCache<string>(text => text);
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
          rows.Add(
            new CaptureCsvRow(
              row.RequiredLong(captureIndex),
              row.Time(capture),
              row.Cell(status) is { Length: > 0 } statusText ? names.Get(statusText) : string.Empty,
              row.Cell(kind) is { Length: > 0 } kindText ? names.Get(kindText) : null,
              row.UInt(runId),
              row.ULong(frameIndex),
              row.Span(animation),
              row.Long(sourceDrops) ?? 0,
              row.Long(missed) ?? 0,
              row.UInt(syncRunId),
              row.ULong(syncFrameIndex),
              row.Time(host),
              row.Time(device),
              row.Cell(payload) is { Length: > 0 } hex ? FromHex(hex) : null
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

    private static byte[] FromHex(ReadOnlySpan<char> hex)
    {
      try
      {
        return Convert.FromHexString(hex);
      }
      catch (FormatException exception)
      {
        throw new InvalidDataException($"'{hex}' is not hexadecimal bytes", exception);
      }
    }
  }
}
