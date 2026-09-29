//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL camera captures: calculate the display's refresh period from when a camera first saw each frame. A capture card needs no
//* estimate: it captures at the display's refresh rate.
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

    private static bool Fits(List<(double Mean, int Count)> clusters, double period, double cameraPeriodTicks) =>
      clusters.All(c => Math.Round(c.Mean / period) >= 1 && Math.Abs(c.Mean - (Math.Round(c.Mean / period) * period)) <= cameraPeriodTicks);
  }
}
