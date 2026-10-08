//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One line of an analysis CSV, split on commas, read by column name so columns added later and columns an older file lacks both work.
//* Every number is a whole one, written as its digits with a '-' in front when negative; times are nanoseconds. A cell that is anything
//* else, or outside its type's range, is an InvalidDataException. The row is a view of the line's characters (no string per cell): it
//* is good as long as the line is.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace MB.FramePacing.Data
{
  internal readonly ref struct CsvRow
  {
    private readonly ReadOnlySpan<char> m_line;
    private readonly ReadOnlySpan<Range> m_cells;

    /// <summary>
    /// The cells of <paramref name="line"/>. <paramref name="cells"/> is the room for them, one more than the file's columns
    /// (<see cref="CellRoom"/>): what a longer line has beyond them ends up in that last one, which no column reads.
    /// </summary>
    public CsvRow(ReadOnlySpan<char> line, Span<Range> cells)
    {
      m_line = line;
      m_cells = cells[..line.Split(cells, ',')];
    }

    /// <summary>The room a row needs for the cells of a file with <paramref name="columnCount"/> columns.</summary>
    public static int CellRoom(int columnCount) => columnCount + 1;

    /// <summary>The cell's text; empty when the line or the file has no such column.</summary>
    public ReadOnlySpan<char> Cell(int index) => index >= 0 && index < m_cells.Length ? m_line[m_cells[index]] : default;

    public long RequiredLong(int index) => ParseLong(Cell(index));

    public long? Long(int index) => Cell(index) is { Length: > 0 } text ? ParseLong(text) : null;

    public ulong RequiredULong(int index) => ParseULong(Cell(index), ulong.MaxValue);

    public ulong? ULong(int index) => Cell(index) is { Length: > 0 } text ? ParseULong(text, ulong.MaxValue) : null;

    public int RequiredInt(int index)
    {
      long value = RequiredLong(index);
      if (value < int.MinValue || value > int.MaxValue)
        throw new InvalidDataException($"'{Cell(index)}' is outside the range of a 32-bit number");
      return (int)value;
    }

    public uint? UInt(int index) => Cell(index) is { Length: > 0 } text ? (uint)ParseULong(text, uint.MaxValue) : null;

    /// <summary>A span's cell: its nanoseconds.</summary>
    public NanosecondTimeSpan RequiredSpan(int index) => new NanosecondTimeSpan(RequiredLong(index));

    /// <summary>A span's cell; null when it is empty.</summary>
    public NanosecondTimeSpan? Span(int index) => Long(index) is { } nanoseconds ? new NanosecondTimeSpan(nanoseconds) : null;

    /// <summary>A point in time's cell: the nanoseconds since its clock's zero.</summary>
    public NanosecondTickCount RequiredTime(int index) => new NanosecondTickCount(RequiredLong(index));

    /// <summary>A point in time's cell; null when it is empty.</summary>
    public NanosecondTickCount? Time(int index) => Long(index) is { } nanoseconds ? new NanosecondTickCount(nanoseconds) : null;

    /// <summary>
    /// A marker's duration, a u32 in the file (0 to Payload.OnDemandFrameTime's 4294967295) as the marker carried it; null when the cell is
    /// empty. A number beyond 32 bits is refused.
    /// </summary>
    public NanosecondTimeDuration? MarkerDuration(int index) =>
      UInt(index) is { } nanoseconds ? NanosecondTimeDuration.FromNanoseconds(nanoseconds) : null;

    /// <summary>A whole number: digits, with a '-' in front when negative.</summary>
    public static long ParseLong(ReadOnlySpan<char> text)
    {
      // AllowLeadingSign takes a '+' too, which the other languages' readers do not
      if (text.Length > 0 && text[0] != '+' && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value))
        return value;
      throw new InvalidDataException($"'{text}' is not a whole number in the range of 64 bits");
    }

    /// <summary>A whole number that is never negative: digits only, at most <paramref name="max"/>.</summary>
    public static ulong ParseULong(ReadOnlySpan<char> text, ulong max)
    {
      if (ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong value) && value <= max)
        return value;
      throw new InvalidDataException($"'{text}' is not a whole number from 0 to {max}");
    }

    /// <summary>The header line's column names with their positions, and how many columns it has.</summary>
    public static Dictionary<string, int> Columns(string header, out int count)
    {
      var columns = new Dictionary<string, int>();
      var names = header.Split(',');
      for (int i = 0; i < names.Length; ++i)
        columns[names[i]] = i;
      count = names.Length;
      return columns;
    }
  }
}
