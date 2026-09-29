//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The scales of the report's time panels: the animation error's symmetric limit and grid, and the display time step's (and frametime's)
//* top and whole-refresh grid. Each covers every value unless a few are far beyond the rest (a hitch): those are left beyond the scale
//* and marked at its edge (ReportCard).
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using MB.FramePacing.Analysis;

namespace MB.FramePacing.Charts
{
  public static class ChartScale
  {
    /// <summary>The smallest error scale, ms either way.</summary>
    public const double MinErrorLimitMs = 2;

    /// <summary>The room above the largest error.</summary>
    public const double ErrorHeadroom = 1.15;

    /// <summary>A scale only leaves out the largest values when they need more than this many times the room the bulk needs.</summary>
    public const double ClipFactor = 8;

    /// <summary>The bulk: the values up to this percentile.</summary>
    public const double BulkPercentile = 0.99;

    // At most this many error grid lines on each side of zero, and display time step grid lines above the first refresh
    private const int MaxErrorTicksPerSide = 4;
    private const int MaxStepTicks = 8;

    /// <summary>
    /// The symmetric error scale: the largest error with some room above it, at least <see cref="MinErrorLimitMs"/>. When that needs more
    /// than <see cref="ClipFactor"/> times the room of the 99th percentile (a few hitches far above everything else), the 99th percentile's.
    /// </summary>
    public static double ErrorLimit(IReadOnlyCollection<double> errorsMs)
    {
      if (errorsMs.Count == 0)
        return MinErrorLimitMs;
      var sorted = errorsMs.Select(Math.Abs).ToArray();
      Array.Sort(sorted);
      return ErrorLimit(sorted[^1], Statistics.Percentile(sorted, BulkPercentile));
    }

    /// <summary>The error scale from the largest |error| and the <see cref="BulkPercentile"/> of the |errors|.</summary>
    public static double ErrorLimit(double largestMs, double bulkMs)
    {
      double all = Math.Max(MinErrorLimitMs, largestMs * ErrorHeadroom);
      double bulk = Math.Max(MinErrorLimitMs, bulkMs * ErrorHeadroom);
      return all <= ClipFactor * bulk ? all : bulk;
    }

    /// <summary>The error grid step: 1 ms, doubled until at most four lines fit on each side of zero.</summary>
    public static double ErrorTickStep(double limitMs)
    {
      double step = 1;
      while (limitMs / step > MaxErrorTicksPerSide)
        step *= 2;
      return step;
    }

    /// <summary>The error grid: zero, and the grid step either way inside the limit.</summary>
    public static IReadOnlyList<double> ErrorTicks(double limitMs)
    {
      double step = ErrorTickStep(limitMs);
      var ticks = new List<double> { 0 };
      for (double value = step; value < limitMs; value += step)
      {
        ticks.Add(value);
        ticks.Add(-value);
      }
      return ticks;
    }

    /// <summary>
    /// The display time step scale's top: half a refresh above the longest hold, at least two refreshes. When that needs more than
    /// <see cref="ClipFactor"/> times the room of the 99th percentile (a few hitches far longer than everything else), half a refresh above
    /// the 99th percentile.
    /// </summary>
    public static double StepTop(IReadOnlyCollection<double> levelsMs, double refreshMs)
    {
      if (levelsMs.Count == 0)
        return (2 * refreshMs) + (refreshMs / 2);
      var sorted = levelsMs.ToArray();
      Array.Sort(sorted);
      return StepTop(sorted[^1], Statistics.Percentile(sorted, BulkPercentile), refreshMs);
    }

    /// <summary>The display time step scale's top from the longest hold and the <see cref="BulkPercentile"/> of the holds.</summary>
    public static double StepTop(double longestMs, double bulkMs, double refreshMs)
    {
      double all = Math.Max(2 * refreshMs, longestMs) + (refreshMs / 2);
      double bulk = Math.Max(2 * refreshMs, bulkMs) + (refreshMs / 2);
      return all <= ClipFactor * bulk ? all : bulk;
    }

    /// <summary>
    /// The display time step grid: zero and every whole number of refreshes (16.7, 33.3, 50 ms at 60 Hz), the grid a display shows frames
    /// on. When more than eight would fit, the first refresh and every 2nd, 4th, 8th... refresh after it.
    /// </summary>
    public static IReadOnlyList<double> StepTicks(double refreshMs, double topMs)
    {
      var ticks = new List<double> { 0 };
      if (refreshMs <= 0)
        return ticks;
      int count = (int)Math.Floor(topMs / refreshMs);
      int step = 1;
      while (count / step > MaxStepTicks)
        step *= 2;
      for (int refreshes = 1; refreshes <= count; ++refreshes)
      {
        if (refreshes == 1 || refreshes % step == 0)
          ticks.Add(refreshes * refreshMs);
      }
      return ticks;
    }
  }
}
