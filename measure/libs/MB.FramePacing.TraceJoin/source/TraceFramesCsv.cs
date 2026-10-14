//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* run-<id>-trace.csv: a header line, then a line per presented frame in display order with what a trace says of it (TraceFrame;
//* measure/doc/perfetto.md "The frames CSV"). The same bytes as tools/trace_augment.py --frames writes: lines end in a line feed, a cell
//* that holds a comma, a quote or a line end is in quotes with its quotes doubled, true and false are written out, and a whole number is
//* its digits with a '-' in front when negative.
//*
//* The file is the tools' own and experimental: it is not part of the analysis output format (sdk/doc/analysis-output-format.md), whose
//* readers never open it.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MB.FramePacing.TraceJoin
{
  /// <summary>Writes and reads the per-frame join of a capture and a trace.</summary>
  public static class TraceFramesCsv
  {
    /// <summary>What a run's file name ends in, after the run's prefix (run-&lt;id&gt;).</summary>
    public const string FileSuffix = "-trace.csv";

    /// <summary>The columns, in the order of <see cref="TraceFrame"/>'s members: named as the analysis output names its own.</summary>
    public static IReadOnlyList<string> Columns { get; } =
      new[]
      {
        "frameIndex",
        "displayTraceNs",
        "cpuStartTraceNs",
        "cpuEndTraceNs",
        "vblankTraceNs",
        "displayMinusVBlankNs",
        "surfaceFrameToken",
        "displayFrameToken",
        "layerName",
        "presentType",
        "jankType",
        "jankTag",
        "onTimeFinish",
        "gpuComposition",
        "systemPresentTraceNs",
        "displayMinusSystemPresentNs",
        "cpuRunningNs",
        "cpuRunnableNs",
        "cpuSleepingNs",
      };

    /// <summary>The file name of the run whose files begin with <paramref name="runFilePrefix"/>.</summary>
    public static string FileName(string runFilePrefix) => runFilePrefix + FileSuffix;

    /// <summary>The rows as the file's text.</summary>
    public static string ToText(IReadOnlyList<TraceFrame> rows)
    {
      var text = new StringBuilder();
      text.AppendJoin(',', Columns).Append('\n');
      foreach (var row in rows)
      {
        text.Append(row.FrameIndex.ToString(CultureInfo.InvariantCulture));
        Number(text, row.DisplayTraceNs);
        Number(text, row.CpuStartTraceNs);
        Number(text, row.CpuEndTraceNs);
        Number(text, row.VBlankTraceNs);
        Number(text, row.DisplayMinusVBlankNs);
        Number(text, row.SurfaceFrameToken);
        Number(text, row.DisplayFrameToken);
        Text(text, row.LayerName);
        Text(text, row.PresentType);
        Text(text, row.JankType);
        Text(text, row.JankTag);
        Flag(text, row.OnTimeFinish);
        Flag(text, row.GpuComposition);
        Number(text, row.SystemPresentTraceNs);
        Number(text, row.DisplayMinusSystemPresentNs);
        Number(text, row.CpuRunningNs);
        Number(text, row.CpuRunnableNs);
        Number(text, row.CpuSleepingNs);
        text.Append('\n');
      }
      return text.ToString();
    }

    /// <summary>Write the rows to <paramref name="path"/> (UTF-8, no byte order mark).</summary>
    public static void Write(string path, IReadOnlyList<TraceFrame> rows) => File.WriteAllText(path, ToText(rows), new UTF8Encoding(false));

    /// <summary>Read a file. Content that is not the format's is an <see cref="InvalidDataException"/> with the file and the line.</summary>
    public static IReadOnlyList<TraceFrame> Read(string path) => Parse(File.ReadAllText(path, Encoding.UTF8), path);

    /// <summary>The rows of a file's <paramref name="text"/>; <paramref name="name"/> is the file's name for an error.</summary>
    public static IReadOnlyList<TraceFrame> Parse(string text, string name)
    {
      var records = Records(text, name);
      if (records.Count == 0)
        throw new InvalidDataException($"{name}: the file has no header line");
      var header = records[0];
      var index = new int[Columns.Count];
      for (int column = 0; column < Columns.Count; ++column)
      {
        index[column] = header.IndexOf(Columns[column]);
        if (index[column] < 0)
          throw new InvalidDataException($"{name}:1: the header has no column '{Columns[column]}'");
      }

      var rows = new List<TraceFrame>(records.Count - 1);
      for (int line = 1; line < records.Count; ++line)
      {
        var cells = records[line];
        if (cells.Count != header.Count)
          throw new InvalidDataException($"{name}:{line + 1}: {cells.Count} cells where the header has {header.Count}");
        string Cell(int column) => cells[index[column]];
        string where = $"{name}:{line + 1}";
        rows.Add(
          new TraceFrame(
            ParseUnsigned(Cell(0), where, Columns[0]),
            ParseNumber(Cell(1), where, Columns[1]) ?? throw new InvalidDataException($"{where}: '{Columns[1]}' is empty"),
            ParseNumber(Cell(2), where, Columns[2]),
            ParseNumber(Cell(3), where, Columns[3]),
            ParseNumber(Cell(4), where, Columns[4]),
            ParseNumber(Cell(5), where, Columns[5]),
            ParseNumber(Cell(6), where, Columns[6]),
            ParseNumber(Cell(7), where, Columns[7]),
            ParseText(Cell(8)),
            ParseText(Cell(9)),
            ParseText(Cell(10)),
            ParseText(Cell(11)),
            ParseFlag(Cell(12), where, Columns[12]),
            ParseFlag(Cell(13), where, Columns[13]),
            ParseNumber(Cell(14), where, Columns[14]),
            ParseNumber(Cell(15), where, Columns[15]),
            ParseNumber(Cell(16), where, Columns[16]),
            ParseNumber(Cell(17), where, Columns[17]),
            ParseNumber(Cell(18), where, Columns[18])
          )
        );
      }
      return rows;
    }

    private static void Number(StringBuilder text, long? value)
    {
      text.Append(',');
      if (value is { } known)
        text.Append(known.ToString(CultureInfo.InvariantCulture));
    }

    private static void Flag(StringBuilder text, bool? value)
    {
      text.Append(',');
      if (value is { } known)
        text.Append(known ? "true" : "false");
    }

    private static void Text(StringBuilder text, string? value)
    {
      text.Append(',');
      if (string.IsNullOrEmpty(value))
        return;
      if (value.AsSpan().IndexOfAny(",\"\r\n") < 0)
      {
        text.Append(value);
        return;
      }
      text.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
    }

    private static ulong ParseUnsigned(string cell, string where, string column) =>
      IsWholeNumber(cell, signed: false) && ulong.TryParse(cell, NumberStyles.None, CultureInfo.InvariantCulture, out ulong value)
        ? value
        : throw new InvalidDataException($"{where}: '{column}' is not a whole number that is not negative: '{cell}'");

    private static long? ParseNumber(string cell, string where, string column)
    {
      if (cell.Length == 0)
        return null;
      return IsWholeNumber(cell, signed: true) && long.TryParse(cell, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value)
        ? value
        : throw new InvalidDataException($"{where}: '{column}' is not a whole number: '{cell}'");
    }

    private static bool? ParseFlag(string cell, string where, string column) =>
      cell switch
      {
        "" => null,
        "true" => true,
        "false" => false,
        _ => throw new InvalidDataException($"{where}: '{column}' is neither true nor false: '{cell}'"),
      };

    private static string? ParseText(string cell) => cell.Length == 0 ? null : cell;

    /// <summary>Digits, with a '-' in front when <paramref name="signed"/> allows one: no '+', spaces, fraction or exponent.</summary>
    private static bool IsWholeNumber(string cell, bool signed)
    {
      int start = signed && cell.StartsWith('-') ? 1 : 0;
      if (cell.Length == start)
        return false;
      for (int i = start; i < cell.Length; ++i)
      {
        if (cell[i] is < '0' or > '9')
          return false;
      }
      return true;
    }

    /// <summary>The file's lines as cells: a cell in quotes may hold commas, line ends and doubled quotes.</summary>
    private static List<List<string>> Records(string text, string name)
    {
      var records = new List<List<string>>();
      var cells = new List<string>();
      var cell = new StringBuilder();
      bool quoted = false;
      bool any = false;
      int line = 1;
      for (int i = 0; i < text.Length; ++i)
      {
        char c = text[i];
        if (quoted)
        {
          if (c == '"')
          {
            if (i + 1 < text.Length && text[i + 1] == '"')
            {
              cell.Append('"');
              ++i;
            }
            else
            {
              quoted = false;
            }
          }
          else
          {
            if (c == '\n')
              ++line;
            cell.Append(c);
          }
          continue;
        }
        switch (c)
        {
          case '"' when cell.Length == 0:
            quoted = true;
            any = true;
            break;
          case ',':
            cells.Add(cell.ToString());
            cell.Clear();
            any = true;
            break;
          case '\r' when i + 1 < text.Length && text[i + 1] == '\n':
            break;
          case '\n':
            cells.Add(cell.ToString());
            cell.Clear();
            records.Add(cells);
            cells = new List<string>();
            any = false;
            ++line;
            break;
          default:
            cell.Append(c);
            any = true;
            break;
        }
      }
      if (quoted)
        throw new InvalidDataException($"{name}:{line}: a cell in quotes does not end");
      if (any)
      {
        cells.Add(cell.ToString());
        records.Add(cells);
      }
      return records;
    }
  }
}
