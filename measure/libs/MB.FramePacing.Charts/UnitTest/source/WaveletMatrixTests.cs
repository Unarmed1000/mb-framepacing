//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The wavelet matrix answers exactly what sorting the range would: its k-th smallest value, how many values are below a bound, and its
//* percentiles as Statistics.Percentile interpolates them, for random ranges of random values with many duplicates.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Linq;
using MB.FramePacing.Analysis;
using NUnit.Framework;

namespace MB.FramePacing.Charts.UnitTest
{
  [TestFixture]
  public class WaveletMatrixTests
  {
    [TestCase(1, 1)]
    [TestCase(7, 1)]
    [TestCase(1000, 3)]
    [TestCase(5000, 100)]
    [TestCase(20000, 1_000_000)]
    public void Queries_AreWhatSortingTheRangeGives(int count, int spread)
    {
      var random = new Random(count + spread);
      var values = Enumerable.Range(0, count).Select(_ => (long)random.Next(-spread, spread + 1) * 1000 + random.Next(0, 2)).ToArray();
      var matrix = new WaveletMatrix(values);
      Assert.That(matrix.Count, Is.EqualTo(count));
      for (int query = 0; query < 300; ++query)
      {
        int start = random.Next(0, count);
        int end = random.Next(start + 1, count + 1);
        var sorted = values[start..end];
        Array.Sort(sorted);
        int k = random.Next(0, end - start);
        Assert.That(matrix.KthSmallest(start, end, k), Is.EqualTo(sorted[k]), $"k-th {k} of {start}..{end}");
        Assert.That(matrix.KthSmallest(start, end, 0), Is.EqualTo(sorted[0]), "min");
        Assert.That(matrix.KthSmallest(start, end, end - start - 1), Is.EqualTo(sorted[^1]), "max");
        long bound = values[random.Next(0, count)] + random.Next(-1, 2);
        Assert.That(matrix.CountBelow(start, end, bound), Is.EqualTo(sorted.Count(v => v < bound)), $"below {bound} in {start}..{end}");
        var ms = sorted.Select(v => v / (double)TimeSpan.TicksPerMillisecond).ToArray();
        foreach (double fraction in new[] { 0, 0.05, 0.5, 0.95, 0.99, 1 })
          Assert.That(matrix.PercentileMs(start, end, fraction), Is.EqualTo(Statistics.Percentile(ms, fraction)), $"p{fraction}");
      }
      Assert.That(matrix.CountBelow(0, count, long.MinValue), Is.Zero);
      Assert.That(matrix.CountBelow(0, count, long.MaxValue), Is.EqualTo(count));
      Assert.That(matrix.CountBelow(3, 3, 0), Is.Zero, "an empty range");
    }

    [Test]
    public void KthSmallest_RefusesARankOutsideTheRange()
    {
      var matrix = new WaveletMatrix(new long[] { 5, 3, 9 });
      Assert.That(() => matrix.KthSmallest(0, 3, 3), Throws.InstanceOf<ArgumentOutOfRangeException>());
      Assert.That(() => matrix.KthSmallest(1, 1, 0), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }
  }
}
