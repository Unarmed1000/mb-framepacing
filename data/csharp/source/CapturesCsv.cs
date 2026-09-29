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
    public const string Header = "captureIndex,captureMs,status,kind,runId,frameIndex,animationMs,sourceDropBefore,hostMs,deviceMs,payloadHex";

    /// <summary>The column an EXPERIMENTAL camera capture adds.</summary>
    public const string CameraColumns = ",secondZoneFrameIndex";

    public static void Write(string path, IEnumerable<CaptureCsvRow> rows, bool camera)
    {
      using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
      Write(writer, rows, camera);
    }

    public static void Write(TextWriter writer, IEnumerable<CaptureCsvRow> rows, bool camera)
    {
      writer.WriteLine(Header + (camera ? CameraColumns : string.Empty));
      foreach (var row in rows)
      {
        writer.WriteLine(
          string.Join(
            ',',
            row.CaptureIndex.ToString(CultureInfo.InvariantCulture),
            row.CaptureTicks is { } capture ? Milliseconds.Format(capture) : string.Empty,
            row.Status,
            row.Kind ?? string.Empty,
            row.RunId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            row.FrameIndex?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            row.AnimationTicks is { } animation ? Milliseconds.Format(animation) : string.Empty,
            row.SourceDropBefore ? "1" : "0",
            row.HostTicks is { } host ? Milliseconds.Format(host) : string.Empty,
            row.DeviceTicks is { } device ? Milliseconds.Format(device) : string.Empty,
            row.Payload != null ? Convert.ToHexString(row.Payload) : string.Empty
          ) + (camera ? "," + (row.SecondZoneFrameIndex?.ToString(CultureInfo.InvariantCulture) ?? string.Empty) : string.Empty)
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
      int sourceDrop = Column("sourceDropBefore");
      int host = Column("hostMs");
      int device = Column("deviceMs");
      int payload = Column("payloadHex");
      int secondZone = Column("secondZoneFrameIndex");

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
            row.Ticks(captureMs),
            row.Cell(status),
            row.Cell(kind) is { Length: > 0 } kindText ? kindText : null,
            (uint?)row.Long(runId),
            row.ULong(frameIndex),
            row.Ticks(animation),
            row.Cell(sourceDrop) == "1",
            row.Ticks(host),
            row.Ticks(device),
            row.Cell(payload) is { Length: > 0 } hex ? Convert.FromHexString(hex) : null,
            row.ULong(secondZone)
          )
        );
      }
      return rows;
    }
  }
}
