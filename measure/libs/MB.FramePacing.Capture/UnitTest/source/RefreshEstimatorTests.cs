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
    private const double Camera = TimeSpan.TicksPerMillisecond; // 1000 fps camera
    private const double Refresh = TimeSpan.TicksPerSecond / 60.0;

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
      double? period = RefreshEstimator.EstimatePeriodTicks(Intervals(Enumerable.Repeat(1, 120)), Camera);

      Assert.That(period, Is.EqualTo(Refresh).Within(0.05 * TimeSpan.TicksPerMillisecond));
    }

    [Test]
    public void MixedTwoAndThreeRefreshes_GiveTheRefresh_NotTheFrameTime()
    {
      double? period = RefreshEstimator.EstimatePeriodTicks(Intervals(Enumerable.Range(0, 120).Select(i => i % 2 == 0 ? 2 : 3)), Camera);

      Assert.That(period, Is.EqualTo(Refresh).Within(0.05 * TimeSpan.TicksPerMillisecond));
    }

    [Test]
    public void SteadyHalfRate_NeedsTheCalibratedRefresh()
    {
      var intervals = Intervals(Enumerable.Repeat(2, 120));

      Assert.That(
        RefreshEstimator.EstimatePeriodTicks(intervals, Camera),
        Is.EqualTo(2 * Refresh).Within(0.1 * TimeSpan.TicksPerMillisecond),
        "ambiguous alone"
      );
      Assert.That(RefreshEstimator.EstimatePeriodTicks(intervals, Camera, Refresh), Is.EqualTo(Refresh).Within(0.05 * TimeSpan.TicksPerMillisecond));
    }

    [Test]
    public void RareStalls_DoNotMoveTheEstimate()
    {
      double? period = RefreshEstimator.EstimatePeriodTicks(Intervals(Enumerable.Range(0, 200).Select(i => i % 37 == 0 ? 2 : 1)), Camera);

      Assert.That(period, Is.EqualTo(Refresh).Within(0.05 * TimeSpan.TicksPerMillisecond));
    }

    [Test]
    public void TooFewIntervals_GiveNoEstimate()
    {
      Assert.That(RefreshEstimator.EstimatePeriodTicks(Intervals(Enumerable.Repeat(1, 3)), Camera), Is.Null);
    }
  }
}
