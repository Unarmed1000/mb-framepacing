//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Groups points by the pixel column they fall in (the floor of their x), as a report card draws a long section: one index array in column
//* order and each column's range of it, instead of a LINQ group per column. Points already in x order (a run's frames are) are not sorted.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;

namespace MB.FramePacing.Charts
{
  internal static class PixelColumns
  {
    /// <summary>
    /// The pixel columns of items <paramref name="start"/> to <paramref name="end"/> (exclusive) in x order (as frames in display order are):
    /// each column with items, its pixel and its items' range. Galloping search finds each column's end, so it costs per column, not per
    /// item; the columns are exactly those <see cref="Of"/> gives.
    /// </summary>
    public static IEnumerable<(int Column, int Start, int End)> Walk(int start, int end, Func<int, double> x)
    {
      int Key(int index) => (int)Math.Floor(x(index));
      int i = start;
      while (i < end)
      {
        int key = Key(i);
        // Gallop until past the column, then search the last step
        int low = i + 1;
        int step = 1;
        int high = low;
        while (high < end && Key(high) <= key)
        {
          low = high + 1;
          high = Math.Min(end, high + step);
          step *= 2;
        }
        int columnEnd = RunChartData.FirstWhere(low, Math.Min(high, end), k => Key(k) > key);
        yield return (key, i, columnEnd);
        i = columnEnd;
      }
    }

    /// <summary>
    /// The points' indices in column order (within a column in their own order, as GroupBy and OrderBy give them), and every column: its
    /// pixel, and where its indices start in the order and how many there are.
    /// </summary>
    public static (int[] Order, List<(int Column, int Start, int Count)> Columns) Of(IReadOnlyList<double> xs)
    {
      int count = xs.Count;
      var keys = new int[count];
      var order = new int[count];
      bool sorted = true;
      for (int i = 0; i < count; ++i)
      {
        keys[i] = (int)Math.Floor(xs[i]);
        order[i] = i;
        sorted &= i == 0 || keys[i] >= keys[i - 1];
      }
      if (!sorted)
        Array.Sort(order, (a, b) => keys[a] != keys[b] ? keys[a].CompareTo(keys[b]) : a.CompareTo(b));

      var columns = new List<(int Column, int Start, int Count)>();
      for (int start = 0; start < count; )
      {
        int key = keys[order[start]];
        int end = start + 1;
        while (end < count && keys[order[end]] == key)
          ++end;
        columns.Add((key, start, end - start));
        start = end;
      }
      return (order, columns);
    }
  }
}
