//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What the GUI does on every zoom and scroll of a long run at 240 Hz (1 hour: 864,000 frames; 10 hours: 8.64 million): cut the section and
//* build its Timeline card (the report's panels, the whole run's scales) and its four distribution cards, with the run's prepared data
//* (RunChartData) already made; the sliding window the GUI builds when zoomed (three screens); and making that data, once per run. A frame
//* of the GUI is 16 ms.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
//****************************************************************************************************************************************************

using BenchmarkDotNet.Attributes;
using MB.FramePacing.Charts;

namespace MB.FramePacing.Benchmarks
{
  [MemoryDiagnoser]
  public class CardBenchmarks
  {
    private static readonly ReportOptions g_panels = ReportOptions.ShowOnly(
      new[] { ReportItem.AnimationError, ReportItem.DisplayTimeStep, ReportItem.FrameTime, ReportItem.LateShare, ReportItem.RefreshStrip }
    );

    private ChartRun m_run = null!;
    private RunSection m_whole = null!;

    [Params(1, 10)]
    public int Hours { get; set; }

    /// <summary>The seconds in view: the whole run (0), a minute or 2 s.</summary>
    [Params(0.0, 60.0, 2.0)]
    public double Seconds { get; set; }

    [GlobalSetup]
    public void Setup()
    {
      m_run = SyntheticHour.Create(240 * 3600 * Hours);
      m_whole = RunSection.Whole(m_run);
      // The data is prepared once per run, when it is first shown
      _ = SectionCards.Build(m_whole, g_panels);
    }

    private RunSection Section() => Seconds <= 0 ? m_whole : RunSection.Create(m_run, 1200, 1200 + Seconds);

    [Benchmark]
    public CardDrawing TimelineCard() => ReportCard.Build(Section(), g_panels, wholeRunScales: true);

    /// <summary>
    /// The GUI's sliding window when zoomed: the view and a screen more on either side, built once per zoom and when a scroll nears its edge
    /// (scrolling within it builds nothing).
    /// </summary>
    [Benchmark]
    public CardDrawing? TimelineWindow() =>
      Seconds <= 0
        ? null
        : ReportCard.Build(RunSection.Create(m_run, 1200 - Seconds, 1200 + (2 * Seconds)), g_panels, true, visible: (1200, 1200 + Seconds));

    [Benchmark]
    public int DistributionCards()
    {
      var section = Section();
      int shapes = 0;
      foreach (var (id, _) in DistributionCard.All)
        shapes += DistributionCard.Build(id, section).Shapes.Count;
      return shapes;
    }

    /// <summary>Preparing the data from scratch (a new run object, so nothing is cached) and drawing the whole run once.</summary>
    [Benchmark]
    public SectionCards FirstShow()
    {
      if (Seconds > 0)
        return null!;
      var run = m_run with { };
      return SectionCards.Build(RunSection.Whole(run), g_panels)!;
    }
  }
}
