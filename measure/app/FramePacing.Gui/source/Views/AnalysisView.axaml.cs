//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Analysis page view. The charts are ScottPlot controls, redrawn whenever the selected run changes (ScottPlot is not bindable). The Timeline
//* tab stacks the animation error, its causes (display time and animation time step), the late share and the refresh strip on one linked
//* time axis.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Styling;
using MB.FramePacing.Analysis;
using MB.FramePacing.Gui.ViewModels;
using ScottPlot.Avalonia;

namespace MB.FramePacing.Gui.Views
{
  public partial class AnalysisView : UserControl
  {
    // The Timeline plots share this left axis width, so their data areas line up
    private const float TimelineLeftAxisPixels = 70;

    private static readonly ScottPlot.Color g_lateColor = ScottPlot.Color.FromHex("#E4572E");

    private AnalysisViewModel? m_viewModel;

    public AnalysisView()
    {
      InitializeComponent();
      LinkTimeline();
      DataContextChanged += (_, _) => Attach(DataContext as AnalysisViewModel);
      ActualThemeVariantChanged += (_, _) => Redraw();
    }

    private AvaPlot[] TimelinePlots => new[] { ErrorPlot, DisplayAnimationPlot, LateSharePlot, RefreshStripPlot };

    /// <summary>Panning or zooming one Timeline plot moves the others along the time axis.</summary>
    private void LinkTimeline()
    {
      var plots = TimelinePlots;
      foreach (var plot in plots)
      {
        foreach (var other in plots.Where(other => other != plot))
          plot.Plot.Axes.Link(other, x: true, y: false);
      }
    }

    private void Attach(AnalysisViewModel? viewModel)
    {
      if (m_viewModel != null)
        m_viewModel.PropertyChanged -= OnViewModelPropertyChanged;
      m_viewModel = viewModel;
      if (m_viewModel != null)
        m_viewModel.PropertyChanged += OnViewModelPropertyChanged;
      Redraw();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
      if (e.PropertyName == nameof(AnalysisViewModel.SelectedRun))
        Redraw();
    }

    private void Redraw()
    {
      var run = m_viewModel?.SelectedRun;
      var frames = run?.Run.Frames.ToArray() ?? Array.Empty<PresentedFrame>();
      long origin = frames.Length > 0 ? frames[0].FirstSeenTicks : 0;
      double Seconds(PresentedFrame f) => (f.FirstSeenTicks - origin) / (double)TimeSpan.TicksPerSecond;

      var withMetrics = frames.Where(f => f.AnimationErrorTicks.HasValue).ToArray();

      DrawTimeline(run, frames, withMetrics, Seconds);

      long periodTicks = run != null ? (long)Math.Round(run.CapturePeriodMs * TimeSpan.TicksPerMillisecond) : 0;
      var histograms = run != null ? RunHistograms.Create(run.Run, periodTicks) : null;

      // How often each animation error occurs; bins are one capture period wide, so each bar is one measurable value
      Reset(ErrorHistogramPlot, "Animation error distribution", "presented frames (log scale)", "animation error (ms)");
      if (histograms != null && histograms.AnimationErrorMs.Total > 0)
      {
        AddLogBars(ErrorHistogramPlot, histograms.AnimationErrorMs);
        AddThresholdLines(ErrorHistogramPlot, run!, vertical: true);
      }
      Finish(
        ErrorHistogramPlot,
        plot =>
        {
          // Centred on zero: too soon on the right, too late on the left
          var x = plot.Axes.GetLimits();
          double half = Math.Max(Math.Abs(x.Left), Math.Abs(x.Right));
          plot.Axes.SetLimitsX(-half, half);
        }
      );

      // |animation error| by percentile: how bad the worst frames are
      Reset(ErrorPercentilePlot, "Animation error by percentile", "|animation error| (ms)", "percentile of presented frames");
      if (withMetrics.Length > 0)
      {
        var sorted = withMetrics.Select(f => Ms(Math.Abs(f.AnimationErrorTicks!.Value))).Order().ToArray();
        var percentiles = Enumerable.Range(0, 1001).Select(i => i / 10.0).ToArray();
        var curve = ErrorPercentilePlot.Plot.Add.Scatter(percentiles, percentiles.Select(p => Statistics.Percentile(sorted, p / 100)).ToArray());
        curve.MarkerSize = 0;
        curve.LineWidth = 2;
        curve.LegendText = "|animation error|";
        AddThresholdLines(ErrorPercentilePlot, run!, vertical: false, symmetric: false);
        foreach (double p in new[] { 95.0, 99.0 })
          ErrorPercentilePlot.Plot.Add.VerticalLine(p, color: ScottPlot.Colors.Gray, pattern: ScottPlot.LinePattern.Dotted).Text = $"p{p:0}";
        ErrorPercentilePlot.Plot.ShowLegend();
      }
      Finish(ErrorPercentilePlot, plot => plot.Axes.SetLimitsX(0, 100));

      // How long frames stayed on screen: steady pacing is one tall bar, stutter shows up as bars at multiples of it
      Reset(
        DisplayTimeHistogramPlot,
        "Display time distribution (how long each frame stayed on screen)",
        "presented frames (log scale)",
        "display time (ms)"
      );
      if (histograms != null && histograms.DisplayDeltaMs.Total > 0)
      {
        AddLogBars(DisplayTimeHistogramPlot, histograms.DisplayDeltaMs);
        double median = run!.Run.Statistics.DisplayDeltaMs.P50;
        var line = DisplayTimeHistogramPlot.Plot.Add.VerticalLine(median, color: ScottPlot.Colors.Orange, pattern: ScottPlot.LinePattern.Dashed);
        line.LegendText = $"median {median:0.##} ms";
        DisplayTimeHistogramPlot.Plot.ShowLegend();
      }
      Finish(DisplayTimeHistogramPlot, plot => plot.Axes.SetLimitsX(0, plot.Axes.GetLimits().Right));

      // Cumulative drift
      Reset(DriftPlot, "Cumulative drift (animation time - display time)", "drift (ms)");
      if (frames.Length > 0)
      {
        var drift = DriftPlot.Plot.Add.Scatter(frames.Select(Seconds).ToArray(), frames.Select(f => Ms(f.DriftTicks)).ToArray());
        drift.MarkerSize = 2;
      }
      Finish(DriftPlot);
    }

