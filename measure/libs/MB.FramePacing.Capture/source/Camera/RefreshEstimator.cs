//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL camera captures: calculate the display's refresh period from when a camera first saw each frame. A capture card needs no
//* estimate: it captures at the display's refresh rate.
//*
//* Two steps. EstimatePeriodTicks finds which period it is, from the intervals between frames (the refresh, not a multiple of it).
//* RefinePeriodTicks then measures it: a line through every first-seen time against the refresh it fell on. With the refreshes known,
//* that least squares slope is the maximum likelihood period (I. V. L. Clarkson, "On the Estimation of Period from Sparse, Noisy Timing
//* Data", 2006, equation 5: times = refresh number x period + offset + noise). An average of intervals only uses the first and the last
//* time of every unbroken stretch; the line uses them all, and frames that were skipped or held longer stay in. That paper's
//* estimators search for the refresh numbers when most events are missing; here nearly every refresh is seen, so the step from one
//* time to the next gives them, and the line is checked against them instead.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;

namespace MB.FramePacing.Capture.Camera
{
  public static class RefreshEstimator
  {
    /// <summary>Fewer intervals than this give no estimate.</summary>
    public const int MinIntervals = 5;

    /// <summary>
    /// The line refines the estimate it starts from and never replaces it: one that leaves it by more than this share is not taken
    /// (the times are then on another grid than the estimate's, or on none: vsync off, a display that changed its rate).
    /// </summary>
    public const double MaxRefinement = 0.01;

    /// <summary>
    /// The display's refresh period from the intervals between consecutive presented frames, as a camera first saw them. With vsync every
    /// interval is a whole number of refreshes, quantised to camera periods (16 or 17 ms for 60 Hz at 1000 fps), so the intervals form
    /// clusters at multiples of the refresh. The refresh is the largest period that makes every well-populated cluster a whole multiple: a
    /// game that alternates 2 and 3 refreshes still gives the refresh, not its frame time. A steady game below the refresh rate (only 2
    /// refreshes) can not be told from a slower display; <paramref name="calibratedPeriodTicks"/> (the rig's measurement) settles that when
    /// the clusters are whole multiples of it.
    /// </summary>
    /// <returns>The refresh period in ticks, or null when there are too few intervals or the camera is too slow to tell.</returns>
    public static double? EstimatePeriodTicks(IEnumerable<double> intervalTicks, double cameraPeriodTicks, double? calibratedPeriodTicks = null)
    {
      var sorted = intervalTicks.Where(v => v > 0).Order().ToArray();
      if (sorted.Length < MinIntervals || cameraPeriodTicks <= 0)
        return null;

      // One refresh count quantises to two neighbouring camera periods (plus a little jitter)
      double width = 1.5 * cameraPeriodTicks;
      var clusters = new List<(double Mean, int Count)>();
      int start = 0;
      for (int i = 1; i <= sorted.Length; ++i)
      {
        if (i == sorted.Length || sorted[i] - sorted[start] > width)
        {
          clusters.Add((sorted.Skip(start).Take(i - start).Average(), i - start));
          start = i;
        }
      }
      int minCount = Math.Max(3, sorted.Length / 20);
      var populated = clusters.Where(c => c.Count >= minCount).ToList();
      if (populated.Count == 0)
        return null;

      double shortest = populated[0].Mean;
      int divisor = 1;
      for (int d = 2; d <= 4 && shortest / d >= 2 * cameraPeriodTicks; ++d)
      {
        if (!Fits(populated, shortest / divisor, cameraPeriodTicks) && Fits(populated, shortest / d, cameraPeriodTicks))
          divisor = d;
      }
      if (calibratedPeriodTicks is { } calibrated && calibrated > 0)
      {
        int multiple = (int)Math.Round(shortest / calibrated);
        if (
          multiple > divisor
          && Math.Abs(shortest - (multiple * calibrated)) <= cameraPeriodTicks
          && Fits(populated, shortest / multiple, cameraPeriodTicks)
        )
          divisor = multiple;
      }

      // Refine: every cluster contributes its mean divided by its refresh count
      double guess = shortest / divisor;
      double sum = 0;
      int count = 0;
      foreach (var (mean, n) in populated)
      {
        sum += n * mean / Math.Max(1, Math.Round(mean / guess));
        count += n;
      }
      return sum / count;
    }

    /// <summary>
    /// The refresh period measured with every first-seen time: the slope of the least squares line through the times against the
    /// refresh each one fell on. <paramref name="periodTicks"/> (<see cref="EstimatePeriodTicks"/>'s) says which refresh that is, from
    /// the step since the time before.
    /// <para>
    /// The line is only taken when that numbering holds up against it: every time is nearer to its own refresh on the line than to the
    /// one before or after, and the slope stays within <see cref="MaxRefinement"/> of the period. A camera slower than about twice the
    /// refresh rate sees a refresh too coarsely for that (one step numbered wrong moves every time after it), and so do times that
    /// are not on one grid of refreshes; the period then stands as it came.
    /// </para>
    /// </summary>
    /// <param name="firstSeenTimes">When frames were first seen, in any order; only times that are a frame's true first sighting.</param>
    /// <param name="periodTicks">The period to refine.</param>
    /// <returns>The refined period in ticks; <paramref name="periodTicks"/> when there are too few times or the line is not taken.</returns>
    public static double RefinePeriodTicks(IEnumerable<TickCount64> firstSeenTimes, double periodTicks)
    {
      var times = firstSeenTimes.Select(time => time.Ticks).Distinct().Order().ToArray();
      if (times.Length <= MinIntervals || !(periodTicks > 0))
        return periodTicks;

      // Relative to the first time, so the sums stay small
      var seen = new double[times.Length];
      var refreshes = new double[times.Length];
      for (int i = 1; i < times.Length; ++i)
      {
        seen[i] = times[i] - times[0];
        refreshes[i] = refreshes[i - 1] + Math.Round((times[i] - times[i - 1]) / periodTicks);
      }
      if (!TryFitLine(refreshes, seen, out double slope, out double offset) || Math.Abs(slope - periodTicks) > MaxRefinement * periodTicks)
        return periodTicks;
      for (int i = 0; i < seen.Length; ++i)
      {
        if (Math.Round((seen[i] - offset) / slope) != refreshes[i])
          return periodTicks;
      }
      return slope;
    }

    /// <summary>The least squares line y = slope x + offset; false when every x is the same.</summary>
    private static bool TryFitLine(double[] x, double[] y, out double slope, out double offset)
    {
      double meanX = x.Average();
      double meanY = y.Average();
      double xy = 0;
      double xx = 0;
      for (int i = 0; i < x.Length; ++i)
      {
        xy += (x[i] - meanX) * (y[i] - meanY);
        xx += (x[i] - meanX) * (x[i] - meanX);
      }
      slope = xx > 0 ? xy / xx : 0;
      offset = meanY - (slope * meanX);
      return slope > 0;
    }

    private static bool Fits(List<(double Mean, int Count)> clusters, double period, double cameraPeriodTicks) =>
      clusters.All(c => Math.Round(c.Mean / period) >= 1 && Math.Abs(c.Mean - (Math.Round(c.Mean / period) * period)) <= cameraPeriodTicks);
  }
}
