//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* One line of an analysis CSV, split on commas, read by column name so columns added later and columns an older file lacks both work.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System.Collections.Generic;
using System.Globalization;

namespace MB.FramePacing.Data
{
  internal readonly struct CsvRow
  {
    private readonly string[] m_cells;

    public CsvRow(string[] cells) => m_cells = cells;

    public string Cell(int index) => index >= 0 && index < m_cells.Length ? m_cells[index] : string.Empty;

    public long? Ticks(int index) => Cell(index) is { Length: > 0 } text ? Milliseconds.ParseTicks(text) : null;

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