    /// <summary>
    /// The Timeline tab: the animation error on top, the display time and animation time step that cause it below (error while the display
    /// time stays flat is delta time jitter, error where it jumps is bad pacing), the share of late frames over the last seconds, and the
    /// refresh strip. Late frames are red throughout.
    /// </summary>
    private void DrawTimeline(RunViewModel? run, PresentedFrame[] frames, PresentedFrame[] withMetrics, Func<PresentedFrame, double> seconds)
    {
      static bool IsLate(PresentedFrame f) => f.Flags.HasFlag(PresentedFrameFlags.Late);
      double[] times = withMetrics.Select(seconds).ToArray();
      var late = withMetrics.Where(IsLate).ToArray();
      double[] lateTimes = late.Select(seconds).ToArray();
      var pacing = run?.Run.Pacing;

      Reset(ErrorPlot, "Animation error per presented frame", "error (ms)", string.Empty);
      if (withMetrics.Length > 0)
      {
        var errors = ErrorPlot.Plot.Add.Scatter(times, withMetrics.Select(f => Ms(f.AnimationErrorTicks!.Value)).ToArray());
        errors.LineWidth = 0;
        errors.MarkerSize = 4;
        errors.LegendText = "animation error";
        AddLateMarkers(ErrorPlot, lateTimes, late.Select(f => Ms(f.AnimationErrorTicks!.Value)).ToArray());
        AddThresholdLines(ErrorPlot, run!, vertical: false, symmetric: true);
        TimelineLegend(ErrorPlot);
      }

      Reset(DisplayAnimationPlot, "Display time and animation time step (their difference is the error)", "ms", string.Empty);
      if (withMetrics.Length > 0)
      {
        var display = DisplayAnimationPlot.Plot.Add.Scatter(times, withMetrics.Select(f => Ms(f.DisplayDeltaTicks!.Value)).ToArray());
        display.LegendText = "display time";
        display.MarkerSize = 3;
        var animation = DisplayAnimationPlot.Plot.Add.Scatter(times, withMetrics.Select(f => Ms(f.AnimationDeltaTicks!.Value)).ToArray());
        animation.LegendText = "animation time step";
        animation.MarkerSize = 3;
        AddLateMarkers(DisplayAnimationPlot, lateTimes, late.Select(f => Ms(f.DisplayDeltaTicks!.Value)).ToArray());
        if (pacing != null)
          DisplayAnimationPlot
            .Plot.Add.HorizontalLine(pacing.TargetFrameMs, color: ScottPlot.Colors.Gray, pattern: ScottPlot.LinePattern.Dotted)
            .LegendText = $"target {pacing.TargetFrameMs.ToString("0.##", CultureInfo.InvariantCulture)} ms";
        TimelineLegend(DisplayAnimationPlot);
      }

      Reset(
        LateSharePlot,
        $"Share of late frames in the last {LateShare.WindowSeconds:0} s: rare spikes or busy stretches?",
        "late (%)",
        string.Empty
      );
      double maxShare = 0;
      if (frames.Length > 0 && pacing != null)
      {
        var shares = LateShare.Rolling(frames, LateShare.WindowTicks).Select(s => s * 100).ToArray();
        maxShare = shares.Max();
        var line = LateSharePlot.Plot.Add.Scatter(frames.Select(seconds).ToArray(), shares);
        line.MarkerSize = 0;
        line.LineWidth = 2;
        line.Color = g_lateColor;
        line.LegendText = $"late frames (whole run {pacing.LateShare.ToString("P1", CultureInfo.InvariantCulture)})";
        TimelineLegend(LateSharePlot);
      }

      Reset(RefreshStripPlot, "Refresh strip: one cell per refresh, shaded by frame; zoom in to see the holds", string.Empty);
      RefreshStripPlot.Plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericManual();
      if (frames.Length > 0 && pacing != null)
        RefreshStripPlot.Plot.Add.Plottable(CreateRefreshStrip(run!, frames, seconds, pacing));

      double start = frames.Length > 0 ? seconds(frames[0]) : 0;
      double end = frames.Length > 0 ? Math.Max(start + 0.001, seconds(frames[^1]) + (run!.CapturePeriodMs / 1000)) : 1;
      foreach (var plot in TimelinePlots)
      {
        plot.Plot.Axes.Left.MinimumSize = TimelineLeftAxisPixels;
        Finish(plot, p => p.Axes.SetLimitsX(start, end));
      }
      LateSharePlot.Plot.Axes.SetLimitsY(0, Math.Max(5, maxShare * 1.5));
      RefreshStripPlot.Plot.Axes.SetLimitsY(0, 1);
      LateSharePlot.Refresh();
      RefreshStripPlot.Refresh();
    }

