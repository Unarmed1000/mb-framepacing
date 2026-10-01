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
using System.Globalization;
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
      foreach (var row in rows)
      {
        writer.WriteLine(
          string.Join(
            ',',
            row.CaptureIndex.ToString(CultureInfo.InvariantCulture),
            row.CaptureTime?.Ticks.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            row.Status,
            row.Kind ?? string.Empty,
            row.RunId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            row.FrameIndex?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            row.AnimationTime?.Ticks.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            row.SourceDropsBefore.ToString(CultureInfo.InvariantCulture),
            row.MissedBefore.ToString(CultureInfo.InvariantCulture),
            row.SyncRunId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            row.SyncFrameIndex?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            row.HostTime?.Ticks.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            row.DeviceTime?.Ticks.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            row.Payload != null ? Convert.ToHexString(row.Payload) : string.Empty
          )
        );
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
      var column = CsvRow.Columns(reader.ReadLine() ?? throw new InvalidDataException($"'{name}' is empty"));
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
      int lineNumber = 1;
      string? line;
      while ((line = reader.ReadLine()) != null)
      {
        ++lineNumber;
        if (line.Length == 0)
          continue;
        var row = new CsvRow(line.Split(','));
        try
        {
          rows.Add(
            new CaptureCsvRow(
              row.RequiredLong(captureIndex),
              row.Time(capture),
              row.Cell(status),
              row.Cell(kind) is { Length: > 0 } kindText ? kindText : null,
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

    private static byte[] FromHex(string hex)
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
