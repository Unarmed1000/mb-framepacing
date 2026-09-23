//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* QR code encoder for the marker: byte mode, error correction level M, versions 1-6, automatic mask selection. A port of the QR Code
//* generator the C++ library vendors (marker/cpp/third_party/qrcodegen), limited to what the marker uses, so both produce exactly the same
//* symbols; the tests check it module by module against test-data/markers/modules.csv. Every buffer is allocated once, Encode never
//* allocates.
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

namespace MB.FrameMarker
{
  internal sealed class QrEncoder
  {
    public const int MinVersion = 1;
    public const int MaxVersion = 6;
    private const int MaxSize = (4 * MaxVersion) + 17;

    // Error correction level M, indexed by version (index 0 unused). From the QR specification, as in qrcodegen.
    private static readonly int[] g_eccCodewordsPerBlock = { -1, 10, 16, 26, 18, 24, 16 };
    private static readonly int[] g_errorCorrectionBlocks = { -1, 1, 1, 1, 2, 2, 4 };

    // Level M is 0 in the format information bits
    private const int FormatBitsLevelM = 0;
    private const int ModeIndicatorByte = 0x4;
    private const int ByteModeCountBits = 8; // versions 1-9

    private const int PenaltyN1 = 3;
    private const int PenaltyN2 = 3;
    private const int PenaltyN3 = 40;
    private const int PenaltyN4 = 10;

    private readonly bool[] m_modules = new bool[MaxSize * MaxSize];
    private readonly bool[] m_isFunction = new bool[MaxSize * MaxSize];
    private readonly byte[] m_dataCodewords = new byte[RawCodewords(MaxVersion)];
    private readonly byte[] m_allCodewords = new byte[RawCodewords(MaxVersion)];
    private readonly byte[] m_blocks;
    private readonly byte[] m_eccRemainder;
    private readonly byte[][] m_divisors = new byte[MaxVersion + 1][];
    private readonly int[] m_runHistory = new int[7];

    public QrEncoder()
    {
      int maxEcc = 0;
      int maxBlockBytes = 0;
      for (int version = MinVersion; version <= MaxVersion; ++version)
      {
        int eccLength = g_eccCodewordsPerBlock[version];
        m_divisors[version] = ReedSolomonComputeDivisor(eccLength);
        maxEcc = Math.Max(maxEcc, eccLength);
        int numBlocks = g_errorCorrectionBlocks[version];
        maxBlockBytes = Math.Max(maxBlockBytes, numBlocks * ((RawCodewords(version) / numBlocks) + 1));
      }
      m_eccRemainder = new byte[maxEcc];
      m_blocks = new byte[maxBlockBytes];
    }

    /// <summary>Modules per side of the last encoded symbol.</summary>
    public int Size { get; private set; }

    public bool IsDark(int x, int y) => m_modules[(y * MaxSize) + x];

    /// <summary>
    /// Encode <paramref name="data"/>[0, length) in byte mode with error correction level M, using the smallest version in
    /// [<paramref name="minVersion"/>, <paramref name="maxVersion"/>] that fits and the mask with the lowest penalty.
    /// Returns false when the data does not fit (or the version range is outside 1-6).
    /// </summary>
    public bool Encode(byte[] data, int length, int minVersion, int maxVersion)
    {
      if (minVersion < MinVersion || maxVersion > MaxVersion || minVersion > maxVersion || length < 0 || length > data.Length)
        return false;

      int usedBits = 4 + ByteModeCountBits + (8 * length);
      int version = minVersion;
      while (usedBits > DataCodewords(version) * 8)
      {
        if (version >= maxVersion)
          return false;
        ++version;
      }

      // Data codewords: mode, character count, data, terminator, bit padding, then alternating pad bytes
      int capacityBytes = DataCodewords(version);
      Array.Clear(m_dataCodewords, 0, m_dataCodewords.Length);
      int bitLength = 0;
      AppendBits(ModeIndicatorByte, 4, ref bitLength);
      AppendBits(length, ByteModeCountBits, ref bitLength);
      for (int i = 0; i < length; ++i)
        AppendBits(data[i], 8, ref bitLength);
      int capacityBits = capacityBytes * 8;
      AppendBits(0, Math.Min(4, capacityBits - bitLength), ref bitLength);
      AppendBits(0, (8 - (bitLength % 8)) % 8, ref bitLength);
      for (int padByte = 0xEC; bitLength < capacityBits; padByte ^= 0xEC ^ 0x11)
        AppendBits(padByte, 8, ref bitLength);

      AddEccAndInterleave(version);

      Size = (4 * version) + 17;
      Array.Clear(m_modules, 0, m_modules.Length);
      Array.Clear(m_isFunction, 0, m_isFunction.Length);
      DrawFunctionPatterns(version);
      DrawCodewords(RawCodewords(version));

      int bestMask = 0;
      long minPenalty = long.MaxValue;
      for (int mask = 0; mask < 8; ++mask)
      {
        ApplyMask(mask);
        DrawFormatBits(mask);
        long penalty = GetPenaltyScore();
        if (penalty < minPenalty)
        {
          bestMask = mask;
          minPenalty = penalty;
        }
        ApplyMask(mask); // undoes the mask (XOR)
      }
      ApplyMask(bestMask);
      DrawFormatBits(bestMask);
      return true;
    }

