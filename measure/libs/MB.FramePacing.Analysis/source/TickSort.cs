//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Sorts whole numbers (ticks) ascending with a radix sort: a pass per byte in which the values differ, usually two or three for a run's
//* times, each a count and a move, so a run of an hour (a million values, sorted a dozen times by the statistics) costs a fraction of a
//* comparison sort. The order is the numbers' own, negative ones first; the scratch array is rented, or the caller's. The charts sort
//* their prepared values with it too.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Buffers;

namespace MB.FramePacing.Analysis
{
  public static class TickSort
  {
    // Below this a comparison sort is as fast, and needs no scratch array
    private const int RadixFrom = 256;

    // With the sign bit flipped, the values' order is the order of their bits as unsigned numbers
    private const ulong SignBit = 1UL << 63;

    /// <summary>Sort <paramref name="values"/> ascending, in place. The scratch array it needs is rented for the call.</summary>
    public static void Sort(Span<long> values)
    {
      if (values.Length < RadixFrom)
      {
        values.Sort();
        return;
      }
      long[] rented = ArrayPool<long>.Shared.Rent(values.Length);
      try
      {
        Sort(values, rented);
      }
      finally
      {
        ArrayPool<long>.Shared.Return(rented);
      }
    }

    /// <summary>
    /// Sort <paramref name="values"/> ascending, in place, with <paramref name="scratch"/> (at least as long) to move them through: for a
    /// caller that sorts on several threads at once, where rented arrays would all stay in the pool afterwards.
    /// </summary>
    public static void Sort(Span<long> values, Span<long> scratch)
    {
      if (values.Length < RadixFrom)
      {
        values.Sort();
        return;
      }
      if (scratch.Length < values.Length)
        throw new ArgumentException("The scratch array is shorter than the values", nameof(scratch));
      ulong any = 0;
      ulong all = ulong.MaxValue;
      foreach (long value in values)
      {
        ulong key = (ulong)value ^ SignBit;
        any |= key;
        all &= key;
      }
      // The bits in which at least two values differ: a byte without one needs no pass
      ulong differing = any ^ all;
      if (differing == 0)
        return;

      var from = values;
      var to = scratch[..values.Length];
      bool inValues = true;
      Span<int> starts = stackalloc int[256];
      for (int shift = 0; shift < 64; shift += 8)
      {
        if (((differing >> shift) & 0xFF) == 0)
          continue;
        starts.Clear();
        foreach (long value in from)
          ++starts[(int)((((ulong)value ^ SignBit) >> shift) & 0xFF)];
        int total = 0;
        for (int i = 0; i < starts.Length; ++i)
        {
          int count = starts[i];
          starts[i] = total;
          total += count;
        }
        foreach (long value in from)
          to[starts[(int)((((ulong)value ^ SignBit) >> shift) & 0xFF)]++] = value;
        var swap = from;
        from = to;
        to = swap;
        inValues = !inValues;
      }
      if (!inValues)
        from.CopyTo(values);
    }
  }
}
