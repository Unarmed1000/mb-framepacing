//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The QR code's eight masks as words (the C++ library's QrMaskPatterns). A mask inverts the data modules where its rule holds; every
//* rule repeats after 12 rows and after 12 columns, so 12 words per mask and direction say which modules of any line it inverts. Applying
//* a mask to a line is then one AND (with the line's data modules) and one XOR.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Marker
{
  internal static class QrMaskPatterns
  {
    public const int MaskCount = 8;

    /// <summary>Every mask's rule repeats after this many rows, and after this many columns.</summary>
    public const int MaskPeriod = 12;

    private static readonly ulong[] g_rowPatterns = MakePatterns(rows: true);
    private static readonly ulong[] g_columnPatterns = MakePatterns(rows: false);
    private static readonly int[] g_formatBits = MakeFormatBits();

    /// <summary>The QR standard's mask rules: <paramref name="mask"/> inverts module (x, y).</summary>
    public static bool MaskInverts(int mask, int x, int y) =>
      mask switch
      {
        0 => (x + y) % 2 == 0,
        1 => y % 2 == 0,
        2 => x % 3 == 0,
        3 => (x + y) % 3 == 0,
        4 => ((x / 3) + (y / 2)) % 2 == 0,
        5 => ((x * y) % 2) + ((x * y) % 3) == 0,
        6 => (((x * y) % 2) + ((x * y) % 3)) % 2 == 0,
        _ => (((x + y) % 2) + ((x * y) % 3)) % 2 == 0,
      };

    /// <summary>[y % MaskPeriod]: the modules of row y that <paramref name="mask"/> inverts (module x is bit 63 - x).</summary>
    public static ReadOnlySpan<ulong> RowPatterns(int mask) => new ReadOnlySpan<ulong>(g_rowPatterns, mask * MaskPeriod, MaskPeriod);

    /// <summary>[x % MaskPeriod]: the modules of column x that <paramref name="mask"/> inverts (module y is bit 63 - y).</summary>
    public static ReadOnlySpan<ulong> ColumnPatterns(int mask) => new ReadOnlySpan<ulong>(g_columnPatterns, mask * MaskPeriod, MaskPeriod);

    /// <summary>
    /// The 15 format bits of error correction level M with <paramref name="mask"/>: the level (00) and the mask, a BCH(15,5) code, and
    /// the standard's XOR pattern.
    /// </summary>
    public static int FormatBits(int mask) => g_formatBits[mask];

    private static ulong[] MakePatterns(bool rows)
    {
      var patterns = new ulong[MaskCount * MaskPeriod];
      for (int mask = 0; mask < MaskCount; ++mask)
      {
        for (int line = 0; line < MaskPeriod; ++line)
        {
          ulong pattern = 0;
          for (int i = 0; i < QrEncoder.MaxSize; ++i)
          {
            if (rows ? MaskInverts(mask, i, line) : MaskInverts(mask, line, i))
              pattern |= QrEncoder.Bit(i);
          }
          patterns[(mask * MaskPeriod) + line] = pattern;
        }
      }
      return patterns;
    }

    private static int[] MakeFormatBits()
    {
      var bits = new int[MaskCount];
      for (int mask = 0; mask < MaskCount; ++mask)
      {
        int remainder = mask;
        for (int i = 0; i < 10; ++i)
          remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
        bits[mask] = ((mask << 10) | remainder) ^ 0x5412;
      }
      return bits;
    }
  }
}
