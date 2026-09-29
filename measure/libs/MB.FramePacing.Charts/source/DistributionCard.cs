//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The distributions of a run, or a section of one, as cards in the report's style (ReportCard): the animation error histogram and the
//* display time step histogram (the fixed 0.1 ms bins of every capture source, counts on a log scale so a handful of bad frames stay visible
//* next to thousands of good ones), the |animation error| by percentile, and the cumulative drift over time.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MB.FramePacing.Analysis;
using static MB.FramePacing.Charts.SvgMarkup;

namespace MB.FramePacing.Charts
{
  public static class DistributionCard
  {
    public const string ErrorHistogram = "error-histogram";
    public const string DisplayTimeStepHistogram = "display-time-step-histogram";
    public const string ErrorPercentiles = "error-percentiles";
    public const string Drift = "drift";

    /// <summary>The bottom of a log count axis: just below log10(1) = 0, so a bin with one frame still gets a bar.</summary>
    public const double LogBase = -0.3;

    /// <summary>The percentiles the error percentile curve is drawn at: 0 to 100 in steps of 0.1.</summary>
    public static readonly IReadOnlyList<double> CurvePercentiles = Enumerable.Range(0, 1001).Select(i => i / 10.0).ToArray();

    /// <summary>Every card, in the order the files and the GUI show them, with what it is.</summary>
    public static readonly IReadOnlyList<(string Id, string Description)> All = new[]
    {
      (ErrorHistogram, "how often each animation error occurs"),
      (DisplayTimeStepHistogram, "how long the frames stayed on screen"),
      (ErrorPercentiles, "|animation error| by percentile"),
      (Drift, "cumulative drift: animation time minus display time"),
    };

    private const double PlotX0 = ReportCard.PlotX0;
    private const double PlotH = 300;
    private const double BarShare = 0.9;
    private static readonly double[] g_percentileMarks = { 95, 99, 99.9 };

    public static bool IsKnown(string id) => All.Any(card => card.Id == id);

    /// <summary>The SVG of card <paramref name="id"/> of <paramref name="section"/>; <paramref name="background"/> as <see cref="ReportCard.Render"/>.</summary>
    public static string Render(string id, RunSection section, string? background = null) => SvgCardWriter.Write(Build(id, section), background);

    /// <summary>Card <paramref name="id"/> of <paramref name="section"/> as shapes, <paramref name="width"/> units wide.</summary>
    public static CardDrawing Build(string id, RunSection section, double width = ReportCard.Width) =>
      id switch
      {
        ErrorHistogram => BuildErrorHistogram(section, width),
        DisplayTimeStepHistogram => BuildDisplayTimeStepHistogram(section, width),
        ErrorPercentiles => BuildErrorPercentiles(section, width),
        Drift => BuildDrift(section, width),
        _ => throw new ArgumentException($"Unknown card '{id}'. Known: {string.Join(", ", All.Select(c => c.Id))}", nameof(id)),
      };

    /// <summary>|animation error| (ms) at <paramref name="fraction"/> of the section's frames that have one (Statistics.Percentile of them sorted).</summary>
    public static double AbsoluteErrorPercentileMs(RunSection section, double fraction)
    {
      var errors = section.Data.AbsoluteErrors;
      var (start, end) = errors.Of(section.Start, section.End);
      return errors.Values.PercentileMs(start, end, fraction);
    }

    /// <summary>How many of the section's frames have an animation error.</summary>
    public static int ErrorCount(RunSection section) => section.Data.Errors.Frames.CountIn(section.Start, section.End);

    private static CardDrawing BuildErrorHistogram(RunSection section, double width)
    {
      var histogram = SectionHistograms.AnimationErrorMs(section);
      double threshold = section.Run.ErrorThresholdTicks / (double)TimeSpan.TicksPerMillisecond;
      var card = Card.Start(
        section,
        width,
        "animation error distribution",
        "How often each animation error occurs, in 0.1 ms bins: + shown too soon (right), − shown too late (left).",
        "ANIMATION ERROR DISTRIBUTION",
        $"presented frames on a log scale; dashed: the ±{Ms(threshold)} ms error threshold"
      );
      if (histogram.Total == 0)
        return card.Empty(ErrorHistogram, "no frames with an animation error");

      // Symmetric: too soon on the right, too late on the left; the threshold always inside
      double half = histogram.Bins.Max(b => Math.Abs(b.CenterMs) + (histogram.BinWidthMs / 2));
      half = Math.Max(half, threshold * 1.5) * 1.05;
      var plot = card.Plot(ErrorHistogram, -half, half, LogBase, LogTop(histogram));
      double step = NiceStep(half, 5);
      for (double v = -Math.Floor(half / step) * step; v <= half + 1e-9; v += step)
        card.XTick(plot, v, Math.Abs(v) < 1e-9 ? "0" : $"{Ms(v, sign: true)} ms");
      LogGrid(card, plot);
      Bars(card, plot, histogram);
      foreach (double t in new[] { -threshold, threshold })
        card.Parts.Add(new LineShape("average-line", N(plot.PixelX(t), 1), N(plot.Top, 0), N(plot.PixelX(t), 1), N(plot.Bottom, 0)));
      return card.Finish("animation error (ms)");
    }

