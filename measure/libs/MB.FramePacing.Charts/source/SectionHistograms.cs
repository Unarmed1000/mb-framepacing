//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* A section's histograms from the run's prepared data: the same bins and counts Histogram.FromTicks gives for the section's frames (0.1 ms
//* bins, wider when the range needs more than the maximum bin count), counted with the wavelet matrix, two queries per bin, so the cost does
//* not grow with the section's length.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using MB.FramePacing.Analysis;

namespace MB.FramePacing.Charts
{
  public static class SectionHistograms
  {
    /// <summary>The animation errors of the section's frames that have one (RunHistograms.AnimationErrorMs).</summary>
    public static Histogram AnimationErrorMs(RunSection section) => Of(section.Data.Errors, section);

    /// <summary>The display time steps of the same frames (RunHistograms.DisplayDeltaMs).</summary>
    public static Histogram DisplayDeltaMs(RunSection section) => Of(section.Data.ErrorDisplaySteps, section);

    private static Histogram Of(FrameSequence sequence, RunSection section)
    {
      var (start, end) = sequence.Of(section.Start, section.End);
      return end > start ? FromMatrix(sequence.Values, start, end, Histogram.DefaultBinWidthTicks) : Histogram.Empty;
    }

    /// <summary>Histogram.FromTicks of positions <paramref name="start"/> to <paramref name="end"/> of <paramref name="values"/>: bin k covers [(k - 0.5) * width, (k + 0.5) * width).</summary>
    public static Histogram FromMatrix(WaveletMatrix values, int start, int end, long binWidthTicks, int maxBins = Histogram.DefaultMaxBins)
    {
      ArgumentOutOfRangeException.ThrowIfLessThan(maxBins, 1);
      if (end <= start)
        return Histogram.Empty;
      long width = binWidthTicks > 0 ? binWidthTicks : TimeSpan.TicksPerMillisecond;
      long min = values.KthSmallest(start, end, 0);
      long max = values.KthSmallest(start, end, end - start - 1);
      long first = BinOf(min, width);
      long last = BinOf(max, width);
      long needed = last - first + 1;
      if (needed > maxBins)
      {
        width *= (needed + maxBins - 1) / maxBins;
        first = BinOf(min, width);
        last = BinOf(max, width);
      }

      var bins = new HistogramBin[last - first + 1];
      double widthMs = width / (double)TimeSpan.TicksPerMillisecond;
      int below = values.CountBelow(start, end, LowestIn(first, width));
      for (long k = first; k <= last; ++k)
      {
        int belowNext = values.CountBelow(start, end, LowestIn(k + 1, width));
        bins[k - first] = new HistogramBin(k * widthMs, belowNext - below);
        below = belowNext;
      }
      return new Histogram(widthMs, end - start, bins);
    }

    private static long BinOf(long value, long width) => (long)Math.Floor((value / (double)width) + 0.5);

    /// <summary>The smallest value whose bin is <paramref name="bin"/> or higher (bins grow with the value).</summary>
    private static long LowestIn(long bin, long width)
    {
      long candidate = (long)Math.Ceiling((bin - 0.5) * width);
      while (BinOf(candidate - 1, width) >= bin)
        --candidate;
      while (BinOf(candidate, width) < bin)
        ++candidate;
      return candidate;
    }
  }
}
