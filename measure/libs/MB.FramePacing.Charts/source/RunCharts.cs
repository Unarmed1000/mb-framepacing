//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The charts of one analysed run, drawn into ScottPlot plots: the GUI passes its controls' plots, the report files new ones. The Timeline
//* stacks the animation error, the display time step, the late share and the refresh strip on one time axis (seconds since the run's first
//* frame); the distributions are the error and display time step histograms, the error percentiles and the drift.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MB.FramePacing.Analysis;
using ScottPlot;

namespace MB.FramePacing.Charts
{
  public static class RunCharts
  {
    // Legend texts, which also name the series
    public const string TargetLegend = "target";
    public const string ErrorPercentileLegend = "|animation error|";

    // The Timeline plots share this left axis width, so their data areas line up
    private const float TimelineLeftAxisPixels = 70;

    /// <summary>
    /// The Timeline (after mb-framepacing-explained's charts): the animation error per frame as signed bars on top, right below it the display
    /// time as held steps (an error while the steps stay flat is delta time jitter, an error at a red step is bad pacing), the share of late
    /// frames over the last seconds, and the refresh strip. Late frames and frames held too long are red throughout. All four share the x range.
    /// </summary>
    public static void Timeline(ChartRun? run, ChartTheme theme, Plot error, Plot displayTimeStep, Plot lateShare, Plot strip)
    {
      var frames = run?.Run.Frames.ToArray() ?? Array.Empty<PresentedFrame>();
      long origin = frames.Length > 0 ? frames[0].FirstSeenTicks : 0;
      double Seconds(PresentedFrame f) => (f.FirstSeenTicks - origin) / (double)TimeSpan.TicksPerSecond;
      var pacing = run?.Run.Pacing;
      long refresh = pacing != null ? Ticks(pacing.RefreshPeriodMs) : run?.CapturePeriodTicks ?? 0;

      Reset(error, theme, "Animation error: + shown too soon, - shown too late (the band is within the threshold)", string.Empty, string.Empty);
      if (frames.Any(f => f.AnimationErrorTicks.HasValue))
      {
        var bars = new AnimationErrorBarsPlottable(frames, origin, refresh, run!.ErrorThresholdTicks)
        {
          BarColor = ChartTheme.Late,
          BandColor = theme.Foreground.WithAlpha(0.08),
          ZeroLineColor = theme.Foreground.WithAlpha(0.45),
          LabelColor = theme.Foreground,
        };
        error.Add.Plottable(bars);
        error.Axes.Left.TickGenerator = ManualTicks(AnimationErrorBarsPlottable.Ticks(bars.LimitMs));
        error.Axes.Margins(bottom: 0, top: 0);
      }

      Reset(
        displayTimeStep,
        theme,
        "Display time step: how long each frame stayed on screen; red held too long, dotted the target",
        string.Empty,
        string.Empty
      );
      var steps = new DisplayTimeStepsPlottable(frames, origin, refresh)
      {
        OnTimeColor = theme.OnTime,
        HeldTooLongColor = ChartTheme.Late,
        RiserColor = theme.Foreground.WithAlpha(0.3),
        LabelColor = theme.Foreground,
      };
      if (steps.StartTicks.Count > 0)
      {
        // Each frame's target, over the hold it measures (from the previous frame): steps where the pacer changes its rate. Behind the holds,
        // so it only shows where they differ
        var targeted = Enumerable
          .Range(1, frames.Length - 1)
          .Where(i => frames[i].TargetTicks.HasValue && frames[i].DisplayDeltaTicks.HasValue && frames[i - 1].Segment == frames[i].Segment)
          .ToArray();
        double topMs = steps.TopMs;
        if (targeted.Length > 0)
        {
          var targetMs = targeted.Select(i => Ms(frames[i].TargetTicks!.Value)).ToArray();
          var target = displayTimeStep.Add.Scatter(targeted.Select(i => Seconds(frames[i - 1])).ToArray(), targetMs);
          target.ConnectStyle = ConnectStyle.StepHorizontal;
          target.MarkerSize = 0;
          target.LinePattern = LinePattern.Dotted;
          target.Color = theme.Foreground.WithAlpha(0.6);
          target.LegendText = TargetLegend;
          topMs = Math.Max(topMs, targetMs.Max() + (steps.RefreshMs / 2));
        }
        displayTimeStep.Add.Plottable(steps);
        displayTimeStep.Axes.Left.TickGenerator = ManualTicks(DisplayTimeStepsPlottable.Ticks(steps.RefreshMs, topMs));
        displayTimeStep.Axes.Margins(bottom: 0, top: 0);
      }

      Reset(
        lateShare,
        theme,
        $"Share of late frames in the last {LateShare.WindowSeconds:0} s: rare spikes or busy stretches?",
        "late (%)",
        string.Empty
      );
      double maxShare = 0;
      if (frames.Length > 0 && pacing != null)
      {
        var shares = LateShare.Rolling(frames, LateShare.WindowTicks).Select(s => s * 100).ToArray();
        maxShare = shares.Max();
        var line = lateShare.Add.Scatter(frames.Select(Seconds).ToArray(), shares);
        line.MarkerSize = 0;
        line.LineWidth = 2;
        line.Color = ChartTheme.Late;
        line.LegendText = $"late frames (whole run {pacing.LateShare.ToString("P1", CultureInfo.InvariantCulture)})";
        TimelineLegend(lateShare);
      }

      Reset(strip, theme, "Refresh strip: one cell per refresh, shaded by frame; zoom in to see the holds", string.Empty);
      strip.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericManual();
      if (frames.Length > 0 && pacing != null)
      {
        strip.Add.Plottable(
          new RefreshStripPlottable(frames, origin, Ticks(pacing.RefreshPeriodMs), run!.CapturePeriodTicks, run.Camera)
          {
            EvenColor = theme.StripEven,
            OddColor = theme.StripOdd,
            LateColor = ChartTheme.Late,
            UnknownColor = theme.StripUnknown,
            EdgeColor = theme.Background,
            MarkColor = theme.Foreground,
          }
        );
      }

      double start = frames.Length > 0 ? Seconds(frames[0]) : 0;
      double end = frames.Length > 0 ? Math.Max(start + 0.001, Seconds(frames[^1]) + (run!.CapturePeriodTicks / (double)TimeSpan.TicksPerSecond)) : 1;
      foreach (var plot in new[] { error, displayTimeStep, lateShare, strip })
      {
        plot.Axes.Left.MinimumSize = TimelineLeftAxisPixels;
        Finish(plot, p => p.Axes.SetLimitsX(start, end));
      }
      lateShare.Axes.SetLimitsY(0, Math.Max(5, maxShare * 1.5));
      strip.Axes.SetLimitsY(0, 1);
    }