    private static CardDrawing BuildDisplayTimeStepHistogram(RunSection section, double width)
    {
      var histogram = SectionHistograms.DisplayDeltaMs(section);
      double refreshMs = RefreshMs(section.Run);
      var steps = section.Data.DisplaySteps;
      var (stepStart, stepEnd) = steps.Of(section.Start, section.End);
      double median = stepEnd > stepStart ? steps.Values.PercentileMs(stepStart, stepEnd, 0.5) : 0;
      var card = Card.Start(
        section,
        width,
        "display time step distribution",
        "How long each frame stayed on screen, in 0.1 ms bins: steady pacing is one tall bar, uneven pacing adds bars at other steps.",
        "DISPLAY TIME STEP DISTRIBUTION",
        histogram.Total > 0 ? $"presented frames on a log scale; dashed: the median, {Ms(median)} ms" : string.Empty
      );
      if (histogram.Total == 0)
        return card.Empty(DisplayTimeStepHistogram, "no frames with a display time step");

      double top = Math.Max(2 * refreshMs, histogram.Bins.Max(b => b.CenterMs + (histogram.BinWidthMs / 2))) + (refreshMs / 2);
      var plot = card.Plot(DisplayTimeStepHistogram, 0, top, LogBase, LogTop(histogram));
      foreach (double position in ChartScale.StepTicks(refreshMs, top))
        card.XTick(plot, position, position == 0 ? "0" : $"{Ms(Math.Round(position, 1))} ms");
      LogGrid(card, plot);
      Bars(card, plot, histogram);
      double x = plot.PixelX(median);
      card.Parts.Add(new LineShape("average-line", N(x, 1), N(plot.Top, 0), N(x, 1), N(plot.Bottom, 0)));
      return card.Finish("display time step (ms)");
    }

