//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What the GUI shows at the pointer over a card of a section: on the time panels and the drift the frame shown then (its display time
//* step, animation error, lateness, frametime and CPU busy), on a histogram the bin under the pointer and its frames, on the percentile
//* curve the |animation error| at that percentile. Only values the analysis has; the histograms and the sorted errors are computed once.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MB.FramePacing.Analysis;

namespace MB.FramePacing.Charts
{
  public sealed class CardHover
  {
    private readonly RunSection m_section;
    private readonly Lazy<Histogram> m_errors;
    private readonly Lazy<Histogram> m_displaySteps;

    public CardHover(RunSection section)
    {
      m_section = section;
      m_errors = new Lazy<Histogram>(() => SectionHistograms.AnimationErrorMs(section));
      m_displaySteps = new Lazy<Histogram>(() => SectionHistograms.DisplayDeltaMs(section));
    }

    /// <summary>The text for the point (<paramref name="x"/>, <paramref name="y"/>) in <paramref name="plot"/>'s values, or null for none.</summary>
    public string? Describe(CardPlot plot, double x, double y)
    {
      switch (plot.Id)
      {
        case DistributionCard.ErrorHistogram:
          return Bin(m_errors.Value, x, "animation error", sign: true);
        case DistributionCard.DisplayTimeStepHistogram:
          return Bin(m_displaySteps.Value, x, "display time step", sign: false);
        case DistributionCard.ErrorPercentiles:
        {
          if (DistributionCard.ErrorCount(m_section) == 0)
            return null;
          double p = Math.Clamp(x, 0, 100);
          return $"p{Invariant(p, "0.0")}: |animation error| {Invariant(DistributionCard.AbsoluteErrorPercentileMs(m_section, p / 100), "0.00")} ms";
        }
        case DistributionCard.Drift:
          return FrameAt(x) is { } frame ? $"{Heading(frame)}\ndrift {Ms(frame.DriftTicks, sign: true)} ms" : null;
        default:
          return FrameAt(x) is { } shown ? Describe(shown) : null;
      }
    }

    /// <summary>The section's frame shown at <paramref name="seconds"/> (since the run's first frame): the last one first seen by then.</summary>
    public PresentedFrame? FrameAt(double seconds)
    {
      if (m_section.FrameCount == 0)
        return null;
      var frames = m_section.Data.Frames;
      long ticks = m_section.OriginTicks + (long)Math.Round(seconds * TimeSpan.TicksPerSecond);
      int after = RunChartData.FirstWhere(m_section.Start, m_section.End, i => frames[i].FirstSeenTicks > ticks);
      return frames[Math.Max(m_section.Start, after - 1)];
    }

    private string Describe(PresentedFrame frame)
    {
      var lines = new List<string> { Heading(frame) };
      if (frame.DisplayDeltaTicks is { } display)
        lines.Add($"display time step {Ms(display)} ms");
      if (frame.AnimationErrorTicks is { } error)
        lines.Add($"animation error {Ms(error, sign: true)} ms");
      if (frame.LatenessTicks is { } lateness)
        lines.Add($"lateness {Ms(lateness, sign: true)} ms");
      if (frame.FrameTimeTicks is { } frameTime)
        lines.Add($"frametime {Ms(frameTime)} ms");
      if (frame.CpuBusyTicks > 0)
        lines.Add($"CPU busy {Ms(frame.CpuBusyTicks)} ms");
      return string.Join('\n', lines);
    }

    /// <summary>"Frame 1234 at 12.345 s", and what happened to it: late, torn, frame indices skipped before it.</summary>
    private string Heading(PresentedFrame frame)
    {
      double seconds = (frame.FirstSeenTicks - m_section.OriginTicks) / (double)TimeSpan.TicksPerSecond;
      var notes = new List<string>();
      if ((frame.Flags & PresentedFrameFlags.Late) != 0)
        notes.Add("late");
      if ((frame.Flags & PresentedFrameFlags.Torn) != 0)
        notes.Add("torn");
      if (frame.SkippedBefore > 0)
        notes.Add($"{frame.SkippedBefore} frame indices skipped before it");
      return $"Frame {frame.FrameIndex} at {Invariant(seconds, "0.000")} s" + (notes.Count > 0 ? $" ({string.Join(", ", notes)})" : string.Empty);
    }

    private static string? Bin(Histogram histogram, double x, string what, bool sign)
    {
      if (histogram.Total == 0)
        return null;
      double half = histogram.BinWidthMs / 2;
      var bin = histogram.Bins.FirstOrDefault(b => x >= b.CenterMs - half && x < b.CenterMs + half);
      if (bin == null)
        return null;
      string center = sign && bin.CenterMs > 0 ? "+" + Invariant(bin.CenterMs, "0.0#") : Invariant(bin.CenterMs, "0.0#");
      string frames = bin.Count == 1 ? "1 frame" : $"{bin.Count.ToString("N0", CultureInfo.InvariantCulture)} frames";
      return $"{what} {center} ms (bin of {Invariant(histogram.BinWidthMs, "0.0#")} ms): {frames}";
    }

    private static string Ms(long ticks, bool sign = false)
    {
      double ms = ticks / (double)TimeSpan.TicksPerMillisecond;
      return (sign && ms > 0 ? "+" : string.Empty) + Invariant(ms, "0.00");
    }

    private static string Invariant(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
  }
}