    private static int RawDataModules(int version)
    {
      int result = ((16 * version) + 128) * version + 64;
      if (version >= 2)
      {
        int numAlign = (version / 7) + 2;
        result -= (((25 * numAlign) - 10) * numAlign) - 55;
        if (version >= 7)
          result -= 36;
      }
      return result;
    }

    private static int RawCodewords(int version) => RawDataModules(version) / 8;

    private static int DataCodewords(int version) => RawCodewords(version) - (g_eccCodewordsPerBlock[version] * g_errorCorrectionBlocks[version]);

    private void AppendBits(int value, int count, ref int bitLength)
    {
      for (int i = count - 1; i >= 0; --i, ++bitLength)
        m_dataCodewords[bitLength >> 3] |= (byte)(((value >> i) & 1) << (7 - (bitLength & 7)));
    }

    private void AddEccAndInterleave(int version)
    {
      int numBlocks = g_errorCorrectionBlocks[version];
      int blockEccLength = g_eccCodewordsPerBlock[version];
      int rawCodewords = RawCodewords(version);
      int numShortBlocks = numBlocks - (rawCodewords % numBlocks);
      int shortBlockLength = rawCodewords / numBlocks;
      int blockStride = shortBlockLength + 1;
      byte[] divisor = m_divisors[version];

      // Split the data into blocks and append the ECC to each block (short blocks keep one unused padding byte)
      int dataOffset = 0;
      for (int i = 0; i < numBlocks; ++i)
      {
        int dataLength = shortBlockLength - blockEccLength + (i < numShortBlocks ? 0 : 1);
        int block = i * blockStride;
        Array.Clear(m_blocks, block, blockStride);
        Array.Copy(m_dataCodewords, dataOffset, m_blocks, block, dataLength);
        ReedSolomonComputeRemainder(m_dataCodewords, dataOffset, dataLength, divisor);
        Array.Copy(m_eccRemainder, 0, m_blocks, block + blockStride - blockEccLength, blockEccLength);
        dataOffset += dataLength;
      }

      // Interleave (not concatenate) the bytes of every block into one sequence
      int k = 0;
      for (int i = 0; i < blockStride; ++i)
      {
        for (int j = 0; j < numBlocks; ++j)
        {
          if (i != shortBlockLength - blockEccLength || j >= numShortBlocks)
            m_allCodewords[k++] = m_blocks[(j * blockStride) + i];
        }
      }
    }

    private static byte[] ReedSolomonComputeDivisor(int degree)
    {
      var result = new byte[degree];
      result[degree - 1] = 1; // start with the monomial x^0
      int root = 1;
      for (int i = 0; i < degree; ++i)
      {
        // Multiply the current product by (x - r^i)
        for (int j = 0; j < degree; ++j)
        {
          result[j] = (byte)ReedSolomonMultiply(result[j], root);
          if (j + 1 < degree)
            result[j] ^= result[j + 1];
        }
        root = ReedSolomonMultiply(root, 0x02);
      }
      return result;
    }

    private void ReedSolomonComputeRemainder(byte[] data, int offset, int length, byte[] divisor)
    {
      int degree = divisor.Length;
      Array.Clear(m_eccRemainder, 0, m_eccRemainder.Length);
      for (int n = 0; n < length; ++n)
      {
        int factor = data[offset + n] ^ m_eccRemainder[0];
        Array.Copy(m_eccRemainder, 1, m_eccRemainder, 0, degree - 1);
        m_eccRemainder[degree - 1] = 0;
        for (int i = 0; i < degree; ++i)
          m_eccRemainder[i] ^= (byte)ReedSolomonMultiply(divisor[i], factor);
      }
    }

