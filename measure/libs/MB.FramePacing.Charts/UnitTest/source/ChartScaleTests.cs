//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The scales of the report's time panels: the error scale is symmetric with room above the largest error, the display time step scale
//* sits half a refresh above the longest hold on a grid of whole refreshes, and both leave out a hitch far above everything else.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.Linq;
using NUnit.Framework;

namespace MB.FramePacing.Charts.UnitTest
{
  [TestFixture]
  public class ChartScaleTests
  {
    [TestCase(new double[] { 0, 0 }, 2.0)]
    [TestCase(new double[] { 1, -1.5 }, 2.0)]
    [TestCase(new double[] { 5, -3 }, 5 * 1.15)]
    [TestCase(new double[] { 2, -700 }, 700 * 1.15)]
    public void ErrorLimit_IsSymmetricWithRoomAboveTheLargestError(double[] errorsMs, double expected)
    {
      Assert.That(ChartScale.ErrorLimit(errorsMs), Is.EqualTo(expected));
    }

    /// <summary>A hitch far above every other error (more than 8 times the room of the 99th percentile) is left out of the scale.</summary>
    [Test]
    public void ErrorLimit_LeavesOutAHitchFarAboveEverythingElse()
    {
      var errors = Enumerable.Repeat(7.0, 200).Concat(Enumerable.Repeat(0.0, 800)).Append(-700).ToArray();
      Assert.That(ChartScale.ErrorLimit(errors), Is.EqualTo(7 * 1.15), "the 99th percentile's room");
      var spike = Enumerable.Repeat(0.0, 1000).Append(-10).ToArray();
      Assert.That(ChartScale.ErrorLimit(spike), Is.EqualTo(10 * 1.15), "a spike within 8 times the minimum scale stays in");
    }

    [TestCase(2.0, 1.0)]
    [TestCase(4.0, 1.0)]
    [TestCase(5.75, 2.0)]
    [TestCase(8.0, 2.0)]
    [TestCase(11.5, 4.0)]
    [TestCase(805.0, 256.0)]
    public void ErrorTickStep_KeepsAtMostFourLinesEachSide(double limitMs, double expected)
    {
      Assert.That(ChartScale.ErrorTickStep(limitMs), Is.EqualTo(expected));
    }

    [Test]
    public void ErrorRefreshTicks_AreTheWholeRefreshesAnErrorReaches()
    {
      const double Refresh60 = 1000.0 / 60;
      Assert.That(ChartScale.ErrorRefreshTicks(Refresh60, 10, -9, 9), Is.Empty, "no error reaches a refresh");
      Assert.That(ChartScale.ErrorRefreshTicks(Refresh60, 20, -3, 17), Is.EqualTo(new[] { Refresh60 }), "only the side reached");
      Assert.That(
        ChartScale.ErrorRefreshTicks(Refresh60, 40, -33.3, 16.6),
        Is.EqualTo(new[] { Refresh60, -Refresh60, -2 * Refresh60 }),
        "within a tenth of a refresh counts as reached"
      );
      Assert.That(ChartScale.ErrorRefreshTicks(Refresh60, 16, -100, 100), Is.Empty, "a line the scale does not show is not drawn");
      Assert.That(ChartScale.ErrorRefreshTicks(20, 45, -41, 0), Is.EqualTo(new[] { -20.0, -40 }), "20 ms at 50 Hz");
    }

    [Test]
    public void ErrorRefreshTicks_ThinOutForLargeErrors()
    {
      // 20 refreshes of 4 ms reached: more than four lines, so the first and every 8th refresh (1, 8 and 16)
      Assert.That(ChartScale.ErrorRefreshTicks(4, 100, 0, 80), Is.EqualTo(new[] { 4.0, 32, 64 }));
    }

    [Test]
    public void ErrorTicks_AreZeroAndTheStepEitherWay()
    {
      Assert.That(ChartScale.ErrorTicks(5.75), Is.EquivalentTo(new[] { 0.0, 2, -2, 4, -4 }));
    }

    [Test]
    public void StepTop_IsHalfARefreshAboveTheLongestHold_AtLeastTwoRefreshes()
    {
      Assert.That(ChartScale.StepTop(new[] { 10.0 }, 10), Is.EqualTo(25));
      Assert.That(ChartScale.StepTop(new[] { 10.0, 40 }, 10), Is.EqualTo(45));
    }

    [Test]
    public void StepTop_LeavesOutAHitchFarAboveEverythingElse()
    {
      var holds = Enumerable.Repeat(10.0, 1000).Append(1000).ToArray();
      Assert.That(ChartScale.StepTop(holds, 10), Is.EqualTo(25), "two refreshes and a half: the hitch is marked");
      Assert.That(ChartScale.StepTop(new[] { 10.0, 10, 100 }, 10), Is.EqualTo(105), "a hold of 10 refreshes stays in");
    }

    [Test]
    public void StepTicks_AreWholeRefreshes()
    {
      double refreshMs = 1000.0 / 60;
      var ticks = ChartScale.StepTicks(refreshMs, ChartScale.StepTop(new[] { refreshMs, 3 * refreshMs }, refreshMs));
      Assert.That(ticks, Is.EqualTo(new[] { 0, refreshMs, 2 * refreshMs, 3 * refreshMs }));
    }

    [Test]
    public void StepTicks_ThinOutForLongHolds()
    {
      // A 1 s hitch at 100 Hz: the first refresh, then every 16th
      Assert.That(
        ChartScale.StepTicks(10, ChartScale.StepTop(new[] { 10.0, 1000 }, 10)),
        Is.EqualTo(new[] { 0.0, 10, 160, 320, 480, 640, 800, 960 })
      );
    }
  }
}
