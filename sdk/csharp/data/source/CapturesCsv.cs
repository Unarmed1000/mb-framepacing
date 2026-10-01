//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Reads and writes captures.csv: a header line, then one line per capture index, comma separated, UTF-8 without a byte order mark. Reading
//* goes by column name. Camera captures add a column.
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
      "captureIndex,captureMs,status,kind,runId,frameIndex,animationMs,sourceDropsBefore,missedBefore,syncRunId,syncFrameIndex,hostMs,deviceMs,payloadHex";

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
            row.CaptureTime is { } capture ? Milliseconds.Format(capture) : string.Empty,
            row.Status,
            row.Kind ?? string.Empty,
            row.RunId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            row.FrameIndex?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            row.AnimationTime is { } animation ? Milliseconds.Format(animation) : string.Empty,
            row.SourceDropsBefore.ToString(CultureInfo.InvariantCulture),
            row.MissedBefore.ToString(CultureInfo.InvariantCulture),
            row.SyncRunId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            row.SyncFrameIndex?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            row.HostTime is { } host ? Milliseconds.Format(host) : string.Empty,
            row.DeviceTime is { } device ? Milliseconds.Format(device) : string.Empty,
            row.Payload != null ? Convert.ToHexString(row.Payload) : string.Empty
          )
        );
      }
    }

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
      int captureMs = Column("captureMs");
      int status = Column("status");
      int kind = Column("kind");
      int runId = Column("runId");
      int frameIndex = Column("frameIndex");
      int animation = Column("animationMs");
      int sourceDrops = Column("sourceDropsBefore");
      int missed = Column("missedBefore");
      int syncRunId = Column("syncRunId");
      int syncFrameIndex = Column("syncFrameIndex");
      int host = Column("hostMs");
      int device = Column("deviceMs");
      int payload = Column("payloadHex");

      var rows = new List<CaptureCsvRow>();
      string? line;
      while ((line = reader.ReadLine()) != null)
      {
        if (line.Length == 0)
          continue;
        var row = new CsvRow(line.Split(','));
        rows.Add(
          new CaptureCsvRow(
            long.Parse(row.Cell(captureIndex), CultureInfo.InvariantCulture),
            row.Time(captureMs),
            row.Cell(status),
            row.Cell(kind) is { Length: > 0 } kindText ? kindText : null,
            (uint?)row.Long(runId),
            row.ULong(frameIndex),
            row.Span(animation),
            row.Long(sourceDrops) ?? 0,
            row.Long(missed) ?? 0,
            (uint?)row.Long(syncRunId),
            row.ULong(syncFrameIndex),
            row.Time(host),
            row.Time(device),
            row.Cell(payload) is { Length: > 0 } hex ? Convert.FromHexString(hex) : null
          )
        );
      }
      return rows;
    }
  }
}