    private static int ReedSolomonMultiply(int x, int y)
    {
      // Russian peasant multiplication in GF(2^8/0x11D)
      int z = 0;
      for (int i = 7; i >= 0; --i)
      {
        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> i) & 1) * x;
      }
      return z;
    }

    private void SetFunctionModule(int x, int y, bool dark)
    {
      m_modules[(y * MaxSize) + x] = dark;
      m_isFunction[(y * MaxSize) + x] = true;
    }

    private void DrawFunctionPatterns(int version)
    {
      // Timing patterns
      for (int i = 0; i < Size; ++i)
      {
        SetFunctionModule(6, i, i % 2 == 0);
        SetFunctionModule(i, 6, i % 2 == 0);
      }

      // Finder patterns in three corners (overwrite some timing modules)
      DrawFinderPattern(3, 3);
      DrawFinderPattern(Size - 4, 3);
      DrawFinderPattern(3, Size - 4);

      // Alignment patterns: versions 2-6 have one at (Size - 7, Size - 7), the others would overlap the finders
      if (version >= 2)
      {
        int numAlign = (version / 7) + 2;
        int step = ((version * 8) + (numAlign * 3) + 5) / ((numAlign * 4) - 4) * 2;
        for (int i = 0; i < numAlign; ++i)
        {
          for (int j = 0; j < numAlign; ++j)
          {
            if ((i == 0 && j == 0) || (i == 0 && j == numAlign - 1) || (i == numAlign - 1 && j == 0))
              continue;
            DrawAlignmentPattern(AlignmentPosition(i, numAlign, step), AlignmentPosition(j, numAlign, step));
          }
        }
      }

      // Format bits with a dummy mask (overwritten after the mask is chosen); versions below 7 have no version information
      DrawFormatBits(0);
    }

    private int AlignmentPosition(int index, int numAlign, int step) => index == 0 ? 6 : Size - 7 - ((numAlign - 1 - index) * step);

    private void DrawFinderPattern(int centerX, int centerY)
    {
      for (int dy = -4; dy <= 4; ++dy)
      {
        for (int dx = -4; dx <= 4; ++dx)
        {
          int distance = Math.Max(Math.Abs(dx), Math.Abs(dy)); // Chebyshev distance
          int x = centerX + dx;
          int y = centerY + dy;
          if (x >= 0 && x < Size && y >= 0 && y < Size)
            SetFunctionModule(x, y, distance != 2 && distance != 4);
        }
      }
    }

    private void DrawAlignmentPattern(int centerX, int centerY)
    {
      for (int dy = -2; dy <= 2; ++dy)
      {
        for (int dx = -2; dx <= 2; ++dx)
          SetFunctionModule(centerX + dx, centerY + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
      }
    }

    private void DrawFormatBits(int mask)
    {
      // Error correction level and mask, then a BCH(15,5) code and the fixed XOR pattern
      int data = (FormatBitsLevelM << 3) | mask;
      int remainder = data;
      for (int i = 0; i < 10; ++i)
        remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
      int bits = ((data << 10) | remainder) ^ 0x5412;

      // First copy
      for (int i = 0; i <= 5; ++i)
        SetFunctionModule(8, i, GetBit(bits, i));
      SetFunctionModule(8, 7, GetBit(bits, 6));
      SetFunctionModule(8, 8, GetBit(bits, 7));
      SetFunctionModule(7, 8, GetBit(bits, 8));
      for (int i = 9; i < 15; ++i)
        SetFunctionModule(14 - i, 8, GetBit(bits, i));

      // Second copy
      for (int i = 0; i < 8; ++i)
        SetFunctionModule(Size - 1 - i, 8, GetBit(bits, i));
      for (int i = 8; i < 15; ++i)
        SetFunctionModule(8, Size - 15 + i, GetBit(bits, i));
      SetFunctionModule(8, Size - 8, true); // always dark
    }

    private void DrawCodewords(int codewordCount)
    {
      // The zigzag scan: column pairs from the right, alternating up and down, skipping the vertical timing column
      int bitIndex = 0;
      int totalBits = codewordCount * 8;
      for (int right = Size - 1; right >= 1; right -= 2)
      {
        if (right == 6)
          right = 5;
        for (int vertical = 0; vertical < Size; ++vertical)
        {
          for (int j = 0; j < 2; ++j)
          {
            int x = right - j;
            bool upward = ((right + 1) & 2) == 0;
            int y = upward ? Size - 1 - vertical : vertical;
            if (!m_isFunction[(y * MaxSize) + x] && bitIndex < totalBits)
            {
              m_modules[(y * MaxSize) + x] = GetBit(m_allCodewords[bitIndex >> 3], 7 - (bitIndex & 7));
              ++bitIndex;
            }
            // Remainder bits (0 to 7) stay light
          }
        }
      }
    }

    private void ApplyMask(int mask)
    {
      for (int y = 0; y < Size; ++y)
      {
        for (int x = 0; x < Size; ++x)
        {
          int index = (y * MaxSize) + x;
          if (m_isFunction[index])
            continue;
          bool invert;
          switch (mask)
          {
            case 0:
              invert = (x + y) % 2 == 0;
              break;
            case 1:
              invert = y % 2 == 0;
              break;
            case 2:
              invert = x % 3 == 0;
              break;
            case 3:
              invert = (x + y) % 3 == 0;
              break;
            case 4:
              invert = ((x / 3) + (y / 2)) % 2 == 0;
              break;
            case 5:
              invert = ((x * y) % 2) + ((x * y) % 3) == 0;
              break;
            case 6:
              invert = (((x * y) % 2) + ((x * y) % 3)) % 2 == 0;
              break;
            default:
              invert = (((x + y) % 2) + ((x * y) % 3)) % 2 == 0;
              break;
          }
          if (invert)
            m_modules[index] = !m_modules[index];
        }
      }
    }

    private long GetPenaltyScore()
    {
      long result = 0;

      // Adjacent modules in a row with the same color, and finder-like patterns
      for (int y = 0; y < Size; ++y)
        result += LinePenalty(y, horizontal: true);
      // The same for columns
      for (int x = 0; x < Size; ++x)
        result += LinePenalty(x, horizontal: false);

      // 2x2 blocks of modules with the same color
      for (int y = 0; y < Size - 1; ++y)
      {
        for (int x = 0; x < Size - 1; ++x)
        {
          bool color = IsDark(x, y);
          if (color == IsDark(x + 1, y) && color == IsDark(x, y + 1) && color == IsDark(x + 1, y + 1))
            result += PenaltyN2;
        }
      }

      // Balance of dark and light modules: the smallest k >= 0 with (45-5k)% <= dark/total <= (55+5k)%
      int dark = 0;
      for (int y = 0; y < Size; ++y)
      {
        for (int x = 0; x < Size; ++x)
        {
          if (IsDark(x, y))
            ++dark;
        }
      }
      int total = Size * Size;
      int k = (int)(((Math.Abs((dark * 20L) - (total * 10L)) + total - 1) / total) - 1);
      result += k * PenaltyN4;
      return result;
    }

    private long LinePenalty(int line, bool horizontal)
    {
      long result = 0;
      bool runColor = false;
      int runLength = 0;
      Array.Clear(m_runHistory, 0, m_runHistory.Length);
      for (int i = 0; i < Size; ++i)
      {
        bool color = horizontal ? IsDark(i, line) : IsDark(line, i);
        if (color == runColor)
        {
          ++runLength;
          if (runLength == 5)
            result += PenaltyN1;
          else if (runLength > 5)
            ++result;
        }
        else
        {
          FinderPenaltyAddHistory(runLength);
          if (!runColor)
            result += FinderPenaltyCountPatterns() * PenaltyN3;
          runColor = color;
          runLength = 1;
        }
      }
      result += FinderPenaltyTerminateAndCount(runColor, runLength) * PenaltyN3;
      return result;
    }

    private int FinderPenaltyCountPatterns()
    {
      int n = m_runHistory[1];
      bool core = n > 0 && m_runHistory[2] == n && m_runHistory[3] == n * 3 && m_runHistory[4] == n && m_runHistory[5] == n;
      return (core && m_runHistory[0] >= n * 4 && m_runHistory[6] >= n ? 1 : 0) + (core && m_runHistory[6] >= n * 4 && m_runHistory[0] >= n ? 1 : 0);
    }

    private int FinderPenaltyTerminateAndCount(bool currentRunColor, int currentRunLength)
    {
      if (currentRunColor)
      {
        // Terminate the dark run
        FinderPenaltyAddHistory(currentRunLength);
        currentRunLength = 0;
      }
      currentRunLength += Size; // add the light border to the final run
      FinderPenaltyAddHistory(currentRunLength);
      return FinderPenaltyCountPatterns();
    }

    private void FinderPenaltyAddHistory(int currentRunLength)
    {
      if (m_runHistory[0] == 0)
        currentRunLength += Size; // add the light border to the initial run
      Array.Copy(m_runHistory, 0, m_runHistory, 1, m_runHistory.Length - 1);
      m_runHistory[0] = currentRunLength;
    }

    private static bool GetBit(int value, int index) => ((value >> index) & 1) != 0;
  }
}
