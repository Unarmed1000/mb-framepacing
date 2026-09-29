//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* Analysis page view. The report cards are CardViews bound to the view model's cards; this wires the Timeline card's zoom, pan, scroll and
//* reset to the view model's section, its width to the cards' layout, and every card's hover text to the view model.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
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
      TimelineCardView.ZoomRequested += (at, factor) => m_viewModel?.Zoom(at, factor);
      TimelineCardView.ScrollRequested += fraction => m_viewModel?.Scroll(fraction);
      // The cards are laid out for the room they have: one column per pixel
      TimelineCardView.PropertyChanged += (_, e) =>
      {
        if (e.Property == BoundsProperty)
          m_viewModel?.SetCardWidth(TimelineCardView.Bounds.Width);
      };
      TimelineCardView.ResetRequested += () => m_viewModel?.ResetRangeCommand.Execute(null);
      foreach (
        var view in new[] { TimelineCardView, ErrorHistogramCardView, ErrorPercentilesCardView, DisplayTimeStepHistogramCardView, DriftCardView }
      )
        view.HoverText = (plot, x, y) => m_viewModel?.HoverText(plot, x, y);
      DataContextChanged += (_, _) =>
      {
        m_viewModel = DataContext as AnalysisViewModel;
        if (TimelineCardView.Bounds.Width > 0)
          m_viewModel?.SetCardWidth(TimelineCardView.Bounds.Width);
      };
    }
  }
}
