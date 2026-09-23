//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Summary statistics over millisecond values.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;

namespace MB.FramePacing.Analysis
{
  public sealed record Statistics(int Count, double Min, double Mean, double StdDev, double P50, double P95, double P99, double Max)
  {
    public static readonly Statistics Empty = new Statistics(0, 0, 0, 0, 0, 0, 0, 0);

    public static Statistics FromTicks(IEnumerable<long> ticks) => From(ticks.Select(t => t / (double)TimeSpan.TicksPerMillisecond));

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
        sorted[^1]
      );
    }

    /// <summary>Linear interpolation between closest ranks.</summary>
    public static double Percentile(double[] sorted, double fraction)
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