    /// <summary>How often each animation error occurs, in the fixed bins every capture source gets; centred on zero.</summary>
    public static void ErrorHistogram(ChartRun? run, ChartTheme theme, Plot plot)
    {
      Reset(plot, theme, "Animation error distribution", "presented frames (log scale)", "animation error (ms)");
      var histogram = run != null ? RunHistograms.Create(run.Run).AnimationErrorMs : Histogram.Empty;
      if (histogram.Total > 0)
      {
        AddLogBars(plot, histogram);
        AddThresholdLines(plot, run!, vertical: true);
      }
      Finish(
        plot,
        p =>
        {
          // Too soon on the right, too late on the left
          var x = p.Axes.GetLimits();
          double half = Math.Max(Math.Abs(x.Left), Math.Abs(x.Right));
          p.Axes.SetLimitsX(-half, half);
        }
      );
    }

    /// <summary>|animation error| by percentile: how bad the worst frames are.</summary>
    public static void ErrorPercentiles(ChartRun? run, ChartTheme theme, Plot plot)
    {
      Reset(plot, theme, "Animation error by percentile", "|animation error| (ms)", "percentile of presented frames");
      var sorted = (run?.Run.Frames ?? Array.Empty<PresentedFrame>())
        .Where(f => f.AnimationErrorTicks.HasValue)
        .Select(f => Ms(Math.Abs(f.AnimationErrorTicks!.Value)))
        .Order()
        .ToArray();
      if (sorted.Length > 0)
      {
        var percentiles = Enumerable.Range(0, 1001).Select(i => i / 10.0).ToArray();
        var curve = plot.Add.Scatter(percentiles, percentiles.Select(p => Analysis.Statistics.Percentile(sorted, p / 100)).ToArray());
        curve.MarkerSize = 0;
        curve.LineWidth = 2;
        curve.LegendText = ErrorPercentileLegend;
        AddThresholdLines(plot, run!, vertical: false, symmetric: false);
        foreach (double p in new[] { 95.0, 99.0 })
          plot.Add.VerticalLine(p, color: Colors.Gray, pattern: LinePattern.Dotted).Text = $"p{p:0}";
        plot.ShowLegend();
      }
      Finish(plot, p => p.Axes.SetLimitsX(0, 100));
    }

    /// <summary>How long frames stayed on screen: steady pacing is one tall bar, stutter shows up as bars at multiples of it.</summary>
    public static void DisplayTimeStepHistogram(ChartRun? run, ChartTheme theme, Plot plot)
    {
      Reset(
        plot,
        theme,
        "Display time step distribution (how long each frame stayed on screen)",
        "presented frames (log scale)",
        "display time step (ms)"
      );
      var histogram = run != null ? RunHistograms.Create(run.Run).DisplayDeltaMs : Histogram.Empty;
      if (histogram.Total > 0)
      {
        AddLogBars(plot, histogram);
        double median = run!.Run.Statistics.DisplayDeltaMs.P50;
        var line = plot.Add.VerticalLine(median, color: Colors.Orange, pattern: LinePattern.Dashed);
        line.LegendText = $"median {median:0.##} ms";
        plot.ShowLegend();
      }
      Finish(plot, p => p.Axes.SetLimitsX(0, p.Axes.GetLimits().Right * 1.05));
    }