    private static CardDrawing BuildErrorPercentiles(RunSection section, double width)
    {
      int count = ErrorCount(section);
      double Percentile(double fraction) => AbsoluteErrorPercentileMs(section, fraction);
      double threshold = section.Run.ErrorThresholdTicks / (double)TimeSpan.TicksPerMillisecond;
      var card = Card.Start(
        section,
        width,
        "animation error by percentile",
        "|animation error| by percentile of the presented frames (every frame with a measured error): how bad the worst frames are.",
        "|ANIMATION ERROR| BY PERCENTILE",
        count > 0
          ? $"dashed: the {Ms(threshold)} ms error threshold; dotted: "
            + string.Join(", ", g_percentileMarks.Select(p => $"p{p.ToString("0.#", CultureInfo.InvariantCulture)} {Ms(Percentile(p / 100))} ms"))
          : string.Empty
      );
      if (count == 0)
        return card.Empty(ErrorPercentiles, "no frames with an animation error");

      double high = Math.Max(Percentile(1), threshold) * 1.1;
      double step = NiceStep(high, 6);
      double top = Math.Ceiling(high / step) * step;
      var plot = card.Plot(ErrorPercentiles, 0, 100, 0, top);
      for (int p = 0; p <= 100; p += 10)
        card.XTick(plot, p, $"p{p}");
      for (double v = step; v <= top + 1e-9; v += step)
        card.YGrid(plot, v, $"{Ms(v)} ms");
      card.YGrid(plot, 0, "0");

      double thresholdY = plot.PixelY(threshold);
      card.Parts.Add(new LineShape("average-line", N(PlotX0, 0), N(thresholdY, 1), N(card.PlotX1, 0), N(thresholdY, 1)));
      card.Parts.Add(new TextShape(PlotX0 + 6, thresholdY - 5, $"{Ms(threshold)} ms error threshold", "average-text", "start"));
      foreach (double p in g_percentileMarks)
      {
        double x = plot.PixelX(p);
        card.Parts.Add(new LineShape("vsync", N(x, 1), N(plot.Top, 0), N(x, 1), N(plot.Bottom, 0)));
      }
      var d = new StringBuilder();
      foreach (double p in CurvePercentiles)
        d.Append(d.Length == 0 ? 'M' : 'L').Append($"{Fixed(plot.PixelX(p), 1)} {Fixed(plot.PixelY(Percentile(p / 100)), 1)}");
      ReportCard.AddPath(card.Parts, "curve", d);
      return card.Finish("percentile of the presented frames");
    }

    private static CardDrawing BuildDrift(RunSection section, double width)
    {
      var data = section.Data;
      var drift = data.Drift;
      int frameCount = section.FrameCount;
      var card = Card.Start(
        section,
        width,
        "drift",
        "Cumulative drift: animation time minus display time since the run's first frame, per frame at its display time.",
        "CUMULATIVE DRIFT",
        string.Empty
      );
      if (frameCount == 0)
        return card.Empty(Drift, "no presented frames");

      static double TicksMs(long ticks) => ticks / (double)TimeSpan.TicksPerMillisecond;
      // Every frame has a drift: the frames' range is the drifts' range
      double low = Math.Min(0, TicksMs(drift.Values.KthSmallest(section.Start, section.End, 0)));
      double high = Math.Max(0, TicksMs(drift.Values.KthSmallest(section.Start, section.End, frameCount - 1)));
      if (high - low < 1)
        (low, high) = (low - ((1 - (high - low)) / 2), high + ((1 - (high - low)) / 2));
      double pad = (high - low) * 0.1;
      double step = NiceStep(high - low + (2 * pad), 6);
      low = Math.Floor((low - pad) / step) * step;
      high = Math.Ceiling((high + pad) / step) * step;
      var plot = card.Plot(Drift, section.FromSeconds, section.ToSeconds, low, high);
      for (double v = low; v <= high + 1e-9; v += step)
      {
        if (Math.Abs(v) > 1e-9)
          card.YGrid(plot, v, $"{Ms(v, sign: true)} ms");
      }
      double zeroY = plot.PixelY(0);
      card.Parts.Add(new LineShape("zero-line", N(PlotX0, 0), N(zeroY, 1), N(card.PlotX1, 0), N(zeroY, 1)));
      card.Parts.Add(new TextShape(PlotX0 - 10, zeroY + 4, "0", "vsync-n", "end"));

      double XOfFrame(int index) => plot.PixelX(data.Seconds(index));
      var d = new StringBuilder();
      void Point(double x, double value) => d.Append(d.Length == 0 ? 'M' : 'L').Append($"{Fixed(x, 1)} {Fixed(plot.PixelY(value), 1)}");
      if ((card.PlotX1 - PlotX0) / frameCount >= 1)
      {
        for (int i = section.Start; i < section.End; ++i)
          Point(XOfFrame(i), TicksMs(data.Frames[i].DriftTicks));
      }
      else
      {
        // Per pixel column: from its lowest to its highest drift, so an hour stays a small file
        foreach (var (column, start, end) in PixelColumns.Walk(section.Start, section.End, XOfFrame))
        {
          double min = TicksMs(drift.Values.KthSmallest(start, end, 0));
          double max = TicksMs(drift.Values.KthSmallest(start, end, end - start - 1));
          Point(column + 0.5, min);
          if (max > min)
            Point(column + 0.5, max);
        }
      }
      ReportCard.AddPath(card.Parts, "curve", d);
      ReportCard.TimeTicks(card.Parts, section.FromSeconds, section.ToSeconds, plot.PixelX, plot.Bottom);
      return card.Finish(string.Empty);
    }

    /// <summary>One bar per occupied bin at its centre, <see cref="BarShare"/> of the bin wide (at least a pixel), log10 of its count high.</summary>
    private static void Bars(Card card, CardPlot plot, Histogram histogram)
    {
      double width = Math.Max(1, plot.PixelX(histogram.BinWidthMs * BarShare) - plot.PixelX(0));
      foreach (var bin in histogram.Bins.Where(b => b.Count > 0))
      {
        double y = plot.PixelY(Math.Log10(bin.Count));
        card.Parts.Add(new RectShape("hist-bar", N(plot.PixelX(bin.CenterMs) - (width / 2), 2), N(y, 1), N(width, 2), N(plot.Bottom - y, 1)));
      }
    }

    /// <summary>The top of a log count axis: above the tallest bar, at least one decade.</summary>
    private static double LogTop(Histogram histogram) => Math.Max(1, Math.Ceiling(Math.Log10(histogram.Bins.Max(b => b.Count)) + 0.15));

    /// <summary>A grid line and label at every decade of a log count axis: 1, 10, 100, 1,000...</summary>
    private static void LogGrid(Card card, CardPlot plot)
    {
      for (int decade = 0; decade <= plot.YTo + 1e-9; ++decade)
        card.YGrid(plot, decade, Math.Pow(10, decade).ToString("N0", CultureInfo.InvariantCulture));
    }

    /// <summary>The smallest 1, 2 or 5 times a power of ten that splits <paramref name="range"/> into at most <paramref name="maxSteps"/>.</summary>
    private static double NiceStep(double range, int maxSteps)
    {
      double step = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(range, 1e-6) / maxSteps)));
      foreach (double factor in new[] { 1.0, 2, 5, 10 })
      {
        if (range / (step * factor) <= maxSteps)
          return step * factor;
      }
      return step * 10;
    }

