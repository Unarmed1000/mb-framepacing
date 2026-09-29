//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* An immutable set of positions as bits, with the number of set positions before any position in constant time: which frames of a run
//* have a value of some kind (an animation error, a hold, a late hold...), and where a range of frames starts in the sequence of those
//* values. One bit per frame, plus one count per 64.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Numerics;

namespace MB.FramePacing.Charts
{
  public sealed class RankBits
  {
    private readonly ulong[] m_bits;
    private readonly int[] m_ranks;

    /// <summary>The positions 0 to <paramref name="length"/> - 1 for which <paramref name="isSet"/> holds.</summary>
    public RankBits(int length, Func<int, bool> isSet)
    {
      Length = length;
      m_bits = new ulong[(length + 63) / 64];
      for (int i = 0; i < length; ++i)
      {
        if (isSet(i))
          m_bits[i >> 6] |= 1UL << (i & 63);
      }
      m_ranks = new int[m_bits.Length + 1];
      for (int w = 0; w < m_bits.Length; ++w)
        m_ranks[w + 1] = m_ranks[w] + BitOperations.PopCount(m_bits[w]);
    }

    /// <summary>How many positions there are, set or not.</summary>
    public int Length { get; }

    /// <summary>How many positions are set.</summary>
    public int Count => m_ranks[^1];

    public bool this[int position] => (m_bits[position >> 6] & (1UL << (position & 63))) != 0;

    /// <summary>How many positions before <paramref name="position"/> are set (its index among them, when it is set itself).</summary>
    public int Rank(int position)
    {
      int word = position >> 6;
      int bit = position & 63;
      int ones = m_ranks[word];
      return bit == 0 ? ones : ones + BitOperations.PopCount(m_bits[word] & ((1UL << bit) - 1));
    }

    /// <summary>How many positions from <paramref name="start"/> to <paramref name="end"/> (exclusive) are set.</summary>
    public int CountIn(int start, int end) => end > start ? Rank(end) - Rank(start) : 0;
  }
}
