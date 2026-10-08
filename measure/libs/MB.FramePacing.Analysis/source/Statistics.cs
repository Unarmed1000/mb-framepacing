//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Summary statistics over millisecond values.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;

namespace MB.FramePacing.Analysis
{
  public sealed record Statistics(int Count, double Min, double Mean, double StdDev, double P50, double P95, double P99, double P999, double Max)
  {
    public static readonly Statistics Empty = new Statistics(0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>The statistics of <paramref name="spans"/>, in milliseconds.</summary>
    public static Statistics From(IEnumerable<NanosecondTimeSpan> spans)
    {
      var nanoseconds = new NanosecondList(spans.TryGetNonEnumeratedCount(out int count) ? count : 0);
      foreach (var span in spans)
        nanoseconds.Add(span);
      nanoseconds.Sort();
      return FromSortedNanoseconds(nanoseconds.Values);
    }

    /// <summary>
    /// The statistics, in milliseconds, of spans given as their nanoseconds in ascending order: the same numbers, to the bit, as
    /// <see cref="From(IEnumerable{double})"/> gives for their milliseconds (nanoseconds and milliseconds sort alike, and the sums run
    /// over the values in that order), without an array of doubles to sort.
    /// </summary>
    internal static Statistics FromSortedNanoseconds(ReadOnlySpan<long> sorted)
    {
      if (sorted.Length == 0)
        return Empty;
      double sum = 0;
      foreach (long nanoseconds in sorted)
        sum += Milliseconds(nanoseconds);
      double mean = sum / sorted.Length;
      double squares = 0;
      if (sorted.Length > 1)
      {
        foreach (long nanoseconds in sorted)
          squares += (Milliseconds(nanoseconds) - mean) * (Milliseconds(nanoseconds) - mean);
      }
      double variance = sorted.Length > 1 ? squares / (sorted.Length - 1) : 0;
      return new Statistics(
        sorted.Length,
        Milliseconds(sorted[0]),
        mean,
        Math.Sqrt(variance),
        Percentile(sorted, 0.50),
        Percentile(sorted, 0.95),
        Percentile(sorted, 0.99),
        Percentile(sorted, 0.999),
        Milliseconds(sorted[^1])
      );
    }

    // One division of the count of nanoseconds
    private static double Milliseconds(long nanoseconds) => new NanosecondTimeSpan(nanoseconds).TotalMilliseconds;

    private static double Percentile(ReadOnlySpan<long> sorted, double fraction)
    {
      if (sorted.Length == 1)
        return Milliseconds(sorted[0]);
      double rank = fraction * (sorted.Length - 1);
      int lower = (int)Math.Floor(rank);
      int upper = Math.Min(lower + 1, sorted.Length - 1);
      return Milliseconds(sorted[lower]) + ((Milliseconds(sorted[upper]) - Milliseconds(sorted[lower])) * (rank - lower));
    }

    public static Statistics From(IEnumerable<double> values)
    {
      var sorted = values.ToArray();
      if (sorted.Length == 0)
        return Empty;
      Array.Sort(sorted);
      double mean = sorted.Average();
      double variance = sorted.Length > 1 ? sorted.Sum(v => (v - mean) * (v - mean)) / (sorted.Length - 1) : 0;
      return new Statistics(
        sorted.Length,
        sorted[0],
        mean,
        Math.Sqrt(variance),
        Percentile(sorted, 0.50),
        Percentile(sorted, 0.95),
        Percentile(sorted, 0.99),
        Percentile(sorted, 0.999),
        sorted[^1]
      );
    }

    /// <summary>Linear interpolation between closest ranks.</summary>
    public static double Percentile(ReadOnlySpan<double> sorted, double fraction)
    {
      if (sorted.Length == 1)
        return sorted[0];
      double rank = fraction * (sorted.Length - 1);
      int lower = (int)Math.Floor(rank);
      int upper = Math.Min(lower + 1, sorted.Length - 1);
      return sorted[lower] + ((sorted[upper] - sorted[lower]) * (rank - lower));
    }
  }
}
