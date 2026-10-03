//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Some of a capture's rows, in their order, as the stretches of the capture's row list they are: a run's rows without a copy of them (a
//* row is over 200 bytes, and a run of an hour has a million). Rows are added by their position in the capture, ascending; a run is
//* usually one stretch, and a few when rows between its own belong to nothing.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;

namespace MB.FramePacing.Analysis
{
  internal sealed class RowRanges
  {
    private readonly IReadOnlyList<CaptureRow> m_rows;

    // Each stretch: where it starts in the capture's rows, how many rows it has, and the position of its first row among these rows
    private readonly List<(int Start, int Count, int First)> m_ranges = new List<(int Start, int Count, int First)>();
    private int m_count;

    // The stretch the last row read was in: rows are read in order, so the next one is in it or in the one after
    private int m_current;

    public RowRanges(IReadOnlyList<CaptureRow> rows)
    {
      m_rows = rows ?? throw new ArgumentNullException(nameof(rows));
    }

    public int Count => m_count;

    /// <summary>Add the capture's row at <paramref name="index"/>, which is after every row added before.</summary>
    public void Add(int index)
    {
      if (index < 0 || index >= m_rows.Count)
        throw new ArgumentOutOfRangeException(nameof(index));
      if (m_ranges.Count > 0)
      {
        var last = m_ranges[^1];
        if (index < last.Start + last.Count)
          throw new ArgumentException("Rows are added in the capture's order", nameof(index));
        if (index == last.Start + last.Count)
        {
          m_ranges[^1] = (last.Start, last.Count + 1, last.First);
          ++m_count;
          return;
        }
      }
      m_ranges.Add((index, 1, m_count));
      ++m_count;
    }

    public CaptureRow this[int index]
    {
      get
      {
        if (index < 0 || index >= m_count)
          throw new ArgumentOutOfRangeException(nameof(index));
        var range = m_ranges[m_current];
        if (index < range.First || index >= range.First + range.Count)
        {
          m_current = RangeOf(index);
          range = m_ranges[m_current];
        }
        return m_rows[range.Start + (index - range.First)];
      }
    }

    /// <summary>The position of the last row that <paramref name="match"/> holds for, or -1.</summary>
    public int FindLastIndex(Predicate<CaptureRow> match)
    {
      ArgumentNullException.ThrowIfNull(match);
      for (int i = m_count - 1; i >= 0; --i)
      {
        if (match(this[i]))
          return i;
      }
      return -1;
    }

    private int RangeOf(int index)
    {
      // The stretch after the current one first: reading in order crosses into it
      if (m_current + 1 < m_ranges.Count)
      {
        var next = m_ranges[m_current + 1];
        if (index >= next.First && index < next.First + next.Count)
          return m_current + 1;
      }
      int low = 0;
      int high = m_ranges.Count - 1;
      while (low < high)
      {
        int middle = low + ((high - low + 1) / 2);
        if (m_ranges[middle].First <= index)
          low = middle;
        else
          high = middle - 1;
      }
      return low;
    }
  }
}