    private static double RefreshMs(ChartRun chart) =>
      chart.Run.Pacing?.RefreshPeriodMs ?? (chart.CapturePeriodTicks / (double)TimeSpan.TicksPerMillisecond);

    /// <summary>A card being built: the header, one plot area under its label, the axes, and the card's height under the plot.</summary>
    private sealed class Card
    {
      private readonly string m_title;
      private readonly List<CardPlot> m_plots = new List<CardPlot>();
      private readonly double m_top;

      private Card(string title, List<CardShape> parts, double top, double width)
      {
        m_title = title;
        Parts = parts;
        m_top = top;
        Width = width;
      }

      public double Width { get; }

      /// <summary>The plot's right edge: 40 units before the card's.</summary>
      public double PlotX1 => Width - (ReportCard.Width - ReportCard.PlotX1);

      public List<CardShape> Parts { get; }

      private double Bottom => m_top + PlotH;

      public static Card Start(RunSection section, double width, string name, string explanation, string label, string note)
      {
        var run = section.Run.Run;
        int count = section.FrameCount;
        string title =
          $"{RunHeadline.Title(run)}{(section.IsWholeRun ? string.Empty : $", {ReportCard.Ms1(section.FromSeconds)}–{ReportCard.Ms1(section.ToSeconds)} s")}: {name}";
        string frames = $"{count.ToString("N0", CultureInfo.InvariantCulture)} presented frames";
        var description = new List<string>
        {
          explanation,
          section.IsWholeRun
            ? $"The whole run: {ReportCard.Ms1(section.ToSeconds)} s, {frames}."
            : $"A section of {ReportCard.Ms1(section.ToSeconds - section.FromSeconds)} s of the run's {ReportCard.Ms1(RunSection.Whole(section.Run).ToSeconds)} s, {frames}.",
        };
        if (RunHeadline.SequenceLine(run) is { } sequence)
          description.Add(sequence);
        var parts = Header(title, description);
        // The last description line, then the panel's label above the plot
        double top = 54 + ((description.Count - 1) * 19) + 58;
        parts.Add(new TextShape(20, top - 16, label, "label", "start"));
        if (note.Length > 0)
          parts.Add(new TextShape(width - (ReportCard.Width - ReportCard.PlotX1), top - 16, note, "vsync-n", "end"));
        return new Card(title, parts, top, width);
      }

      public CardPlot Plot(string id, double xFrom, double xTo, double yFrom, double yTo)
      {
        var plot = new CardPlot(id, PlotX0, m_top, PlotX1, Bottom, xFrom, xTo, yFrom, yTo);
        m_plots.Add(plot);
        return plot;
      }

      /// <summary>A label under the plot at <paramref name="value"/>.</summary>
      public void XTick(CardPlot plot, double value, string label) => Parts.Add(new TextShape(plot.PixelX(value), Bottom + 18, label, "vsync-n"));

      /// <summary>A grid line across the plot at <paramref name="value"/>, labelled on the left.</summary>
      public void YGrid(CardPlot plot, double value, string label)
      {
        double y = plot.PixelY(value);
        Parts.Add(ReportCard.GridLine(y, PlotX1));
        Parts.Add(new TextShape(PlotX0 - 10, y + 4, label, "vsync-n", "end"));
      }

      /// <summary>The card without data: <paramref name="text"/> where the plot would be.</summary>
      public CardDrawing Empty(string id, string text)
      {
        Plot(id, 0, 1, 0, 1);
        Parts.Add(new TextShape(PlotX0, m_top + (PlotH / 2), text, "vsync-n", "start"));
        return Finish(string.Empty);
      }

      /// <summary>The axis title under the tick labels, and the card's height.</summary>
      public CardDrawing Finish(string axisTitle)
      {
        if (axisTitle.Length > 0)
          Parts.Add(new TextShape((PlotX0 + PlotX1) / 2, Bottom + 40, axisTitle, "vsync-n"));
        return new CardDrawing(m_title, Width, Bottom + 64, Parts, m_plots);
      }
    }
  }
}
