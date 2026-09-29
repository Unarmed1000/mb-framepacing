//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* An immutable wavelet matrix over a sequence of whole numbers (ticks): for any range of positions it answers the k-th smallest value and
//* how many values are below a bound, exactly, in one step per bit of the number of distinct values (about 20 for a run's values), whatever
//* the range's length. The charts ask it for each pixel column's minimum, maximum, median and percentiles, a section's scale and its
//* histograms, so a zoom costs per column, not per frame.
//*
//* Claude, Navarro and Ordóñez, "The wavelet matrix", Information Systems 47 (2015); range quantiles: Gagie, Puglisi and Turpin, "Range
//* quantile queries: another virtue of wavelet trees", SPIRE 2009. The values are replaced by their rank among the distinct values first.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Numerics;

namespace MB.FramePacing.Charts
{
  public sealed class WaveletMatrix
  {
    private readonly long[] m_distinct;
    private readonly ulong[][] m_bits;
    private readonly int[][] m_ranks;
    private readonly int[] m_zeros;
    private readonly int m_levels;

    /// <summary>The matrix of <paramref name="values"/>, in their order.</summary>
    public WaveletMatrix(ReadOnlySpan<long> values)
    {
      Count = values.Length;
      var sorted = values.ToArray();
      Array.Sort(sorted);
      int distinct = 0;
      for (int i = 0; i < sorted.Length; ++i)
      {
        if (i == 0 || sorted[i] != sorted[i - 1])
          sorted[distinct++] = sorted[i];
      }
      m_distinct = sorted[..distinct];
      m_levels = Math.Max(1, 64 - BitOperations.LeadingZeroCount((ulong)Math.Max(1, distinct - 1)));

      // Each value as its rank among the distinct values; level by level, the most significant bit first, zeros stably before ones
      var current = new int[Count];
      for (int i = 0; i < Count; ++i)
        current[i] = Array.BinarySearch(m_distinct, values[i]);
      var next = new int[Count];
      int words = (Count + 63) / 64;
      m_bits = new ulong[m_levels][];
      m_ranks = new int[m_levels][];
      m_zeros = new int[m_levels];
      for (int level = 0; level < m_levels; ++level)
      {
        int shift = m_levels - 1 - level;
        var bits = new ulong[words];
        int zeros = 0;
        for (int i = 0; i < Count; ++i)
        {
          if (((current[i] >> shift) & 1) != 0)
            bits[i >> 6] |= 1UL << (i & 63);
          else
            ++zeros;
        }
        // Ones before each word: rank1(i) = ranks[i / 64] + popcount of the word's bits below i
        var ranks = new int[words + 1];
        for (int w = 0; w < words; ++w)
          ranks[w + 1] = ranks[w] + BitOperations.PopCount(bits[w]);
        int zero = 0;
        int one = zeros;
        for (int i = 0; i < Count; ++i)
        {
          if (((current[i] >> shift) & 1) != 0)
            next[one++] = current[i];
          else
            next[zero++] = current[i];
        }
        (current, next) = (next, current);
        m_bits[level] = bits;
        m_ranks[level] = ranks;
        m_zeros[level] = zeros;
      }
    }

    /// <summary>How many values the sequence holds.</summary>
    public int Count { get; }

    /// <summary>The <paramref name="k"/>-th smallest value (0 = the smallest) among positions <paramref name="start"/> to <paramref name="end"/> (exclusive).</summary>
    public long KthSmallest(int start, int end, int k)
    {
      if (start < 0 || end > Count || start >= end || k < 0 || k >= end - start)
        throw new ArgumentOutOfRangeException(nameof(k), $"k {k} is outside the range {start}..{end} of {Count} values");
      int rank = 0;
      for (int level = 0; level < m_levels; ++level)
      {
        int zerosBefore = start - Ones(level, start);
        int zerosInRange = (end - Ones(level, end)) - zerosBefore;
        if (k < zerosInRange)
        {
          start = zerosBefore;
          end = zerosBefore + zerosInRange;
        }
        else
        {
          k -= zerosInRange;
          rank |= 1 << (m_levels - 1 - level);
          start = m_zeros[level] + Ones(level, start);
          end = m_zeros[level] + Ones(level, end);
        }
      }
      return m_distinct[rank];
    }

    /// <summary>How many values among positions <paramref name="start"/> to <paramref name="end"/> (exclusive) are below <paramref name="bound"/>.</summary>
    public int CountBelow(int start, int end, long bound)
    {
      if (start >= end)
        return 0;
      // The values below the bound are the ranks below the first distinct value at or above it
      int limit = Array.BinarySearch(m_distinct, bound);
      if (limit < 0)
        limit = ~limit;
      if (limit == 0)
        return 0;
      if (limit >= m_distinct.Length)
        return end - start;
      int count = 0;
      for (int level = 0; level < m_levels; ++level)
      {
        int shift = m_levels - 1 - level;
        int onesStart = Ones(level, start);
        int onesEnd = Ones(level, end);
        if (((limit >> shift) & 1) != 0)
        {
          // Everything going left here (a 0 at this bit, the same higher bits) is below the limit
          count += (end - onesEnd) - (start - onesStart);
          start = m_zeros[level] + onesStart;
          end = m_zeros[level] + onesEnd;
        }
        else
        {
          start -= onesStart;
          end -= onesEnd;
        }
      }
      return count;
    }

    /// <summary>
    /// The percentile <paramref name="fraction"/> of positions <paramref name="start"/> to <paramref name="end"/> in milliseconds, exactly as
    /// Statistics.Percentile gives it for the same values sorted: linear interpolation between the closest ranks.
    /// </summary>
    public double PercentileMs(int start, int end, double fraction)
    {
      int count = end - start;
      if (count == 1)
        return Ms(KthSmallest(start, end, 0));
      double rank = fraction * (count - 1);
      int lower = (int)Math.Floor(rank);
      int upper = Math.Min(lower + 1, count - 1);
      double low = Ms(KthSmallest(start, end, lower));
      double high = upper == lower ? low : Ms(KthSmallest(start, end, upper));
      return low + ((high - low) * (rank - lower));
    }

    private static double Ms(long ticks) => ticks / (double)TimeSpan.TicksPerMillisecond;

    /// <summary>The ones at <paramref name="level"/> before position <paramref name="position"/>.</summary>
    private int Ones(int level, int position)
    {
      int word = position >> 6;
      int bit = position & 63;
      int ones = m_ranks[level][word];
      return bit == 0 ? ones : ones + BitOperations.PopCount(m_bits[level][word] & ((1UL << bit) - 1));
    }
  }
}