    /// <summary>Cumulative drift: animation time minus display time since the run's first frame.</summary>
    public static void Drift(ChartRun? run, ChartTheme theme, Plot plot)
    {
      Reset(plot, theme, "Cumulative drift (animation time - display time)", "drift (ms)");
      var frames = run?.Run.Frames ?? Array.Empty<PresentedFrame>();
      if (frames.Count > 0)
      {
        long origin = frames[0].FirstSeenTicks;
        var drift = plot.Add.Scatter(
          frames.Select(f => (f.FirstSeenTicks - origin) / (double)TimeSpan.TicksPerSecond).ToArray(),
          frames.Select(f => Ms(f.DriftTicks)).ToArray()
        );
        drift.MarkerSize = 2;
      }
      Finish(plot);
    }

    private static void TimelineLegend(Plot plot)
    {
      // One line at the top left, with room above the data so it does not hide it
      plot.ShowLegend(Alignment.UpperLeft, Orientation.Horizontal);
      plot.Axes.Margins(bottom: 0.08, top: 0.3);
    }

    private static ScottPlot.TickGenerators.NumericManual ManualTicks(IEnumerable<(double Position, string Label)> ticks)
    {
      var generator = new ScottPlot.TickGenerators.NumericManual();
      foreach (var (position, label) in ticks)
        generator.AddMajor(position, label);
      return generator;
    }

    /// <summary>
    /// Histogram bars on a logarithmic count axis, so a handful of bad frames stay visible next to thousands of good ones.
    /// ScottPlot has no log axis: the bars hold log10(count) and the tick labels are transformed back.
    /// </summary>
    private static void AddLogBars(Plot plot, Histogram histogram)
    {
      const double Base = -0.3; // just below log10(1) = 0, so single frames get a visible bar
      var bars = histogram
        .Bins.Where(b => b.Count > 0)
        .Select(b => new Bar
        {
          Position = b.CenterMs,
          Value = Math.Log10(b.Count),
          ValueBase = Base,
          Size = histogram.BinWidthMs * 0.9,
          FillColor = Colors.SteelBlue,
          LineWidth = 0,
        })
        .ToList();
      plot.Add.Bars(bars);
      plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic
      {
        IntegerTicksOnly = true,
        MinorTickGenerator = new ScottPlot.TickGenerators.LogMinorTickGenerator(),
        LabelFormatter = y => Math.Pow(10, y).ToString("N0", CultureInfo.CurrentCulture),
      };
      plot.Axes.Margins(bottom: 0);
    }

    /// <summary>The error threshold: errors inside it are not counted as frames with animation error.</summary>
    private static void AddThresholdLines(Plot plot, ChartRun run, bool vertical, bool symmetric = true)
    {
      var color = Colors.Orange;
      double threshold = Ms(run.ErrorThresholdTicks);
      string legend = $"{threshold.ToString("0.###", CultureInfo.CurrentCulture)} ms error threshold";
      if (vertical)
      {
        plot.Add.VerticalLine(threshold, color: color, pattern: LinePattern.Dashed).LegendText = "±" + legend;
        plot.Add.VerticalLine(-threshold, color: color, pattern: LinePattern.Dashed);
      }
      else
      {
        plot.Add.HorizontalLine(threshold, color: color, pattern: LinePattern.Dashed).LegendText = (symmetric ? "±" : "") + legend;
        if (symmetric)
          plot.Add.HorizontalLine(-threshold, color: color, pattern: LinePattern.Dashed);
      }
      plot.ShowLegend();
    }

    private static void Reset(Plot plot, ChartTheme theme, string title, string yLabel, string xLabel = "time since the first frame (s)")
    {
      plot.Clear();
      plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic();
      plot.FigureBackground.Color = theme.Background;
      plot.DataBackground.Color = theme.Background;
      plot.Axes.Color(theme.Foreground);
      plot.Grid.MajorLineColor = theme.Grid;
      plot.Legend.BackgroundColor = theme.Background;
      plot.Legend.FontColor = theme.Foreground;
      plot.Legend.OutlineColor = theme.Grid;
      // Shown again by the charts that have one: a plot redrawn for another run or theme keeps no legend it no longer has
      plot.Legend.IsVisible = false;
      plot.Title(title);
      plot.XLabel(xLabel);
      plot.YLabel(yLabel);
    }

    private static void Finish(Plot plot, Action<Plot>? adjustLimits = null)
    {
      plot.Axes.AutoScale();
      if (plot.PlottableList.Count > 0)
        adjustLimits?.Invoke(plot);
    }

    private static double Ms(long ticks) => ticks / (double)TimeSpan.TicksPerMillisecond;

    private static long Ticks(double ms) => (long)Math.Round(ms * TimeSpan.TicksPerMillisecond);
  }
}
