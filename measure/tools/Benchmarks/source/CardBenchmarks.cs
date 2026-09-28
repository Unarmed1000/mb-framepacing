//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* What the GUI does on every zoom and pan of an hour at 240 Hz (864,000 frames): cut the section (its statistics included) and build its
//* Timeline card (the report's panels) and its four distribution cards. A frame of the GUI is 16 ms.
//*
//* (c) 2026 Mana Battery
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

    [Params(3600.0, 60.0, 2.0)]
    public double Seconds { get; set; }

    [GlobalSetup]
    public void Setup()
    {
      m_run = SyntheticHour.Create();
      m_whole = RunSection.Whole(m_run);
    }

    private RunSection Section() => Seconds >= m_whole.ToSeconds ? m_whole : RunSection.Create(m_run, 1200, 1200 + Seconds);

    [Benchmark]
    public RunSection CutSection() => Section();

    [Benchmark]
    public CardDrawing TimelineCard() => ReportCard.Build(Section(), g_panels);

    [Benchmark]
    public int DistributionCards()
    {
      var section = Section();
      int shapes = 0;
      foreach (var (id, _) in DistributionCard.All)
        shapes += DistributionCard.Build(id, section).Shapes.Count;
      return shapes;
    }
  }
}
