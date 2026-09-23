//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Histogram binning: bins centred on multiples of the capture period, no gaps, widened when the range is too large.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Linq;
using NUnit.Framework;

namespace MB.FramePacing.Analysis.UnitTest
{
  [TestFixture]
  public class HistogramTests
  {
    private const long Ms = TimeSpan.TicksPerMillisecond;

    [Test]
    public void Empty()
    {
      Assert.That(Histogram.FromTicks(Array.Empty<long>(), 4 * Ms), Is.SameAs(Histogram.Empty));
    }

    [Test]
    public void BinsAreCentredOnMultiplesOfTheWidth_AndCoverTheRangeWithoutGaps()
    {
      // Quantised errors at a 4 ms capture period: -8, 0 (x3), +4, +12 and a slightly jittered 0
      var histogram = Histogram.FromTicks(new[] { -8 * Ms, 0, 0, 0, 4 * Ms, 12 * Ms, Ms }, 4 * Ms);

      Assert.That(histogram.BinWidthMs, Is.EqualTo(4));
      Assert.That(histogram.Total, Is.EqualTo(7));
      Assert.That(histogram.Bins.Select(b => b.CenterMs), Is.EqualTo(new double[] { -8, -4, 0, 4, 8, 12 }));
      Assert.That(histogram.Bins.Select(b => b.Count), Is.EqualTo(new long[] { 1, 0, 4, 1, 0, 1 }));
    }

    [Test]
    public void BinEdges_BelongToTheUpperBin()
    {
      var histogram = Histogram.FromTicks(new[] { -2 * Ms, 2 * Ms }, 4 * Ms);
      Assert.That(histogram.Bins.Select(b => (b.CenterMs, b.Count)), Is.EqualTo(new[] { (0.0, 1L), (4.0, 1L) }));
    }

    [Test]
    public void WideRanges_UseAMultipleOfTheWidth()
    {
      var histogram = Histogram.FromTicks(new[] { 0, 1000 * Ms }, 2 * Ms, maxBins: 100);

      Assert.That(histogram.Bins, Has.Count.LessThanOrEqualTo(100));
      Assert.That(histogram.BinWidthMs % 2, Is.Zero, "still a multiple of the capture period");
      Assert.That(histogram.Bins.Sum(b => b.Count), Is.EqualTo(2));
    }

    [Test]
    public void NoWidth_FallsBackToOneMillisecond()
    {
      var histogram = Histogram.FromTicks(new[] { 0L, 3 * Ms }, 0);
      Assert.That(histogram.BinWidthMs, Is.EqualTo(1));
      Assert.That(histogram.Bins, Has.Count.EqualTo(4));
    }
  }
}
