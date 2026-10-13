//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What is fixed for a QR version, made once for the two versions the marker uses (2 and 6, error correction level M): which modules
//* carry data, the function patterns, where the format bits go and the order the codeword bits are placed in (the C++ library's
//* QrVersionTables). The layout is the QR standard's, as the QR Code generator library draws it (see QrEncoder.cs for its notice).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Marker
{
  internal sealed class QrVersionTables
  {
    /// <summary>The format information is 15 bits, drawn twice.</summary>
    public const int FormatBitCount = 15;

    /// <summary>Version 2-M: 44 codewords, 28 of them data, in one block.</summary>
    public static readonly QrVersionTables Version2 = new QrVersionTables(2, 44, 28, 1);

    /// <summary>Version 6-M: 172 codewords, 108 of them data, in four blocks.</summary>
    public static readonly QrVersionTables Version6 = new QrVersionTables(6, 172, 108, 4);

    public readonly int Version;

    /// <summary>Modules per side.</summary>
    public readonly int Size;

    public readonly int CodewordCount;

    /// <summary>Codewords that carry data (the rest are error correction).</summary>
    public readonly int DataCodewordCount;

    /// <summary>Error correction blocks; all have the same length for versions 2 and 6.</summary>
    public readonly int BlockCount;

    /// <summary>1 = a data module (a mask applies to it), as rows and as columns (module i of a line is bit 63 - i).</summary>
    public readonly ulong[] DataRows = new ulong[QrEncoder.MaxSize];
    public readonly ulong[] DataColumns = new ulong[QrEncoder.MaxSize];

    /// <summary>The function patterns' dark modules: finders, timing, alignment and the always dark module. The format bits are light.</summary>
    public readonly ulong[] FunctionRows = new ulong[QrEncoder.MaxSize];
    public readonly ulong[] FunctionColumns = new ulong[QrEncoder.MaxSize];

    /// <summary>The modules of the format bits, both copies: <see cref="ModuleOf"/> with the format bit's index above (<see cref="FormatBitOf"/>).</summary>
    public readonly ushort[] FormatModules = new ushort[2 * FormatBitCount];

    /// <summary>Codeword bit i (the first codeword's highest bit first) goes to module CodewordModules[i] (<see cref="ModuleOf"/>).</summary>
    public readonly ushort[] CodewordModules;

    private QrVersionTables(int version, int codewordCount, int dataCodewordCount, int blockCount)
    {
      Version = version;
      Size = (4 * version) + 17;
      CodewordCount = codewordCount;
      DataCodewordCount = dataCodewordCount;
      BlockCount = blockCount;
      for (int y = 0; y < Size; ++y)
      {
        for (int x = 0; x < Size; ++x)
        {
          if (!IsFunctionModule(Size, x, y))
          {
            DataRows[y] |= QrEncoder.Bit(x);
            DataColumns[x] |= QrEncoder.Bit(y);
          }
          else if (IsDarkFunctionModule(Size, x, y))
          {
            FunctionRows[y] |= QrEncoder.Bit(x);
            FunctionColumns[x] |= QrEncoder.Bit(y);
          }
        }
      }

      for (int i = 0; i < FormatBitCount; ++i)
      {
        // The first copy goes around the upper left finder, skipping the timing patterns
        int first = ModuleOf(14 - i, 8);
        if (i < 6)
          first = ModuleOf(8, i);
        else if (i < 8)
          first = ModuleOf(8, i + 1);
        else if (i == 8)
          first = ModuleOf(7, 8);
        // The second copy is split: below the upper right finder, and beside the lower left one
        int second = i < 8 ? ModuleOf(Size - 1 - i, 8) : ModuleOf(8, Size - 15 + i);
        FormatModules[i] = (ushort)((i << 12) | first);
        FormatModules[FormatBitCount + i] = (ushort)((i << 12) | second);
      }

      // The standard's zigzag: two columns at a time from the right, alternating upward and downward, skipping the function modules. The
      // data modules left over after the codewords stay light
      CodewordModules = new ushort[codewordCount * 8];
      int count = 0;
      for (int right = Size - 1; right >= 1; right -= 2)
      {
        // The vertical timing pattern's column is not part of a pair
        if (right == 6)
          right = 5;
        bool upward = ((right + 1) & 2) == 0;
        for (int step = 0; step < Size; ++step)
        {
          int y = upward ? Size - 1 - step : step;
          for (int x = right; x >= right - 1; --x)
          {
            if (!IsFunctionModule(Size, x, y) && count < CodewordModules.Length)
              CodewordModules[count++] = (ushort)ModuleOf(x, y);
          }
        }
      }
    }

    /// <summary>The tables of <paramref name="version"/>: null unless it is 2 or 6.</summary>
    public static QrVersionTables For(int version) =>
      version switch
      {
        6 => Version6,
        2 => Version2,
        _ => null,
      };

    /// <summary>A module's position in one value.</summary>
    public static int ModuleOf(int x, int y) => (y << 6) | x;

    public static int ModuleX(int module) => module & 63;

    public static int ModuleY(int module) => (module >> 6) & 63;

    /// <summary>The format bit a <see cref="FormatModules"/> entry carries.</summary>
    public static int FormatBitOf(int module) => module >> 12;

    /// <summary>
    /// Module (x, y) of a symbol of <paramref name="size"/> modules belongs to a function pattern: timing, the three finders with their
    /// separators and format bits, or the alignment pattern (one, at size - 7, for versions 2 to 6).
    /// </summary>
    public static bool IsFunctionModule(int size, int x, int y)
    {
      if (x == 6 || y == 6)
        return true;
      if ((x < 9 && y < 9) || (x >= size - 8 && y < 9) || (x < 9 && y >= size - 8))
        return true;
      return RingDistance(x, y, size - 7, size - 7) <= 2;
    }

    /// <summary>A function module's colour, before a mask's format bits are drawn (they are light until then).</summary>
    public static bool IsDarkFunctionModule(int size, int x, int y)
    {
      // A finder: a dark 3x3 centre, a light ring, a dark ring and the light separator
      int farCenter = size - 4;
      int finder = Math.Min(RingDistance(x, y, 3, 3), Math.Min(RingDistance(x, y, farCenter, 3), RingDistance(x, y, 3, farCenter)));
      if (finder <= 4)
        return finder != 2 && finder != 4;
      // The alignment pattern: a dark centre, a light ring and a dark ring
      int alignment = RingDistance(x, y, size - 7, size - 7);
      if (alignment <= 2)
        return alignment != 1;
      // The timing patterns alternate, dark first
      if (x == 6)
        return y % 2 == 0;
      if (y == 6)
        return x % 2 == 0;
      // What is left are the format bits, and the module above the lower left finder that is always dark
      return x == 8 && y == size - 8;
    }

    // How many rings (x, y) is away from a pattern's centre
    private static int RingDistance(int x, int y, int centerX, int centerY) => Math.Max(Math.Abs(x - centerX), Math.Abs(y - centerY));
  }
}
