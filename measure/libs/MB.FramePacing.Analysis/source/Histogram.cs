//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Histograms of per-frame values. Captured times are quantised to the capture period, so the bins are one capture period wide and
//* centred on its multiples: every possible measured value falls in the middle of a bin. The bins cover min..max without gaps (empty
//* bins included), so they can be drawn directly.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;

namespace MB.FramePacing.Analysis
{
  public sealed record HistogramBin(double CenterMs, long Count);

  public sealed record Histogram(double BinWidthMs, long Total, IReadOnlyList<HistogramBin> Bins)
  {
    /// <summary>Upper bound for the bin count; wider bins (a multiple of the requested width) are used when the range needs more.</summary>
    public const int DefaultMaxBins = 400;

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

  /// <summary>The distributions reported per run.</summary>
  public sealed record RunHistograms(Histogram AnimationErrorMs, Histogram DisplayDeltaMs)
  {
    public static RunHistograms Create(RunAnalysis run, long capturePeriodTicks)
    {
      var frames = run.Frames.Where(f => f.AnimationErrorTicks.HasValue).ToArray();
      return new RunHistograms(
        Histogram.FromTicks(frames.Select(f => f.AnimationErrorTicks!.Value), capturePeriodTicks),
        Histogram.FromTicks(frames.Select(f => f.DisplayDeltaTicks!.Value), capturePeriodTicks)
      );
    }
  }
}
