//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL camera captures: calculate the display's refresh period from when a camera first saw each frame. A capture card needs no
//* estimate: it captures at the display's refresh rate.
//*
//* Three steps. EstimatePeriodNanoseconds finds which period it is, from the intervals between frames (the refresh, not a multiple of it).
//* GridPeriodNanoseconds asks the first-seen times themselves which period's grid they are on (the periodogram of the times at each period:
//* the usual way to find a period in timing data when the event numbers are not known), and takes that one where the intervals gave
//* another: a camera that sees a refresh in two or three frames gives intervals that mislead.
//* RefinePeriodNanoseconds then measures it: a line through every first-seen time against the refresh it fell on. With the refreshes known,
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
  // The estimator's periods and intervals are doubles of nanoseconds (the "...Nanoseconds" names): a period has a fraction. The
  // first-seen times come in as the tools hold them, whole nanoseconds. A caller rounds a period to the whole time it needs.
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
    /// Fewer first-seen times than this say nothing about which grid they are on (<see cref="GridPeriodNanoseconds"/>): about a second of
    /// frames at 60 Hz. It is also the stretch of times <see cref="GridFit"/> looks at.
    /// </summary>
    public const int MinGridTimes = 64;

    /// <summary>The <see cref="GridFit"/> of a stretch of times on no grid: sqrt(pi / 4) / sqrt(<see cref="MinGridTimes"/>).</summary>
    private const double OffGridFit = 0.1108;

    /// <summary>The standard deviation of that fit: sqrt(1 - pi / 4) / sqrt(<see cref="MinGridTimes"/>).</summary>
    private const double OffGridFitDeviation = 0.0579;

    /// <summary>
    /// The shortest period looked for, in camera periods: every time is on the camera's own grid, so a period next to it fits whatever
    /// the display does.
    /// </summary>
    private const double ShortestPeriod = 1.1;

    /// <summary>The longest period looked for is the mean interval and this share more (what a few hundred intervals leave open).</summary>
    private const double MeanIntervalSlack = 0.02;

    /// <summary>
    /// The intervals' estimate is the period the search found when the two are within this share: the search's periods are a fifth of
    /// a percent apart, and the line (<see cref="RefinePeriodNanoseconds"/>) measures either to the same period.
    /// </summary>
    private const double SamePeriod = 0.005;

    /// <summary>The search for the period the times are on looks at this many times (16 stretches): which grid it is, not how long.</summary>
    private const int SearchTimes = 16 * MinGridTimes;

    /// <summary>The search's periods are this share apart: a period a thousandth off fits a stretch of times as well as the exact one.</summary>
    private const double SearchStep = 0.002;

    /// <summary>A peak of the fit counts as a period the times are on from this share of the best peak.</summary>
    private const double SearchPeakShare = 0.8;

    /// <summary>
    /// The display's refresh period from the intervals between consecutive presented frames, as a camera first saw them. With vsync every
    /// interval is a whole number of refreshes, quantised to camera periods (16 or 17 ms for 60 Hz at 1000 fps), so the intervals form
    /// clusters at multiples of the refresh. The refresh is the largest period that makes every well-populated cluster a whole multiple: a
    /// game that alternates 2 and 3 refreshes still gives the refresh, not its frame time. A steady game below the refresh rate (only 2
    /// refreshes) can not be told from a slower display; <paramref name="calibratedPeriodNanoseconds"/> (the rig's measurement) settles that when
    /// the clusters are whole multiples of it.
    /// </summary>
    /// <returns>The refresh period in nanoseconds, or null when there are too few intervals or the camera is too slow to tell.</returns>
    public static double? EstimatePeriodNanoseconds(
      IEnumerable<double> intervalNanoseconds,
      double cameraPeriodNanoseconds,
      double? calibratedPeriodNanoseconds = null
    )
    {
      var sorted = intervalNanoseconds.Where(v => v > 0).Order().ToArray();
      if (sorted.Length < MinIntervals || cameraPeriodNanoseconds <= 0)
        return null;

      // One refresh count quantises to two neighbouring camera periods (plus a little jitter)
      double width = 1.5 * cameraPeriodNanoseconds;
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
      for (int d = 2; d <= 4 && shortest / d >= 2 * cameraPeriodNanoseconds; ++d)
      {
        if (!Fits(populated, shortest / divisor, cameraPeriodNanoseconds) && Fits(populated, shortest / d, cameraPeriodNanoseconds))
          divisor = d;
      }
      if (calibratedPeriodNanoseconds is { } calibrated && calibrated > 0)
      {
        int multiple = (int)Math.Round(shortest / calibrated);
        if (
          multiple > divisor
          && Math.Abs(shortest - (multiple * calibrated)) <= cameraPeriodNanoseconds
          && Fits(populated, shortest / multiple, cameraPeriodNanoseconds)
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
    /// The period of the grid of refreshes the first-seen times are on (<see cref="GridFit"/>), found by a search of the times; null
    /// when they are on none the camera can see, or are too few to say. It is <paramref name="expectedPeriodNanoseconds"/> (the user's or
    /// the rig's rate) when the times are on its grid, else the period found, and <paramref name="periodNanoseconds"/>
    /// (<see cref="EstimatePeriodNanoseconds"/>'s) as it came when it is that period.
    /// <para>
    /// The intervals mislead when a camera sees a refresh in two or three frames and some sightings come a camera frame late (the
    /// marker was changing in the frame before): the interval before is a camera period longer and the one after a camera period
    /// shorter, so the clusters of neighbouring refresh counts run into each other. A camera at exactly twice the refresh rate shows
    /// it: its intervals are 1, 2 and 3 camera periods, and their clusters give a period between one and two. The times themselves
    /// are unharmed: a late sighting is off the grid alone, and the others stay on it.
    /// </para>
    /// <para>
    /// The search tries every period from just above the camera's to the mean interval, two thousandths apart, and takes the longest
    /// one the times are on (a peak of the fit within a fifth of the best one). The longest, because a refresh's grid also holds the
    /// grids of its halves and thirds, and because on a camera's times a period has an alias (their rates add up to the camera's):
    /// one of the two is longer than two camera periods and one shorter. Up to the mean interval, because every frame is shown for a
    /// refresh or longer, and sightings that come late and the ones after them cancel in the mean: that leaves the alias out when it
    /// is the longer one (a camera slower than twice the refresh rate). A steady game below the refresh rate gives its frame time, as
    /// the intervals do; the expected period settles that, here as there.
    /// </para>
    /// <para>
    /// The times are on no grid when vsync is off, and when about as many sightings come a camera frame late as do not at a camera
    /// rate of twice the refresh rate: each time is then on one of two camera frames half a refresh apart, and all that is left is
    /// the camera's own grid.
    /// </para>
    /// </summary>
    /// <param name="firstSeenTimes">When frames were first seen, in any order; only times that are a frame's true first sighting.</param>
    /// <param name="periodNanoseconds">The period the intervals gave.</param>
    /// <param name="intervalNanoseconds">The intervals that period was estimated from.</param>
    /// <param name="cameraPeriodNanoseconds">The camera's frame period.</param>
    /// <param name="expectedPeriodNanoseconds">The refresh period the user or the rig expects, when there is one.</param>
    /// <returns>The period in nanoseconds, or null: no grid, or fewer than <see cref="MinGridTimes"/> times.</returns>
    public static double? GridPeriodNanoseconds(
      IEnumerable<NanosecondTickCount> firstSeenTimes,
      double periodNanoseconds,
      IEnumerable<double> intervalNanoseconds,
      double cameraPeriodNanoseconds,
      double? expectedPeriodNanoseconds = null
    )
    {
      var times = SortedNanoseconds(firstSeenTimes);
      var intervals = intervalNanoseconds.Where(v => v > 0).ToArray();
      if (times.Length < MinGridTimes || intervals.Length < MinIntervals || !(periodNanoseconds > 0) || !(cameraPeriodNanoseconds > 0))
        return null;
      // A period must be longer than the camera's: every time is on the camera's own grid, and on the grid of any period that divides it
      double shortest = ShortestPeriod * cameraPeriodNanoseconds;
      double longest = intervals.Average() * (1 + MeanIntervalSlack);
      if (longest <= shortest)
        return null;

      // The first stretches say which grid it is; the line through every time measures it afterwards
      var first = new ArraySegment<long>(times, 0, Math.Min(times.Length, SearchTimes));
      double onGrid = MinGridFit(first.Count);
      double found = SearchPeriodNanoseconds(first, shortest, longest, out double fit);
      if (fit < onGrid)
        return null;
      // The expected period may be a whole share of the one found (a game at half rate): its grid holds the times too, less sharply
      if (expectedPeriodNanoseconds is { } expected && expected >= shortest && GridFit(first, expected) >= onGrid)
        return expected;
      return Math.Abs(periodNanoseconds - found) <= SamePeriod * found ? periodNanoseconds : found;
    }

    /// <summary>
    /// Times are on a period's grid from this <see cref="GridFit"/> on: five standard deviations above what times on no grid give.
    /// That is 0.11 with a deviation of 0.058 for one stretch of <see cref="MinGridTimes"/> times, and the deviation falls with the
    /// square root of the number of stretches: 0.40 for one stretch, 0.26 for four, 0.18 for sixteen. The search tries hundreds of
    /// periods, so a lower limit would find a grid in times that are on none. The true period fits with 0.44 or more from a camera
    /// at 1.7 times the refresh rate on.
    /// </summary>
    /// <param name="timeCount">How many times the fit is of.</param>
    public static double MinGridFit(int timeCount) => OffGridFit + (5 * OffGridFitDeviation / Math.Sqrt(Math.Max(1, timeCount / MinGridTimes)));

    /// <summary>
    /// How well times sit on a grid of <paramref name="periodNanoseconds"/>: 1 when every time is a whole number of periods from the others,
    /// about 1 / sqrt(<see cref="MinGridTimes"/>) (0.1) when they have nothing to do with it. It is the periodogram of the times at
    /// that period (the length of the sum of every time's phase on the grid, per time), taken over stretches of
    /// <see cref="MinGridTimes"/> times and averaged: within a stretch a period that is a little off (a few tenths of a percent) still
    /// fits, so the period need not be exact to be recognised, however long the run is.
    /// <para>
    /// What a camera's quantisation costs: its times are up to a camera period late, so the true period fits with about 0.64 at twice
    /// the refresh rate, 0.9 at four times and more above; sightings a camera frame late cost more.
    /// </para>
    /// </summary>
    /// <param name="sortedTimes">The times in nanoseconds, in ascending order.</param>
    /// <param name="periodNanoseconds">The grid's period.</param>
    public static double GridFit(IReadOnlyList<long> sortedTimes, double periodNanoseconds)
    {
      if (!(periodNanoseconds > 0))
        return 0;
      double total = 0;
      int counted = 0;
      for (int start = 0; start + MinGridTimes <= sortedTimes.Count; start += MinGridTimes)
      {
        // The last stretch takes the times that are left, so every time counts once
        int end = start + (2 * MinGridTimes) <= sortedTimes.Count ? start + MinGridTimes : sortedTimes.Count;
        double real = 0;
        double imaginary = 0;
        for (int i = start; i < end; ++i)
        {
          double phase = 2 * Math.PI * ((sortedTimes[i] - sortedTimes[start]) / periodNanoseconds);
          real += Math.Cos(phase);
          imaginary += Math.Sin(phase);
        }
        total += Math.Sqrt((real * real) + (imaginary * imaginary));
        counted += end - start;
        if (end == sortedTimes.Count)
          break;
      }
      return counted > 0 ? total / counted : 0;
    }

    /// <summary>
    /// The longest period between <paramref name="shortest"/> and <paramref name="longest"/> that the times are on, with its fit: the
    /// longest one whose fit is a peak within <see cref="SearchPeakShare"/> of the best.
    /// </summary>
    private static double SearchPeriodNanoseconds(IReadOnlyList<long> sortedTimes, double shortest, double longest, out double fit)
    {
      var periods = new List<double>();
      for (double period = shortest; period < longest; period *= 1 + SearchStep)
        periods.Add(period);
      periods.Add(longest);
      var fits = periods.Select(period => GridFit(sortedTimes, period)).ToArray();

      int best = Array.IndexOf(fits, fits.Max());
      for (int i = fits.Length - 1; i > best; --i)
      {
        bool peak = fits[i] >= fits[i - 1] && (i == fits.Length - 1 || fits[i] >= fits[i + 1]);
        if (peak && fits[i] >= SearchPeakShare * fits[best])
        {
          best = i;
          break;
        }
      }
      fit = fits[best];
      return periods[best];
    }

    /// <summary>
    /// The refresh period measured with every first-seen time: the slope of the least squares line through the times against the
    /// refresh each one fell on. <paramref name="periodNanoseconds"/> (<see cref="EstimatePeriodNanoseconds"/>'s) says which refresh that is, from
    /// the step since the time before.
    /// <para>
    /// The line is only taken when that numbering holds up against it: every time is nearer to its own refresh on the line than to the
    /// one before or after, and the slope stays within <see cref="MaxRefinement"/> of the period. A camera slower than about twice the
    /// refresh rate sees a refresh too coarsely for that (one step numbered wrong moves every time after it), and so do times that
    /// are not on one grid of refreshes; the period then stands as it came.
    /// </para>
    /// </summary>
    /// <param name="firstSeenTimes">When frames were first seen, in any order; only times that are a frame's true first sighting.</param>
    /// <param name="periodNanoseconds">The period to refine.</param>
    /// <returns>
    /// The refined period in nanoseconds; <paramref name="periodNanoseconds"/> when there are too few times or the line is not taken.
    /// </returns>
    public static double RefinePeriodNanoseconds(IEnumerable<NanosecondTickCount> firstSeenTimes, double periodNanoseconds)
    {
      var times = SortedNanoseconds(firstSeenTimes);
      if (times.Length <= MinIntervals || !(periodNanoseconds > 0))
        return periodNanoseconds;

      // Relative to the first time, so the sums stay small
      var seen = new double[times.Length];
      var refreshes = new double[times.Length];
      for (int i = 1; i < times.Length; ++i)
      {
        seen[i] = times[i] - times[0];
        refreshes[i] = refreshes[i - 1] + Math.Round((times[i] - times[i - 1]) / periodNanoseconds);
      }
      if (
        !TryFitLine(refreshes, seen, out double slope, out double offset)
        || Math.Abs(slope - periodNanoseconds) > MaxRefinement * periodNanoseconds
      )
        return periodNanoseconds;
      for (int i = 0; i < seen.Length; ++i)
      {
        if (Math.Round((seen[i] - offset) / slope) != refreshes[i])
          return periodNanoseconds;
      }
      return slope;
    }

    /// <summary>The different times in ascending order, in nanoseconds.</summary>
    private static long[] SortedNanoseconds(IEnumerable<NanosecondTickCount> times) =>
      times.Select(time => time.Nanoseconds).Distinct().Order().ToArray();

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

    private static bool Fits(List<(double Mean, int Count)> clusters, double period, double cameraPeriodNanoseconds) =>
      clusters.All(c => Math.Round(c.Mean / period) >= 1 && Math.Abs(c.Mean - (Math.Round(c.Mean / period) * period)) <= cameraPeriodNanoseconds);
  }
}
