//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* QR code encoder for the marker's symbols: versions 2 and 6, error correction level M, byte mode, the mask with the lowest penalty
//* (the C++ library's QrEncoder). It gives exactly the symbols of the encoder it replaces (Reference/ReferenceQrEncoder.cs, which the
//* tests compare it with) and of the QR Code generator library both are ported from, and scores the masks on whole rows and columns
//* instead of module by module (doc/encoding-performance.md). Every buffer is allocated once, Encode never allocates.
//*
//* Based on the QR Code generator library, https://www.nayuki.io/page/qr-code-generator-library
//* Copyright (c) Project Nayuki. (MIT License)
//* Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the
//* "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish,
//* distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to
//* the following conditions:
//* - The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
//* - The Software is provided "as is", without warranty of any kind, express or implied, including but not limited to the warranties of
//*   merchantability, fitness for a particular purpose and noninfringement. In no event shall the authors or copyright holders be liable
//*   for any claim, damages or other liability, whether in an action of contract, tort or otherwise, arising from, out of or in connection
//*   with the Software or the use or other dealings in the Software.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Marker
{
  internal sealed class QrEncoder
  {
    /// <summary>Modules per side of the largest symbol the marker uses (QR version 6).</summary>
    public const int MaxSize = 41;

    /// <summary><see cref="PenaltyScore"/>'s limit for the whole score.</summary>
    public const int NoPenaltyLimit = int.MaxValue;

    // The QR standard's penalty weights: runs, 2x2 blocks, finder-like patterns, balance
    private const int PenaltyN1 = 3;
    private const int PenaltyN2 = 3;
    private const int PenaltyN3 = 40;
    private const int PenaltyN4 = 10;

    // LinePenalty works on a line moved this many bits down: eight light modules above it (and at least fifteen below) stand for the
    // light border the standard gives every line
    private const int LinePad = 8;

    private const int MaxCodewordCount = 172;

    // The symbol as one word per row and one per column: module (x, y) is bit 63 - x of its row and bit 63 - y of its column
    private ulong[] m_rows = new ulong[MaxSize];
    private ulong[] m_columns = new ulong[MaxSize];
    private ulong[] m_candidateRows = new ulong[MaxSize];
    private ulong[] m_candidateColumns = new ulong[MaxSize];
    private readonly ulong[] m_unmaskedRows = new ulong[MaxSize];
    private readonly ulong[] m_unmaskedColumns = new ulong[MaxSize];
    private readonly byte[] m_codewords = new byte[MaxCodewordCount];
    private readonly byte[] m_interleaved = new byte[MaxCodewordCount];
    private readonly byte[] m_errorCorrection = new byte[QrReedSolomon.EccCodewordsPerBlock];

    /// <summary>Modules per side of the last encoded symbol.</summary>
    public int Size { get; private set; }

    /// <summary>The bit of module <paramref name="position"/> of a line.</summary>
    public static ulong Bit(int position) => 1UL << (63 - position);

    public bool IsDark(int x, int y) => (m_rows[y] & Bit(x)) != 0;

    /// <summary>Row y of the last encoded symbol: module x is bit 63 - x, the bits below the row are zero.</summary>
    public ulong Row(int y) => m_rows[y];

    /// <summary>Column x of the last encoded symbol: module y is bit 63 - y.</summary>
    public ulong Column(int x) => m_columns[x];

    /// <summary>
    /// Encode <paramref name="data"/> in byte mode at error correction level M, with the mask that scores the lowest penalty (the lowest
    /// numbered one of equals). Returns false, leaving the symbol unchanged, when <paramref name="version"/> is not 2 or 6 or the data
    /// does not fit it (26 and 106 bytes).
    /// </summary>
    public bool Encode(ReadOnlySpan<byte> data, int version)
    {
      var tables = QrVersionTables.For(version);
      if (tables == null || !BuildUnmasked(data, tables))
        return false;
      // The first mask with the lowest penalty. A mask's scoring stops when it reaches the best score so far: it can not win any more
      int bestScore = NoPenaltyLimit;
      for (int mask = 0; mask < QrMaskPatterns.MaskCount; ++mask)
      {
        ApplyMask(tables, mask);
        int score = PenaltyScore(m_candidateRows, m_candidateColumns, tables.Size, bestScore);
        if (score < bestScore)
        {
          bestScore = score;
          TakeCandidate(tables.Size);
        }
      }
      return true;
    }

    /// <summary>As <see cref="Encode"/>, with a given mask (0 to 7) instead of the best one. Also false for a mask outside 0 to 7.</summary>
    public bool EncodeWithMask(ReadOnlySpan<byte> data, int version, int mask)
    {
      var tables = QrVersionTables.For(version);
      if (tables == null || mask < 0 || mask >= QrMaskPatterns.MaskCount || !BuildUnmasked(data, tables))
        return false;
      ApplyMask(tables, mask);
      TakeCandidate(tables.Size);
      return true;
    }

    /// <summary>
    /// The QR standard's penalty score of a symbol (its rows and its columns): runs of five or more modules of one colour and finder-like
    /// patterns in every row and column, 2x2 blocks of one colour, and the balance of dark and light. The scoring stops once the score
    /// reaches <paramref name="limit"/>: the result is then at least the limit, and no longer the whole score.
    /// </summary>
    public static int PenaltyScore(ReadOnlySpan<ulong> rows, ReadOnlySpan<ulong> columns, int size, int limit = NoPenaltyLimit)
    {
      // 2x2 blocks of one colour, and the dark modules, from the rows
      ulong pairs = ulong.MaxValue << (65 - size);
      ulong previousRow = rows[0];
      ulong previousSame = ~(previousRow ^ (previousRow << 1)) & pairs;
      int dark = BitUtil.PopCount(previousRow);
      int blocks = 0;
      for (int y = 1; y < size; ++y)
      {
        ulong row = rows[y];
        ulong same = ~(row ^ (row << 1)) & pairs;
        blocks += BitUtil.PopCount(same & previousSame & ~(row ^ previousRow));
        dark += BitUtil.PopCount(row);
        previousRow = row;
        previousSame = same;
      }
      // The balance: 10 for every 5 % the dark modules are away from 45 % to 55 %
      int total = size * size;
      int imbalance = Math.Abs((dark * 20) - (total * 10));
      int score = (blocks * PenaltyN2) + ((((imbalance + total - 1) / total) - 1) * PenaltyN4);

      // The runs and the finder-like patterns of every row and column
      for (int i = 0; i < size; ++i)
      {
        if (score >= limit)
          return score;
        score += LinePenalty(rows[i], size) + LinePenalty(columns[i], size);
      }
      return score;
    }

    /// <summary>
    /// The penalty of one line (a row or a column) of <paramref name="size"/> modules: its runs of one colour and its finder-like
    /// patterns. A line with nine or more dark modules in a row is walked run by run; every other line needs a few word operations.
    /// </summary>
    public static int LinePenalty(ulong line, int size)
    {
      // Module i is bit 55 - i of padded
      ulong padded = line >> LinePad;
      ulong light = ~padded;

      // Runs of five or more of one colour: PenaltyN1 for the first five modules and 1 for each further one. A run of n modules has
      // n - 4 places where five in a row start, so it scores those places plus PenaltyN1 - 1
      ulong pairs = ((1UL << (size - 1)) - 1) << (56 - size);
      ulong same = ~(padded ^ (padded >> 1)) & pairs;
      ulong fives = same & (same >> 1) & (same >> 2) & (same >> 3);
      int runs = BitUtil.PopCount(fives) + ((PenaltyN1 - 1) * BitUtil.PopCount(fives & ~(fives << 1)));

      // Finder-like patterns of a larger unit than 2 have a dark run of nine or more: those lines are walked run by run
      ulong dark2 = padded & (padded >> 1);
      ulong dark4 = dark2 & (dark2 >> 2);
      ulong dark8 = dark4 & (dark4 >> 4);
      if ((dark8 & (padded >> 8)) != 0)
        return runs + (FinderPatternsByRuns(line, size) * PenaltyN3);

      // The light modules next to a pattern that starts at a bit: the 2, 4 and 8 below it
      ulong below2 = (light << 1) & (light << 2);
      ulong below4 = below2 & (below2 << 2);
      ulong below8 = below4 & (below4 << 4);

      // Unit 1: dark, light, three dark, light, dark, between light modules; it counts once for four light modules on either side
      ulong core1 = padded & (light >> 1) & (dark2 >> 2) & (padded >> 4) & (light >> 5) & (padded >> 6) & (light << 1) & (light >> 7);
      ulong above1x2 = (light >> 7) & (light >> 8);
      ulong above1x4 = above1x2 & (above1x2 >> 2);

      // Unit 2: every run twice as long, between two light modules on either side; eight on a side to count
      ulong light2 = light & (light >> 1);
      ulong above2x2 = light2 >> 14;
      ulong above2x4 = above2x2 & (above2x2 >> 2);
      ulong above2x8 = above2x4 & (above2x4 >> 4);
      ulong core2 = dark2 & (light2 >> 2) & (dark4 >> 4) & (dark2 >> 8) & (light2 >> 10) & (dark2 >> 12) & below2 & above2x2;

      int patterns =
        BitUtil.PopCount(core1 & below4) + BitUtil.PopCount(core1 & above1x4) + BitUtil.PopCount(core2 & below8) + BitUtil.PopCount(core2 & above2x8);
      return runs + (patterns * PenaltyN3);
    }

    /// <summary>
    /// How many finder-like patterns a line has, found by walking its runs, as the reference does. <see cref="LinePenalty"/> uses it for a
    /// line with nine or more dark modules in a row.
    /// </summary>
    public static int FinderPatternsByRuns(ulong line, int size)
    {
      // The last seven runs, the newest first; a line starts and ends in a light border of its own length
      Span<int> history = stackalloc int[7];
      history.Clear();
      int patterns = 0;
      int remaining = size;
      bool dark = false;
      while (true)
      {
        // The first run is light, and empty when the line starts dark
        int length = Math.Min(BitUtil.LeadingZeroCount(dark ? ~line : line), remaining);
        if (length == remaining)
        {
          if (dark)
          {
            AddRun(history, length, size);
            AddRun(history, size, size);
          }
          else
          {
            AddRun(history, length + size, size);
          }
          return patterns + CountPatterns(history);
        }
        AddRun(history, length, size);
        if (!dark)
          patterns += CountPatterns(history);
        line <<= length;
        remaining -= length;
        dark = !dark;
      }
    }

    private static void AddRun(Span<int> history, int length, int size)
    {
      if (history[0] == 0)
        length += size;
      history.Slice(0, history.Length - 1).CopyTo(history.Slice(1));
      history[0] = length;
    }

    // After a light run: dark, light, dark, light, dark runs of 1:1:3:1:1 before it, with four times the unit of light on one side and at
    // least the unit on the other
    private static int CountPatterns(ReadOnlySpan<int> history)
    {
      int unit = history[1];
      if (unit <= 0 || history[2] != unit || history[3] != unit * 3 || history[4] != unit || history[5] != unit)
        return 0;
      return (history[0] >= unit * 4 && history[6] >= unit ? 1 : 0) + (history[6] >= unit * 4 && history[0] >= unit ? 1 : 0);
    }

    // The symbol of data before a mask, in the unmasked rows and columns: the function patterns (format bits light) and the codewords.
    // False when data does not fit
    private bool BuildUnmasked(ReadOnlySpan<byte> data, QrVersionTables tables)
    {
      int capacity = tables.DataCodewordCount;
      // Byte mode (4 bits), the byte count (8 bits) and the terminator (4 bits) take two codewords
      if (data.Length + 2 > capacity)
        return false;

      // The data codewords: the mode and the count put every byte half a byte further, then the pad bytes alternate
      int previous = data.Length;
      m_codewords[0] = (byte)(0x40 | (previous >> 4));
      int count = 1;
      for (int i = 0; i < data.Length; ++i)
      {
        m_codewords[count++] = (byte)((previous << 4) | (data[i] >> 4));
        previous = data[i];
      }
      m_codewords[count++] = (byte)(previous << 4);
      for (int pad = 0xEC; count < capacity; pad ^= 0xEC ^ 0x11)
        m_codewords[count++] = (byte)pad;

      // Every block's error correction, and the blocks interleaved: all first codewords, all second ones, ...
      int blockCount = tables.BlockCount;
      int blockLength = capacity / blockCount;
      for (int block = 0; block < blockCount; ++block)
      {
        var blockData = new ReadOnlySpan<byte>(m_codewords, block * blockLength, blockLength);
        QrReedSolomon.ComputeErrorCorrection(blockData, m_errorCorrection);
        for (int i = 0; i < blockLength; ++i)
          m_interleaved[(i * blockCount) + block] = blockData[i];
        for (int i = 0; i < m_errorCorrection.Length; ++i)
          m_interleaved[capacity + (i * blockCount) + block] = m_errorCorrection[i];
      }

      // The function patterns, then the codeword bits onto their modules
      Array.Copy(tables.FunctionRows, m_unmaskedRows, MaxSize);
      Array.Copy(tables.FunctionColumns, m_unmaskedColumns, MaxSize);
      var modules = tables.CodewordModules;
      for (int i = 0; i < modules.Length; ++i)
      {
        ulong bit = (ulong)((m_interleaved[i >> 3] >> (7 - (i & 7))) & 1);
        int module = modules[i];
        int x = QrVersionTables.ModuleX(module);
        int y = QrVersionTables.ModuleY(module);
        m_unmaskedRows[y] |= bit << (63 - x);
        m_unmaskedColumns[x] |= bit << (63 - y);
      }
      return true;
    }

    // The unmasked symbol with a mask applied to its data modules and the mask's format bits drawn, in the candidate rows and columns
    private void ApplyMask(QrVersionTables tables, int mask)
    {
      var rowPatterns = QrMaskPatterns.RowPatterns(mask);
      var columnPatterns = QrMaskPatterns.ColumnPatterns(mask);
      for (int i = 0; i < tables.Size; ++i)
      {
        m_candidateRows[i] = m_unmaskedRows[i] ^ (rowPatterns[i % QrMaskPatterns.MaskPeriod] & tables.DataRows[i]);
        m_candidateColumns[i] = m_unmaskedColumns[i] ^ (columnPatterns[i % QrMaskPatterns.MaskPeriod] & tables.DataColumns[i]);
      }
      int format = QrMaskPatterns.FormatBits(mask);
      var formatModules = tables.FormatModules;
      for (int i = 0; i < formatModules.Length; ++i)
      {
        int module = formatModules[i];
        ulong bit = (ulong)((format >> QrVersionTables.FormatBitOf(module)) & 1);
        int x = QrVersionTables.ModuleX(module);
        int y = QrVersionTables.ModuleY(module);
        m_candidateRows[y] |= bit << (63 - x);
        m_candidateColumns[x] |= bit << (63 - y);
      }
    }

    // The candidate becomes the symbol; the symbol's buffers take the next candidate
    private void TakeCandidate(int size)
    {
      (m_rows, m_candidateRows) = (m_candidateRows, m_rows);
      (m_columns, m_candidateColumns) = (m_candidateColumns, m_columns);
      Size = size;
    }
  }
}
