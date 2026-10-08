//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* EXPERIMENTAL camera captures: the display's refresh period from first-seen intervals quantised to camera periods.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using MB.FramePacing.Capture.Camera;
using NUnit.Framework;

namespace MB.FramePacing.Capture.UnitTest
{
  [TestFixture]
  public class RefreshEstimatorTests
  {
    // The estimator's periods and intervals are doubles of nanoseconds
    private const double Camera = NanosecondTimeSpan.NanosecondsPerMillisecond; // 1000 fps camera
    private const double Refresh = NanosecondTimeSpan.NanosecondsPerSecond / 60.0;

    /// <summary>First-seen intervals of frames shown for the given refresh counts, as a 1000 fps camera quantises them.</summary>
    private static List<double> Intervals(IEnumerable<int> refreshes)
    {
      var intervals = new List<double>();
      double display = 0;
      double previousSeen = 0;
      bool first = true;
      foreach (int count in refreshes)
      {
        double seen = Math.Ceiling((display + (0.37 * Camera)) / Camera) * Camera;
        if (!first)
          intervals.Add(seen - previousSeen);
        first = false;
        previousSeen = seen;
        display += count * Refresh;
      }
      return intervals;
    }

    [Test]
    public void FullRate_GivesTheRefresh()
    {
      double? period = RefreshEstimator.EstimatePeriodNanoseconds(Intervals(Enumerable.Repeat(1, 120)), Camera);

      Assert.That(period, Is.EqualTo(Refresh).Within(0.05 * NanosecondTimeSpan.NanosecondsPerMillisecond));
    }

    [Test]
    public void MixedTwoAndThreeRefreshes_GiveTheRefresh_NotTheFrameTime()
    {
      double? period = RefreshEstimator.EstimatePeriodNanoseconds(Intervals(Enumerable.Range(0, 120).Select(i => i % 2 == 0 ? 2 : 3)), Camera);

      Assert.That(period, Is.EqualTo(Refresh).Within(0.05 * NanosecondTimeSpan.NanosecondsPerMillisecond));
    }

    [Test]
    public void SteadyHalfRate_NeedsTheCalibratedRefresh()
    {
      var intervals = Intervals(Enumerable.Repeat(2, 120));

      Assert.That(
        RefreshEstimator.EstimatePeriodNanoseconds(intervals, Camera),
        Is.EqualTo(2 * Refresh).Within(0.1 * NanosecondTimeSpan.NanosecondsPerMillisecond),
        "ambiguous alone"
      );
      Assert.That(
        RefreshEstimator.EstimatePeriodNanoseconds(intervals, Camera, Refresh),
        Is.EqualTo(Refresh).Within(0.05 * NanosecondTimeSpan.NanosecondsPerMillisecond)
      );
    }

    [Test]
    public void RareStalls_DoNotMoveTheEstimate()
    {
      double? period = RefreshEstimator.EstimatePeriodNanoseconds(Intervals(Enumerable.Range(0, 200).Select(i => i % 37 == 0 ? 2 : 1)), Camera);

      Assert.That(period, Is.EqualTo(Refresh).Within(0.05 * NanosecondTimeSpan.NanosecondsPerMillisecond));
    }

    [Test]
    public void TooFewIntervals_GiveNoEstimate()
    {
      Assert.That(RefreshEstimator.EstimatePeriodNanoseconds(Intervals(Enumerable.Repeat(1, 3)), Camera), Is.Null);
    }
  }
}
