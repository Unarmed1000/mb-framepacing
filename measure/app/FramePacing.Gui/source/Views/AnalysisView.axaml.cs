//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Analysis page view. The charts are ScottPlot controls, drawn by the shared RunCharts (the same charts the report files hold) whenever the
//* selected run or the theme changes (ScottPlot is not bindable). The Timeline plots are linked on their time axis.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Styling;
using MB.FramePacing.Charts;
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
      var run = m_viewModel?.SelectedRun?.Chart;
      var theme = ActualThemeVariant == ThemeVariant.Dark ? ChartTheme.Dark : ChartTheme.Light;
      RunCharts.Timeline(run, theme, ErrorPlot.Plot, DisplayAnimationPlot.Plot, LateSharePlot.Plot, RefreshStripPlot.Plot);
      RunCharts.ErrorHistogram(run, theme, ErrorHistogramPlot.Plot);
      RunCharts.ErrorPercentiles(run, theme, ErrorPercentilePlot.Plot);
      RunCharts.DisplayTimeHistogram(run, theme, DisplayTimeHistogramPlot.Plot);
      RunCharts.Drift(run, theme, DriftPlot.Plot);
      foreach (var plot in TimelinePlots.Concat(new[] { ErrorHistogramPlot, ErrorPercentilePlot, DisplayTimeHistogramPlot, DriftPlot }))
        plot.Refresh();
    }
  }
}
