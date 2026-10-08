//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Histogram binning: bins centred on multiples of the capture period, no gaps, widened when the range is too large.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Linq;
using NUnit.Framework;

namespace MB.FramePacing.Analysis.UnitTest
{
  [TestFixture]
  public class HistogramTests
  {
    private const long Ms = NanosecondTimeSpan.NanosecondsPerMillisecond;

    private static NanosecondTimeSpan[] Spans(params long[] nanoseconds) => nanoseconds.Select(t => new NanosecondTimeSpan(t)).ToArray();

    [Test]
    public void Empty()
    {
      Assert.That(Histogram.From(Array.Empty<NanosecondTimeSpan>(), new NanosecondTimeSpan(4 * Ms)), Is.SameAs(Histogram.Empty));
    }

    [Test]
    public void BinsAreCentredOnMultiplesOfTheWidth_AndCoverTheRangeWithoutGaps()
    {
      // Quantised errors at a 4 ms capture period: -8, 0 (x3), +4, +12 and a slightly jittered 0
      var histogram = Histogram.From(Spans(-8 * Ms, 0, 0, 0, 4 * Ms, 12 * Ms, Ms), new NanosecondTimeSpan(4 * Ms));

      Assert.That(histogram.BinWidthMs, Is.EqualTo(4));
      Assert.That(histogram.Total, Is.EqualTo(7));
      Assert.That(histogram.Bins.Select(b => b.CenterMs), Is.EqualTo(new double[] { -8, -4, 0, 4, 8, 12 }));
      Assert.That(histogram.Bins.Select(b => b.Count), Is.EqualTo(new long[] { 1, 0, 4, 1, 0, 1 }));
    }

    [Test]
    public void BinEdges_BelongToTheUpperBin()
    {
      var histogram = Histogram.From(Spans(-2 * Ms, 2 * Ms), new NanosecondTimeSpan(4 * Ms));
      Assert.That(histogram.Bins.Select(b => (b.CenterMs, b.Count)), Is.EqualTo(new[] { (0.0, 1L), (4.0, 1L) }));
    }

    [Test]
    public void WideRanges_UseAMultipleOfTheWidth()
    {
      var histogram = Histogram.From(Spans(0, 1000 * Ms), new NanosecondTimeSpan(2 * Ms), maxBins: 100);

      Assert.That(histogram.Bins, Has.Count.LessThanOrEqualTo(100));
      Assert.That(histogram.BinWidthMs % 2, Is.Zero, "still a multiple of the requested width");
      Assert.That(histogram.Bins.Sum(b => b.Count), Is.EqualTo(2));
    }

    [Test]
    public void NoWidth_FallsBackToOneMillisecond()
    {
      var histogram = Histogram.From(Spans(0, 3 * Ms), NanosecondTimeSpan.Zero);
      Assert.That(histogram.BinWidthMs, Is.EqualTo(1));
      Assert.That(histogram.Bins, Has.Count.EqualTo(4));
    }
  }
}
