//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Analysis page view. The charts are ScottPlot controls, drawn by the shared RunCharts (the same charts the report files hold) whenever the
//* selected run or the theme changes (ScottPlot is not bindable). The Timeline plots are linked on their time axis. Reset zoom, or a
//* double-click on a chart, draws them again for the whole run.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using MB.FramePacing.Charts;
using MB.FramePacing.Gui.ViewModels;
using ScottPlot.Avalonia;
using ScottPlot.Interactivity;
using ScottPlot.Interactivity.UserActionResponses;

namespace MB.FramePacing.Gui.Views
{
  public partial class AnalysisView : UserControl
  {
    private AnalysisViewModel? m_viewModel;

    public AnalysisView()
    {
      InitializeComponent();
      LinkTimeline();
      ResetOnDoubleClick();
      DataContextChanged += (_, _) => Attach(DataContext as AnalysisViewModel);
      ActualThemeVariantChanged += (_, _) => Redraw();
    }

    private AvaPlot[] TimelinePlots => new[] { ErrorPlot, DisplayTimeStepPlot, LateSharePlot, RefreshStripPlot };

    private AvaPlot[] AllPlots =>
      TimelinePlots.Concat(new[] { ErrorHistogramPlot, ErrorPercentilePlot, DisplayTimeStepHistogramPlot, DriftPlot }).ToArray();

    /// <summary>Double-clicking a chart resets the zoom (instead of ScottPlot's benchmark overlay), like the Reset zoom button.</summary>
    private void ResetOnDoubleClick()
    {
      foreach (var plot in AllPlots)
      {
        plot.UserInputProcessor.DoubleLeftClickBenchmark(false);
        plot.UserInputProcessor.UserActionResponses.Add(new DoubleClickResponse(StandardMouseButtons.Left, (_, _) => Redraw()));
      }
    }

    /// <summary>Back to the whole run: every chart is drawn again with its own limits.</summary>
    private void OnResetZoom(object? sender, RoutedEventArgs e) => Redraw();

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
      var run = m_viewModel?.SelectedRun?.Chart;
      var theme = ActualThemeVariant == ThemeVariant.Dark ? ChartTheme.Dark : ChartTheme.Light;
      RunCharts.Timeline(run, theme, ErrorPlot.Plot, DisplayTimeStepPlot.Plot, LateSharePlot.Plot, RefreshStripPlot.Plot);
      RunCharts.ErrorHistogram(run, theme, ErrorHistogramPlot.Plot);
      RunCharts.ErrorPercentiles(run, theme, ErrorPercentilePlot.Plot);
      RunCharts.DisplayTimeStepHistogram(run, theme, DisplayTimeStepHistogramPlot.Plot);
      RunCharts.Drift(run, theme, DriftPlot.Plot);
      foreach (var plot in AllPlots)
        plot.Refresh();
    }
  }
}
