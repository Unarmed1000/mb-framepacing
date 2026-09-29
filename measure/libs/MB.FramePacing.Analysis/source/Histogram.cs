//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Histograms of per-frame values, in bins of a fixed width centred on its multiples, whatever captured the frames: the report shows the
//* measured values, and a coarse measurement shows as their spread. The bins cover min..max without gaps (empty bins included), so they
//* can be drawn directly.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;

namespace MB.FramePacing.Analysis
{
  public sealed record Histogram(double BinWidthMs, long Total, IReadOnlyList<HistogramBin> Bins)
  {
    /// <summary>Upper bound for the bin count; wider bins (a multiple of the requested width) are used when the range needs more.</summary>
    public const int DefaultMaxBins = 400;

    /// <summary>The width of the reported bins: 0.1 ms, fine enough to show a spread well below a refresh.</summary>
    public const long DefaultBinWidthTicks = TimeSpan.TicksPerMillisecond / 10;

    public static readonly Histogram Empty = new Histogram(0, 0, Array.Empty<HistogramBin>());

    /// <summary>Bin <paramref name="ticks"/>; bin k covers [(k - 0.5) * width, (k + 0.5) * width).</summary>
    public static Histogram FromTicks(IEnumerable<long> ticks, long binWidthTicks, int maxBins = DefaultMaxBins)
    {
      ArgumentOutOfRangeException.ThrowIfLessThan(maxBins, 1);
      var values = ticks.ToArray();
      if (values.Length == 0)
        return Empty;
      long width = binWidthTicks > 0 ? binWidthTicks : TimeSpan.TicksPerMillisecond;

      long first = BinOf(values.Min(), width);
      long last = BinOf(values.Max(), width);
      long needed = last - first + 1;
      if (needed > maxBins)
      {
        width *= (needed + maxBins - 1) / maxBins;
        first = BinOf(values.Min(), width);
        last = BinOf(values.Max(), width);
      }

      var counts = new long[last - first + 1];
      foreach (long value in values)
        ++counts[BinOf(value, width) - first];
      double widthMs = width / (double)TimeSpan.TicksPerMillisecond;
      var bins = counts.Select((count, i) => new HistogramBin((first + i) * widthMs, count)).ToArray();
      return new Histogram(widthMs, values.Length, bins);
    }

    private static long BinOf(long value, long width) => (long)Math.Floor((value / (double)width) + 0.5);
  }
}
