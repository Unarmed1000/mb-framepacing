//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One line of an analysis CSV, split on commas, read by column name so columns added later and columns an older file lacks both work.
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
  internal readonly struct CsvRow
  {
    private readonly string[] m_cells;

    public CsvRow(string[] cells) => m_cells = cells;

    public string Cell(int index) => index >= 0 && index < m_cells.Length ? m_cells[index] : string.Empty;

    /// <summary>A span's cell; null when it is empty.</summary>
    public TimeSpan? Span(int index) => Cell(index) is { Length: > 0 } text ? Milliseconds.ParseMilliseconds(text) : null;

    /// <summary>A point in time's cell (milliseconds since its clock's zero); null when it is empty.</summary>
    public TickCount64? Time(int index) => Span(index) is { } span ? new TickCount64(span) : null;

    /// <summary>A marker's 32-bit span (0 to Payload.OnDemandFrameTime); null when the cell is empty.</summary>
    public TimeSpan32? Span32(int index)
    {
      if (!(Span(index) is { } span))
        return null;
      if (span < TimeSpan.Zero || span > TimeSpan32.MaxValue.ToTimeSpan())
        throw new InvalidDataException($"'{Cell(index)}' is not a 32-bit span of ticks");
      return TimeSpan32.FromTimeSpan(span);
    }

    public long? Long(int index) => Cell(index) is { Length: > 0 } text ? long.Parse(text, CultureInfo.InvariantCulture) : null;

    public ulong? ULong(int index) => Cell(index) is { Length: > 0 } text ? ulong.Parse(text, CultureInfo.InvariantCulture) : null;

    public static Dictionary<string, int> Columns(string header)
    {
      var columns = new Dictionary<string, int>();
      var names = header.Split(',');
      for (int i = 0; i < names.Length; ++i)
        columns[names[i]] = i;
      return columns;
    }
  }
}