    private RefreshStripPlottable CreateRefreshStrip(
      RunViewModel run,
      PresentedFrame[] frames,
      Func<PresentedFrame, double> seconds,
      RunPacing pacing
    )
    {
      bool dark = ActualThemeVariant == ThemeVariant.Dark;
      return new RefreshStripPlottable(frames, seconds, pacing.RefreshPeriodMs / 1000, run.CapturePeriodMs / 1000, run.IsCamera)
      {
        EvenColor = ScottPlot.Color.FromHex(dark ? "#4C8DD6" : "#2F6DB5"),
        OddColor = ScottPlot.Color.FromHex(dark ? "#8DB8E8" : "#9CC2EC"),
        LateColor = g_lateColor,
        UnknownColor = ScottPlot.Color.FromHex(dark ? "#3A3E45" : "#D5D8DD"),
        EdgeColor = ScottPlot.Color.FromHex(dark ? "#202328" : "#FFFFFF"),
        MarkColor = ScottPlot.Color.FromHex(dark ? "#D6DAE0" : "#2B2F36"),
      };
    }

    /// <summary>The Timeline's legends: one line at the top left, with room above the data so they do not hide it.</summary>
    private static void TimelineLegend(AvaPlot plot)
    {
      plot.Plot.ShowLegend(ScottPlot.Alignment.UpperLeft, ScottPlot.Orientation.Horizontal);
      plot.Plot.Axes.Margins(bottom: 0.08, top: 0.3);
    }

    private static void AddLateMarkers(AvaPlot plot, double[] times, double[] values)
    {
      if (times.Length == 0)
        return;
      var markers = plot.Plot.Add.Scatter(times, values);
      markers.LineWidth = 0;
      markers.MarkerSize = 6;
      markers.Color = g_lateColor;
      markers.LegendText = "late frame";
    }

