//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Analysis page view. The report cards are CardViews bound to the view model's cards; this wires the Timeline card's zoom, pan and reset to
//* the view model's section, and every card's hover text to the view model.
//*
//* (c) 2026 Mana Battery
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using Avalonia.Controls;
using MB.FramePacing.Gui.ViewModels;

namespace MB.FramePacing.Gui.Views
{
  public partial class AnalysisView : UserControl
  {
    private AnalysisViewModel? m_viewModel;

    public AnalysisView()
    {
      InitializeComponent();
      TimelineCardView.RangeRequested += (from, to) => m_viewModel?.ShowRange(from, to);
      TimelineCardView.ResetRequested += () => m_viewModel?.ResetRangeCommand.Execute(null);
      foreach (
        var view in new[] { TimelineCardView, ErrorHistogramCardView, ErrorPercentilesCardView, DisplayTimeStepHistogramCardView, DriftCardView }
      )
        view.HoverText = (plot, x, y) => m_viewModel?.HoverText(plot, x, y);
      DataContextChanged += (_, _) => m_viewModel = DataContext as AnalysisViewModel;
    }
  }
}
