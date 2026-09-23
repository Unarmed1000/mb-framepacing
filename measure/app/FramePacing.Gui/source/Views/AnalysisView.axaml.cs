//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Analysis page view. The charts are ScottPlot controls, redrawn whenever the selected run changes (ScottPlot is not bindable).
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

using System;
using System.ComponentModel;
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
    private AnalysisViewModel? m_viewModel;

    public AnalysisView()
    {
      InitializeComponent();
      DataContextChanged += (_, _) => Attach(DataContext as AnalysisViewModel);
      ActualThemeVariantChanged += (_, _) => Redraw();
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
      static double Ms(long ticks) => ticks / (double)TimeSpan.TicksPerMillisecond;

      var withMetrics = frames.Where(f => f.AnimationErrorTicks.HasValue).ToArray();
      double[] times = withMetrics.Select(Seconds).ToArray();

      // Animation error with the measurement resolution band (+- one capture period)
      Reset(ErrorPlot, "Animation error per presented frame", "animation error (ms)");
      if (withMetrics.Length > 0)
      {
        var errors = ErrorPlot.Plot.Add.Scatter(times, withMetrics.Select(f => Ms(f.AnimationErrorTicks!.Value)).ToArray());
        errors.LineWidth = 0;
        errors.MarkerSize = 4;
        errors.LegendText = "animation delta - display delta";
        double resolution = run!.CapturePeriodMs;
        var bandColor = ScottPlot.Colors.Orange;
        var upper = ErrorPlot.Plot.Add.HorizontalLine(resolution, color: bandColor, pattern: ScottPlot.LinePattern.Dashed);
        upper.LegendText = "+- capture period (measurement resolution)";
        ErrorPlot.Plot.Add.HorizontalLine(-resolution, color: bandColor, pattern: ScottPlot.LinePattern.Dashed);
        ErrorPlot.Plot.ShowLegend();
      }
      Finish(ErrorPlot);

      long periodTicks = run != null ? (long)Math.Round(run.CapturePeriodMs * TimeSpan.TicksPerMillisecond) : 0;
      var histograms = run != null ? RunHistograms.Create(run.Run, periodTicks) : null;

      // How often each animation error occurs; bins are one capture period wide, so each bar is one measurable value
      Reset(ErrorHistogramPlot, "Animation error distribution", "presented frames (log scale)", "animation error (ms)");
      if (histograms != null && histograms.AnimationErrorMs.Total > 0)
      {
        AddLogBars(ErrorHistogramPlot, histograms.AnimationErrorMs);
        AddResolutionLines(ErrorHistogramPlot, run!.CapturePeriodMs, vertical: true);
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
        AddResolutionLines(ErrorPercentilePlot, run!.CapturePeriodMs, vertical: false);
        foreach (double p in new[] { 95.0, 99.0 })
          ErrorPercentilePlot.Plot.Add.VerticalLine(p, color: ScottPlot.Colors.Gray, pattern: ScottPlot.LinePattern.Dotted).Text = $"p{p:0}";
        ErrorPercentilePlot.Plot.ShowLegend();
      }
      Finish(ErrorPercentilePlot, plot => plot.Axes.SetLimitsX(0, 100));

      // Display delta vs animation delta
      Reset(FrameTimePlot, "Frame times", "ms");
      if (withMetrics.Length > 0)
      {
        var display = FrameTimePlot.Plot.Add.Scatter(times, withMetrics.Select(f => Ms(f.DisplayDeltaTicks!.Value)).ToArray());
        display.LegendText = "display delta (captured)";
        display.MarkerSize = 3;
        var animation = FrameTimePlot.Plot.Add.Scatter(times, withMetrics.Select(f => Ms(f.AnimationDeltaTicks!.Value)).ToArray());
        animation.LegendText = "animation delta (marker)";
        animation.MarkerSize = 3;
        FrameTimePlot.Plot.ShowLegend();
      }
      Finish(FrameTimePlot);

      // How long frames stayed on screen: steady pacing is one tall bar, stutter shows up as bars at multiples of it
      Reset(FrameTimeHistogramPlot, "Frame time distribution (captured display delta)", "presented frames (log scale)", "display delta (ms)");
      if (histograms != null && histograms.DisplayDeltaMs.Total > 0)
      {
        AddLogBars(FrameTimeHistogramPlot, histograms.DisplayDeltaMs);
        double median = run!.Run.Statistics.DisplayDeltaMs.P50;
        var line = FrameTimeHistogramPlot.Plot.Add.VerticalLine(median, color: ScottPlot.Colors.Orange, pattern: ScottPlot.LinePattern.Dashed);
        line.LegendText = $"median {median:0.##} ms";
        FrameTimeHistogramPlot.Plot.ShowLegend();
      }
      Finish(FrameTimeHistogramPlot, plot => plot.Axes.SetLimitsX(0, plot.Axes.GetLimits().Right));

      // Cumulative drift
      Reset(DriftPlot, "Cumulative drift (animation time - display time)", "drift (ms)");
      if (frames.Length > 0)
      {
        var drift = DriftPlot.Plot.Add.Scatter(frames.Select(Seconds).ToArray(), frames.Select(f => Ms(f.DriftTicks)).ToArray());
        drift.MarkerSize = 2;
      }
      Finish(DriftPlot);
    }

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
        LabelFormatter = y => Math.Pow(10, y).ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
      };
      plot.Plot.Axes.Margins(bottom: 0);
    }

    /// <summary>The +- one capture period band: differences smaller than this are below the measurement resolution.</summary>
    private static void AddResolutionLines(AvaPlot plot, double capturePeriodMs, bool vertical)
    {
      var color = ScottPlot.Colors.Orange;
      if (vertical)
      {
        plot.Plot.Add.VerticalLine(capturePeriodMs, color: color, pattern: ScottPlot.LinePattern.Dashed).LegendText =
          "+- capture period (measurement resolution)";
        plot.Plot.Add.VerticalLine(-capturePeriodMs, color: color, pattern: ScottPlot.LinePattern.Dashed);
      }
      else
      {
        plot.Plot.Add.HorizontalLine(capturePeriodMs, color: color, pattern: ScottPlot.LinePattern.Dashed).LegendText =
          "capture period (measurement resolution)";
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