    private static double Ms(long ticks) => ticks / (double)TimeSpan.TicksPerMillisecond;

    private void Reset(AvaPlot plot, string title, string yLabel, string xLabel = "time since the first frame (s)")
    {
      plot.Plot.Clear();
      plot.Plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic();
      ApplyTheme(plot);
      plot.Plot.Title(title);
      plot.Plot.XLabel(xLabel);
      plot.Plot.YLabel(yLabel);
    }

    /// <summary>
    /// Histogram bars on a logarithmic count axis, so a handful of bad frames stay visible next to thousands of good ones.
    /// ScottPlot has no log axis: the bars hold log10(count) and the tick labels are transformed back.
    /// </summary>
    private static void AddLogBars(AvaPlot plot, Histogram histogram)
    {
      const double Base = -0.3; // just below log10(1) = 0, so single frames get a visible bar
      var bars = histogram
        .Bins.Where(b => b.Count > 0)
        .Select(b => new ScottPlot.Bar
        {
          Position = b.CenterMs,
          Value = Math.Log10(b.Count),
          ValueBase = Base,
          Size = histogram.BinWidthMs * 0.9,
          FillColor = ScottPlot.Colors.SteelBlue,
          LineWidth = 0,
        })
        .ToList();
      plot.Plot.Add.Bars(bars);
      plot.Plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic
      {
        IntegerTicksOnly = true,
        MinorTickGenerator = new ScottPlot.TickGenerators.LogMinorTickGenerator(),
        LabelFormatter = y => Math.Pow(10, y).ToString("N0", CultureInfo.CurrentCulture),
      };
      plot.Plot.Axes.Margins(bottom: 0);
    }

    /// <summary>
    /// The error threshold: errors inside it are not counted. A capture card sees whole refreshes, so it is half a refresh (one missed refresh
    /// is outside); an EXPERIMENTAL camera films asynchronously, so it is one camera period (its measurement resolution).
    /// </summary>
    private static void AddThresholdLines(AvaPlot plot, RunViewModel run, bool vertical, bool symmetric = true)
    {
      var color = ScottPlot.Colors.Orange;
      double threshold = run.ErrorThresholdMs;
      string legend = run.IsCamera ? "1 camera period (the measurement resolution)" : "½ refresh (errors are whole refreshes)";
      if (vertical)
      {
        plot.Plot.Add.VerticalLine(threshold, color: color, pattern: ScottPlot.LinePattern.Dashed).LegendText = "±" + legend;
        plot.Plot.Add.VerticalLine(-threshold, color: color, pattern: ScottPlot.LinePattern.Dashed);
      }
      else
      {
        plot.Plot.Add.HorizontalLine(threshold, color: color, pattern: ScottPlot.LinePattern.Dashed).LegendText = (symmetric ? "±" : "") + legend;
        if (symmetric)
          plot.Plot.Add.HorizontalLine(-threshold, color: color, pattern: ScottPlot.LinePattern.Dashed);
      }
      plot.Plot.ShowLegend();
    }

    /// <summary>Match the chart colours to the application's light or dark theme.</summary>
    private void ApplyTheme(AvaPlot plot)
    {
      bool dark = ActualThemeVariant == ThemeVariant.Dark;
      var background = ScottPlot.Color.FromHex(dark ? "#202328" : "#FFFFFF");
      var foreground = ScottPlot.Color.FromHex(dark ? "#D6DAE0" : "#2B2F36");
      var grid = ScottPlot.Color.FromHex(dark ? "#33373E" : "#E6E8EC");
      plot.Plot.FigureBackground.Color = background;
      plot.Plot.DataBackground.Color = background;
      plot.Plot.Axes.Color(foreground);
      plot.Plot.Grid.MajorLineColor = grid;
      plot.Plot.Legend.BackgroundColor = background;
      plot.Plot.Legend.FontColor = foreground;
      plot.Plot.Legend.OutlineColor = grid;
    }

    private static void Finish(AvaPlot plot, Action<ScottPlot.Plot>? adjustLimits = null)
    {
      plot.Plot.Axes.AutoScale();
      if (plot.Plot.PlottableList.Count > 0)
        adjustLimits?.Invoke(plot.Plot);
      plot.Refresh();
    }
  }
}
