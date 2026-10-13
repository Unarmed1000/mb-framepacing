//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The QR code's Reed-Solomon error correction over GF(2^8) modulo 0x11D, for blocks with 16 error correction codewords (the C++
//* library's QrReedSolomon). The arithmetic is the QR Code generator library's (see QrEncoder.cs for its notice); the multiplications go
//* through logarithm tables, made once, instead of eight shift and add steps each.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

using System;

namespace MB.FramePacing.Marker
{
  internal static class QrReedSolomon
  {
    /// <summary>Error correction codewords per block: 16 for versions 2 and 6 at level M.</summary>
    public const int EccCodewordsPerBlock = 16;

    // Powers of the generator element 2, twice over: the sum of two logarithms indexes it without a modulo
    private const int ExpCount = 510;

    private static readonly byte[] g_exp = MakeExp();
    private static readonly byte[] g_log = MakeLog(g_exp);
    private static readonly byte[] g_divisorLog = MakeDivisorLog(g_log);

    /// <summary>The product of two field elements (shift and add; the tables replace it when encoding).</summary>
    public static int Multiply(int x, int y)
    {
      int z = 0;
      for (int i = 7; i >= 0; --i)
      {
        z = ((z << 1) ^ ((z >> 7) * 0x11D)) & 0xFF;
        z ^= ((y >> i) & 1) * x;
      }
      return z;
    }

    /// <summary>2 to the power <paramref name="exponent"/> (0 to 509).</summary>
    public static int Exp(int exponent) => g_exp[exponent];

    /// <summary>The logarithm of <paramref name="value"/> (1 to 255) to the base 2.</summary>
    public static int Log(int value) => g_log[value];

    /// <summary>The 16 error correction codewords of a block: the remainder of <paramref name="data"/> divided by the generator polynomial.</summary>
    public static void ComputeErrorCorrection(ReadOnlySpan<byte> data, Span<byte> remainder)
    {
      remainder = remainder.Slice(0, EccCodewordsPerBlock);
      remainder.Clear();
      for (int n = 0; n < data.Length; ++n)
      {
        int factor = data[n] ^ remainder[0];
        remainder.Slice(1).CopyTo(remainder);
        remainder[EccCodewordsPerBlock - 1] = 0;
        if (factor != 0)
        {
          int factorLog = g_log[factor];
          for (int i = 0; i < EccCodewordsPerBlock; ++i)
            remainder[i] ^= g_exp[g_divisorLog[i] + factorLog];
        }
      }
    }

    private static byte[] MakeExp()
    {
      var exp = new byte[ExpCount];
      int value = 1;
      for (int i = 0; i < ExpCount; ++i)
      {
        exp[i] = (byte)value;
        value = Multiply(value, 2);
      }
      return exp;
    }

    private static byte[] MakeLog(byte[] exp)
    {
      // 0 has no logarithm: its entry is never read
      var log = new byte[256];
      for (int i = 0; i < 255; ++i)
        log[exp[i]] = (byte)i;
      return log;
    }

    private static byte[] MakeDivisorLog(byte[] log)
    {
      // The generator polynomial of degree 16, (x - 2^0)(x - 2^1)...(x - 2^15), highest power first, without the leading 1
      var divisor = new byte[EccCodewordsPerBlock];
      divisor[EccCodewordsPerBlock - 1] = 1;
      int root = 1;
      for (int i = 0; i < EccCodewordsPerBlock; ++i)
      {
        for (int j = 0; j < EccCodewordsPerBlock; ++j)
        {
          divisor[j] = (byte)Multiply(divisor[j], root);
          if (j + 1 < EccCodewordsPerBlock)
            divisor[j] ^= divisor[j + 1];
        }
        root = Multiply(root, 2);
      }
      // None of its coefficients is 0, so each has a logarithm
      var divisorLog = new byte[EccCodewordsPerBlock];
      for (int i = 0; i < EccCodewordsPerBlock; ++i)
        divisorLog[i] = log[divisor[i]];
      return divisorLog;
    }
  }
}
