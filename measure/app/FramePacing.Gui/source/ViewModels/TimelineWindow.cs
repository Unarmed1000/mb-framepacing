//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* The Timeline's sliding window: the section a zoomed Timeline card is built for (the view and a screen more on either side, inside the
//* run) and the range its plots show as built. Scrolling within it only moves the card; its start is snapped to a whole pixel column of the
//* run's time, so every window at one zoom and width draws the same columns. The whole run is a window of its own that never moves.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using System;
using MB.FramePacing.Charts;

namespace MB.FramePacing.Gui.ViewModels
{
  internal sealed class TimelineWindow
  {
    // The screens built on either side of the view; the next window is built when the view comes within half a screen of an edge
    private const double ScreensAside = 1;
    private const double NearEdgeScreens = 0.5;

    private readonly double m_wholeSeconds;
    private readonly double m_width;

    private TimelineWindow(RunSection section, (double From, double To)? visible, double width)
    {
      Section = section;
      Visible = visible;
      m_width = width;
      m_wholeSeconds = RunSection.Whole(section.Run).ToSeconds;
    }

    /// <summary>The section the card is built for.</summary>
    public RunSection Section { get; }

    /// <summary>The range the card's plots show as built (seconds); null for the whole run.</summary>
    public (double From, double To)? Visible { get; }

    /// <summary>What SectionCards.Build takes for the window: null for the whole run.</summary>
    public (RunSection Section, (double From, double To) Visible)? Card => Visible is { } visible ? (Section, visible) : null;

    /// <summary>The window around <paramref name="range"/> (null: the whole run) for a card <paramref name="width"/> units wide.</summary>
    public static TimelineWindow Around(ChartRun chart, (double From, double To)? range, double width)
    {
      if (range is not { } view)
        return new TimelineWindow(RunSection.Whole(chart), null, width);
      double length = view.To - view.From;
      double pixelsPerSecond = ReportCard.PlotWidth(width) / length;
      double from = Math.Round(view.From * pixelsPerSecond) / pixelsPerSecond;
      double whole = RunSection.Whole(chart).ToSeconds;
      var section = RunSection.Create(chart, Math.Max(0, from - (length * ScreensAside)), Math.Min(whole, from + (length * (1 + ScreensAside))));
      return new TimelineWindow(section, (from, from + length), width);
    }

    /// <summary>The view is as long as the window's and the card as wide: this window can show it by moving.</summary>
    public bool SameZoom((double From, double To) view, double width)
    {
      if (Visible is not { } visible || width != m_width)
        return false;
      double length = visible.To - visible.From;
      return Math.Abs(length - (view.To - view.From)) <= 1e-9 * length;
    }

    /// <summary>The window is of <paramref name="chart"/>, at the view's zoom and width, and the view lies inside it.</summary>
    public bool Holds(ChartRun chart, (double From, double To) view, double width)
    {
      if (!ReferenceEquals(Section.Run, chart) || !SameZoom(view, width))
        return false;
      double tolerance = 1e-9 * (view.To - view.From);
      return view.From >= Section.FromSeconds - tolerance && view.To <= Section.ToSeconds + tolerance;
    }

    /// <summary>The view is within half a screen of an edge that is not the run's: time to build the next window.</summary>
    public bool NearEdge((double From, double To) view)
    {
      double margin = (view.To - view.From) * NearEdgeScreens;
      return (Section.FromSeconds > 0 && view.From - Section.FromSeconds < margin)
        || (Section.ToSeconds < m_wholeSeconds && Section.ToSeconds - view.To < margin);
    }

    /// <summary>How far to move the card (card units, to the right) to show <paramref name="view"/>; 0 when this window is not at its zoom.</summary>
    public double OffsetFor((double From, double To) view, double width) =>
      SameZoom(view, width) && Visible is { } visible ? (visible.From - view.From) * ReportCard.PlotWidth(width) / (visible.To - visible.From) : 0;
  }
}
